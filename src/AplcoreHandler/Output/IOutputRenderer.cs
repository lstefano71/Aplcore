using AplcoreHandler.Models;

namespace AplcoreHandler.Output;

public interface IOutputRenderer
{
    void ReportBanner(string version);
    void ReportConfig(AppConfig config, string configPath, bool dryRun);
    void ReportScanStart(int directoryCount);
    void ReportScanResult(int totalFound, int toProcess);
    void ReportArchiveStart(int fileCount);
    void ReportArchiveProgress(string filePath, long sizeBytes, DateTime lastModifiedUtc, int current, int total);
    void ReportArchiveComplete(string fileName, bool hadTrailer, string zipPath);
    void ReportSummary(int archived, int skipped, int errors, long totalSourceBytes, long totalZipBytes, bool dryRun);
    void ReportWarning(string message);
    void ReportError(string message);
    void ReportDryRunItem(string filePath, long sizeBytes, string reason);
}
