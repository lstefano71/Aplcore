namespace AplcoreHandler.Output;

public class PlainRenderer : IOutputRenderer
{
    public virtual void ReportBanner(string version) =>
        Console.WriteLine($"AplcoreHandler v{version}");

    public virtual void ReportScanStart(int directoryCount) =>
        Console.WriteLine($"Scanning {directoryCount} source director{(directoryCount == 1 ? "y" : "ies")}...");

    public virtual void ReportScanResult(int totalFound, int toProcess) =>
        Console.WriteLine($"Found {totalFound} file(s), {toProcess} to process.");

    public virtual void ReportArchiveStart(int fileCount) =>
        Console.WriteLine($"Archiving {fileCount} file(s)...");

    public virtual void ReportArchiveProgress(string fileName, int current, int total) =>
        Console.WriteLine($"[{current}/{total}] Archiving {fileName}...");

    public virtual void ReportArchiveComplete(string fileName, bool hadTrailer) =>
        Console.WriteLine(hadTrailer
            ? $"  ✓ {fileName} (with metadata)"
            : $"  ✓ {fileName} (no trailer found)");

    public virtual void ReportSummary(int archived, int skipped, int errors, bool dryRun)
    {
        Console.WriteLine();
        if (dryRun)
            Console.WriteLine("=== DRY RUN SUMMARY ===");
        else
            Console.WriteLine("=== SUMMARY ===");

        Console.WriteLine($"  Archived: {archived}");
        Console.WriteLine($"  Skipped:  {skipped}");
        Console.WriteLine($"  Errors:   {errors}");
    }

    public virtual void ReportWarning(string message) =>
        Console.WriteLine($"  ⚠ {message}");

    public virtual void ReportError(string message) =>
        Console.Error.WriteLine($"  ✗ {message}");

    public virtual void ReportDryRunItem(string filePath, long sizeBytes, string reason) =>
        Console.WriteLine($"  → {Path.GetFileName(filePath)} ({FormatSize(sizeBytes)}) [{reason}]");

    protected static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
