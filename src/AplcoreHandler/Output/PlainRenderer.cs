using AplcoreHandler.Models;

namespace AplcoreHandler.Output;

public class PlainRenderer : IOutputRenderer
{
  public virtual void ReportBanner(string version) =>
      Console.WriteLine($"AplcoreHandler v{version}");

  public virtual void ReportConfig(AppConfig config, string configPath, bool dryRun)
  {
    Console.WriteLine($"Config:       {Path.GetFullPath(configPath)}");
    Console.WriteLine($"Pattern:      {config.FilePattern}");
    Console.WriteLine($"Target:       {Path.GetFullPath(config.TargetDirectory)}");
    foreach (var dir in config.SourceDirectories) {
      var label = string.IsNullOrEmpty(dir.Label) ? "" : $" ({dir.Label})";
      Console.WriteLine($"  Source:     {dir.Path}{label}");
    }
    if (dryRun)
      Console.WriteLine("Mode:         DRY RUN");
    Console.WriteLine();
  }

  public virtual void ReportScanStart(int directoryCount) =>
      Console.WriteLine($"Scanning {directoryCount} source director{(directoryCount == 1 ? "y" : "ies")}...");

  public virtual void ReportScanResult(int totalFound, int toProcess) =>
      Console.WriteLine($"Found {totalFound} file(s), {toProcess} to process.");

  public virtual void ReportArchiveStart(int fileCount) =>
      Console.WriteLine($"Archiving {fileCount} file(s)...");

  public virtual void ReportArchiveProgress(string filePath, long sizeBytes, DateTime lastModifiedUtc, int current, int total)
  {
    Console.WriteLine($"[{current}/{total}] {Path.GetFileName(filePath)}");
    Console.WriteLine($"         Path:     {filePath}");
    Console.WriteLine($"         Size:     {FormatSize(sizeBytes)}");
    Console.WriteLine($"         Modified: {lastModifiedUtc:yyyy-MM-dd HH:mm:ss} UTC");
  }

  public virtual void ReportArchiveComplete(string fileName, bool hadTrailer, string zipPath)
  {
    var trailerNote = hadTrailer ? "with metadata" : "no trailer found";
    Console.WriteLine($"  ✓ {fileName} ({trailerNote})");
    Console.WriteLine($"    → {zipPath}");
  }

  public virtual void ReportSummary(int archived, int skipped, int errors, long totalSourceBytes, long totalZipBytes, bool dryRun)
  {
    Console.WriteLine();
    if (dryRun)
      Console.WriteLine("=== DRY RUN SUMMARY ===");
    else
      Console.WriteLine("=== SUMMARY ===");

    Console.WriteLine($"  Archived:     {archived}");
    Console.WriteLine($"  Skipped:      {skipped}");
    Console.WriteLine($"  Errors:       {errors}");
    Console.WriteLine($"  Source total:  {FormatSize(totalSourceBytes)}");
    Console.WriteLine($"  Zip total:    {FormatSize(totalZipBytes)}");
    if (totalSourceBytes > 0)
      Console.WriteLine($"  Compression:  {(1.0 - (double)totalZipBytes / totalSourceBytes) * 100:F1}%");
  }

  public virtual void ReportWarning(string message) =>
      Console.WriteLine($"  ⚠ {message}");

  public virtual void ReportError(string message) =>
      Console.Error.WriteLine($"  ✗ {message}");

  public virtual void ReportDryRunItem(string filePath, long sizeBytes, string reason) =>
      Console.WriteLine($"  → {Path.GetFileName(filePath)} ({FormatSize(sizeBytes)}) [{reason}]");

  public virtual void ReportTransferStart(int fileCount) =>
      Console.WriteLine($"Uploading {fileCount} file(s) via SFTP...");

  public virtual void ReportTransferProgress(string fileName, long sizeBytes, int current, int total) =>
      Console.WriteLine($"[{current}/{total}] {fileName} ({FormatSize(sizeBytes)})");

  public virtual void ReportTransferResult(string fileName, string outcome)
  {
    var symbol = outcome == "error" ? "✗" : "✓";
    Console.WriteLine($"  → {fileName}: {symbol} {outcome}");
  }

  public virtual void ReportTransferSummary(int uploaded, int skipped, int failed)
  {
    Console.WriteLine();
    Console.WriteLine("=== TRANSFER SUMMARY ===");
    Console.WriteLine($"  Uploaded:     {uploaded}");
    Console.WriteLine($"  Skipped:      {skipped}");
    Console.WriteLine($"  Failed:       {failed}");
  }

  public virtual void ReportNotificationSent(string[] recipients) =>
      Console.WriteLine($"  ✉ Email sent to: {string.Join(", ", recipients)}");

  public virtual void ReportDryRunTransferItem(string fileName, long sizeBytes, string status) =>
      Console.WriteLine($"  → {fileName} ({FormatSize(sizeBytes)}) [{status}]");

  public virtual void ReportDryRunNotification(bool wouldSend, string[] recipients)
  {
    if (wouldSend)
      Console.WriteLine($"  ✉ Would send email to: {string.Join(", ", recipients)}");
    else
      Console.WriteLine("  ✉ No email would be sent.");
  }

  protected static string FormatSize(long bytes) => bytes switch {
    < 1024 => $"{bytes} B",
    < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
    < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
    _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
  };
}
