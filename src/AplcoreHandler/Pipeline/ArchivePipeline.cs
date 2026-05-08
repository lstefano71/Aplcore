using AplcoreHandler.Models;
using AplcoreHandler.Output;
using AplcoreHandler.Services;

namespace AplcoreHandler.Pipeline;

public sealed class ArchivePipeline
{
  private readonly AppConfig _config;
  private readonly IOutputRenderer _output;
  private readonly bool _dryRun;

  public ArchivePipeline(AppConfig config, IOutputRenderer output, bool dryRun)
  {
    _config = config;
    _output = output;
    _dryRun = dryRun;
  }

  /// <summary>
  /// Runs the full archive pipeline. Returns exit code (0=success, 1=partial, 2=fatal).
  /// </summary>
  public int Run()
  {
    // Ensure target directory exists
    Directory.CreateDirectory(_config.TargetDirectory);

    var targetDirFull = Path.GetFullPath(_config.TargetDirectory);

    // Phase 1: Discover files
    _output.ReportScanStart(_config.SourceDirectories.Length);

    var discovered = FileDiscoveryService.Discover(
        _config.SourceDirectories,
        _config.FilePattern,
        excludeDirectory: targetDirFull,
        _output);

    // Phase 2: Load DB and detect changes
    using var dbService = new DatabaseService(_config.TargetDirectory, _output);
    var db = dbService.AcquireAndLoad();

    var toProcess = DatabaseService.DetectChanges(db, discovered);

    _output.ReportScanResult(discovered.Count, toProcess.Count);

    if (toProcess.Count == 0) {
      _output.ReportSummary(0, 0, 0, 0, 0, _dryRun);
      // Even if no new archives, still run shipment for backlog
      return RunPostArchivePhases(db, dbService, targetDirFull, archiveErrors: 0);
    }

    // Phase 3: Dry run or archive
    if (_dryRun)
      return RunDryRun(db, dbService, toProcess, targetDirFull);

    return RunArchive(db, dbService, toProcess, targetDirFull);
  }

  private int RunDryRun(AplcoreDb db, DatabaseService dbService, List<DiscoveredFile> toProcess, string targetDirFull)
  {
    long totalSourceBytes = 0;
    foreach (var item in toProcess) {
      var key = DatabaseService.NormalizePath(item.File.FullName);
      var reason = db.Entries.ContainsKey(key) ? "changed" : "new";
      _output.ReportDryRunItem(item.File.FullName, item.File.Length, reason);
      totalSourceBytes += item.File.Length;
    }

    _output.ReportSummary(0, 0, 0, totalSourceBytes, 0, dryRun: true);

    // Dry-run shipment + notification phases
    return RunPostArchivePhases(db, dbService, targetDirFull, archiveErrors: 0);
  }

  private int RunArchive(AplcoreDb db, DatabaseService dbService, List<DiscoveredFile> toProcess, string targetDirFull)
  {
    int archived = 0, skipped = 0, errors = 0;
    long totalSourceBytes = 0, totalZipBytes = 0;

    _output.ReportArchiveStart(toProcess.Count);

    for (int i = 0; i < toProcess.Count; i++) {
      var item = toProcess[i];
      var file = item.File;
      _output.ReportArchiveProgress(file.FullName, file.Length, file.LastWriteTimeUtc, i + 1, toProcess.Count);

      try {
        // Extract trailer
        var rawTrailer = TrailerExtractor.Extract(file.FullName);
        TrailerData? trailer = null;

        if (rawTrailer is not null) {
          trailer = MetadataSplitter.Split(rawTrailer);
        } else {
          _output.ReportWarning($"No trailer found in {file.Name}, archiving without metadata.");
        }

        // Create zip archive
        var zipPath = ArchiveService.Archive(
            file,
            _config.TargetDirectory,
            item.Label,
            trailer,
            _config.ZipNameTemplate,
            _output);

        if (zipPath is not null) {
          _output.ReportArchiveComplete(file.Name, rawTrailer is not null, zipPath);

          totalSourceBytes += file.Length;
          var zipInfo = new FileInfo(zipPath);
          totalZipBytes += zipInfo.Length;

          // Update DB after each successful archive (crash-safe)
          dbService.RecordProcessed(db, file);
          dbService.Save(db);

          archived++;
        } else {
          errors++;
        }
      } catch (Exception ex) {
        _output.ReportError($"Unexpected error processing {file.Name}: {ex.Message}");
        errors++;
      }
    }

    _output.ReportSummary(archived, skipped, errors, totalSourceBytes, totalZipBytes, dryRun: false);

    // Post-archive phases (shipment + notification)
    var postExitCode = RunPostArchivePhases(db, dbService, targetDirFull, archiveErrors: errors);

    // Aggregate exit codes: worst wins
    return Math.Max(errors > 0 ? 1 : 0, postExitCode);
  }

  private int RunPostArchivePhases(AplcoreDb db, DatabaseService dbService, string targetDirFull, int archiveErrors)
  {
    int exitCode = archiveErrors > 0 ? 1 : 0;

    // Phase 4: SFTP Shipment (if configured)
    if (_config.Sftp is not null) {
      var sftpPassword = ConfigResolver.ResolveEffectiveSftpPassword(_config.Sftp);

      // Collect all zip files in target directory
      var zipFiles = Directory.GetFiles(targetDirFull, "*.zip")
          .Select(f => new FileInfo(f))
          .OrderBy(f => f.LastWriteTimeUtc)
          .ToList();

      var sftpService = new SftpTransferService(_config.Sftp, sftpPassword, _output);
      var transferResult = sftpService.UploadAll(zipFiles, db, dbService, _dryRun);

      if (transferResult.Failed.Count > 0)
        exitCode = Math.Max(exitCode, 1);

      // Phase 5: Email Notification (if configured and at least one upload succeeded)
      if (_config.Smtp is not null) {
        var smtpPassword = ConfigResolver.ResolveEffectiveSmtpPassword(_config.Smtp);
        var emailSent = NotificationService.SendIfNeeded(
            _config.Smtp, smtpPassword, transferResult, _output, _dryRun);

        if (!_dryRun && transferResult.Uploaded.Count > 0 && !emailSent)
          exitCode = Math.Max(exitCode, 1);
      }
    }

    return exitCode;
  }
}
