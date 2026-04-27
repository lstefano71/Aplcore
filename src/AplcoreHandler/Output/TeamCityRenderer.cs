namespace AplcoreHandler.Output;

public sealed class TeamCityRenderer : PlainRenderer
{
  public override void ReportArchiveProgress(string filePath, long sizeBytes, DateTime lastModifiedUtc, int current, int total)
  {
    base.ReportArchiveProgress(filePath, sizeBytes, lastModifiedUtc, current, total);
    EmitServiceMessage("progressMessage", $"Archiving {Path.GetFileName(filePath)} ({current}/{total})");
  }

  public override void ReportSummary(int archived, int skipped, int errors, long totalSourceBytes, long totalZipBytes, bool dryRun)
  {
    base.ReportSummary(archived, skipped, errors, totalSourceBytes, totalZipBytes, dryRun);

    EmitStatistic("AplcoreHandler.Archived", archived);
    EmitStatistic("AplcoreHandler.Skipped", skipped);
    EmitStatistic("AplcoreHandler.Errors", errors);
    EmitStatistic("AplcoreHandler.SourceMB", (int)(totalSourceBytes / (1024 * 1024)));
    EmitStatistic("AplcoreHandler.ZipMB", (int)(totalZipBytes / (1024 * 1024)));
  }

  public override void ReportWarning(string message)
  {
    base.ReportWarning(message);
    EmitServiceMessage("message", Escape(message), "WARNING");
  }

  public override void ReportError(string message)
  {
    base.ReportError(message);
    EmitServiceMessage("message", Escape(message), "ERROR");
  }

  private static void EmitServiceMessage(string type, string value, string? status = null)
  {
    if (status is not null)
      Console.WriteLine($"##teamcity[{type} text='{Escape(value)}' status='{status}']");
    else
      Console.WriteLine($"##teamcity[{type} '{Escape(value)}']");
  }

  private static void EmitStatistic(string key, int value) =>
      Console.WriteLine($"##teamcity[buildStatisticValue key='{Escape(key)}' value='{value}']");

  private static string Escape(string value) =>
      value.Replace("|", "||")
           .Replace("'", "|'")
           .Replace("\n", "|n")
           .Replace("\r", "|r")
           .Replace("[", "|[")
           .Replace("]", "|]");
}
