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
  void ReportTransferStart(int fileCount);
  void ReportTransferProgress(string fileName, long sizeBytes, int current, int total);
  void ReportTransferResult(string fileName, string outcome);
  void ReportTransferSummary(int uploaded, int skipped, int failed);
  void ReportNotificationSent(string[] recipients);
  void ReportDryRunTransferItem(string fileName, long sizeBytes, string status);
  void ReportDryRunNotification(bool wouldSend, string[] recipients);
}
