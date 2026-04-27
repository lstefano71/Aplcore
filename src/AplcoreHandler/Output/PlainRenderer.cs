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

  protected static string FormatSize(long bytes) => bytes switch {
    < 1024 => $"{bytes} B",
    < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
    < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
    _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
  };
}
