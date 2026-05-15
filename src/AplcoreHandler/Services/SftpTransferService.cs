using AplcoreHandler.Models;
using AplcoreHandler.Output;
using Renci.SshNet;

namespace AplcoreHandler.Services;

/// <summary>Result of a batch transfer operation.</summary>
public sealed record TransferArchive(string Name, long SizeBytes);
public sealed record FailedTransferArchive(string Name, long SizeBytes, string Error);

public sealed class TransferResult
{
    public List<TransferArchive> Uploaded { get; } = [];
    public List<TransferArchive> Skipped { get; } = [];
    public List<FailedTransferArchive> Failed { get; } = [];
}

public sealed class SftpTransferService
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);

    private readonly SftpConfig _config;
    private readonly string _effectivePassword;
    private readonly IOutputRenderer _output;

    public SftpTransferService(SftpConfig config, string effectivePassword, IOutputRenderer output)
    {
        _config = config;
        _effectivePassword = effectivePassword;
        _output = output;
    }

    /// <summary>
    /// Uploads all eligible (unshipped) zip files from the given list.
    /// Updates the DB ledger for each successful upload and saves after each.
    /// </summary>
    public TransferResult UploadAll(
        List<FileInfo> zipFiles,
        AplcoreDb db,
        DatabaseService dbService,
        bool dryRun)
    {
        var result = new TransferResult();

        // Filter to unshipped files
        var eligible = new List<FileInfo>();
        foreach (var zip in zipFiles) {
            if (DatabaseService.IsShipped(db, zip.FullName, zip.Length, zip.LastWriteTimeUtc)) {
                if (dryRun)
                    _output.ReportDryRunTransferItem(zip.Name, zip.Length, "already-shipped");
                continue;
            }
            eligible.Add(zip);
        }

        if (dryRun) {
            foreach (var zip in eligible)
                _output.ReportDryRunTransferItem(zip.Name, zip.Length, "would-upload");
            return result;
        }

        if (eligible.Count == 0)
            return result;

        _output.ReportTransferStart(eligible.Count);

        using var client = CreateClient();
        try {
            client.Connect();
        } catch (Exception ex) {
            _output.ReportError($"SFTP connection failed: {ex.Message}");
            foreach (var zip in eligible)
                result.Failed.Add(new FailedTransferArchive(zip.Name, zip.Length, ex.Message));
            _output.ReportTransferSummary(result.Uploaded.Count, result.Skipped.Count, result.Failed.Count);
            return result;
        }

        try {
            // Ensure remote directory exists
            EnsureRemoteDirectory(client, _config.RemotePath);

            for (int i = 0; i < eligible.Count; i++) {
                var zip = eligible[i];
                var remotePath = _config.RemotePath.TrimEnd('/') + "/" + zip.Name;

                _output.ReportTransferProgress(zip.Name, zip.Length, i + 1, eligible.Count);

                try {
                    var outcome = UploadSingle(client, zip, remotePath);

                    switch (outcome) {
                        case UploadOutcome.Uploaded:
                            result.Uploaded.Add(new TransferArchive(zip.Name, zip.Length));
                            _output.ReportTransferResult(zip.Name, "uploaded");
                            // Crash-safe: record + save after each successful upload
                            DatabaseService.RecordShipped(db, zip.FullName, zip.Length, zip.LastWriteTimeUtc);
                            dbService.Save(db);
                            break;
                        case UploadOutcome.SkippedSameSize:
                            result.Skipped.Add(new TransferArchive(zip.Name, zip.Length));
                            _output.ReportTransferResult(zip.Name, "skipped");
                            // Treat remote-exists-same-size as confirmed
                            DatabaseService.RecordShipped(db, zip.FullName, zip.Length, zip.LastWriteTimeUtc);
                            dbService.Save(db);
                            break;
                        case UploadOutcome.ErrorSizeMismatch:
                            var msg = "Remote file exists with different size";
                            result.Failed.Add(new FailedTransferArchive(zip.Name, zip.Length, msg));
                            _output.ReportTransferResult(zip.Name, "error");
                            _output.ReportError($"{zip.Name}: {msg}");
                            break;
                    }
                } catch (Exception ex) {
                    result.Failed.Add(new FailedTransferArchive(zip.Name, zip.Length, ex.Message));
                    _output.ReportTransferResult(zip.Name, "error");
                    _output.ReportError($"Upload failed for {zip.Name}: {ex.Message}");
                }
            }
        } finally {
            client.Disconnect();
        }

        _output.ReportTransferSummary(result.Uploaded.Count, result.Skipped.Count, result.Failed.Count);
        return result;
    }

    private enum UploadOutcome { Uploaded, SkippedSameSize, ErrorSizeMismatch }

    private UploadOutcome UploadSingle(SftpClient client, FileInfo localFile, string remotePath)
    {
        // Check if remote file already exists (conflict policy)
        if (client.Exists(remotePath)) {
            var remoteAttrs = client.GetAttributes(remotePath);
            if (remoteAttrs.Size == localFile.Length)
                return UploadOutcome.SkippedSameSize;
            else
                return UploadOutcome.ErrorSizeMismatch;
        }

        // Upload with retry + exponential backoff
        var delay = InitialRetryDelay;
        for (int attempt = 0; attempt <= MaxRetries; attempt++) {
            try {
                using var stream = new FileStream(localFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                client.UploadFile(stream, remotePath, canOverride: false);
                return UploadOutcome.Uploaded;
            } catch (Exception) when (attempt < MaxRetries) {
                _output.ReportWarning(
                    $"Upload retry for {localFile.Name} in {delay.TotalSeconds:F0}s (attempt {attempt + 1}/{MaxRetries})...");
                Thread.Sleep(delay);
                delay *= 2;
            }
        }

        throw new IOException($"Upload failed after {MaxRetries} retries: {localFile.Name}");
    }

    private static void EnsureRemoteDirectory(SftpClient client, string path)
    {
        if (client.Exists(path))
            return;

        // Build path segments and create from root
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        foreach (var segment in segments) {
            current = current.TrimEnd('/') + "/" + segment;
            if (!client.Exists(current))
                client.CreateDirectory(current);
        }
    }

    private SftpClient CreateClient()
    {
        var connectionInfo = new ConnectionInfo(
            _config.Host,
            _config.Port,
            _config.Username,
            new PasswordAuthenticationMethod(_config.Username, _effectivePassword));

        return new SftpClient(connectionInfo);
    }
}
