namespace AplcoreHandler.Output;

public interface IOutputRenderer
{
    void ReportBanner(string version);
    void ReportScanStart(int directoryCount);
    void ReportScanResult(int totalFound, int toProcess);
    void ReportArchiveStart(int fileCount);
    void ReportArchiveProgress(string fileName, int current, int total);
    void ReportArchiveComplete(string fileName, bool hadTrailer);
    void ReportSummary(int archived, int skipped, int errors, bool dryRun);
    void ReportWarning(string message);
    void ReportError(string message);
    void ReportDryRunItem(string filePath, long sizeBytes, string reason);
}
