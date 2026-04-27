using Spectre.Console;

namespace AplcoreHandler.Output;

public sealed class SpectreRenderer : IOutputRenderer
{
  public void ReportBanner(string version)
  {
    AnsiConsole.Write(new FigletText("AplcoreHandler")
        .Color(Color.CadetBlue));
    AnsiConsole.Write(
   new Rule($"[dim]Pack aplcores[/]  [white]v{ThisAssembly.AssemblyInformationalVersion}[/]")
       .LeftJustified()
       .RuleStyle(Style.Parse("cyan1 dim")));
    AnsiConsole.WriteLine();
  }

  public void ReportScanStart(int directoryCount) =>
      AnsiConsole.MarkupLineInterpolated(
          $"[blue]Scanning[/] {directoryCount} source director{(directoryCount == 1 ? "y" : "ies")}...");

  public void ReportScanResult(int totalFound, int toProcess) =>
      AnsiConsole.MarkupLineInterpolated(
          $"Found [green]{totalFound}[/] file(s), [yellow]{toProcess}[/] to process.");

  public void ReportArchiveStart(int fileCount) =>
      AnsiConsole.MarkupLineInterpolated(
          $"[blue]Archiving[/] {fileCount} file(s)...");

  public void ReportArchiveProgress(string fileName, int current, int total) =>
      AnsiConsole.MarkupLineInterpolated(
          $"  [grey][[{current}/{total}]][/] Archiving [white]{Markup.Escape(fileName)}[/]...");

  public void ReportArchiveComplete(string fileName, bool hadTrailer)
  {
    if (hadTrailer)
      AnsiConsole.MarkupLineInterpolated($"  [green]✓[/] {Markup.Escape(fileName)} [grey](with metadata)[/]");
    else
      AnsiConsole.MarkupLineInterpolated($"  [yellow]✓[/] {Markup.Escape(fileName)} [grey](no trailer)[/]");
  }

  public void ReportSummary(int archived, int skipped, int errors, bool dryRun)
  {
    AnsiConsole.WriteLine();

    var table = new Table()
        .Border(TableBorder.Rounded)
        .Title(dryRun ? "[yellow]DRY RUN SUMMARY[/]" : "[green]SUMMARY[/]")
        .AddColumn("Metric")
        .AddColumn("Count");

    table.AddRow("Archived", archived.ToString());
    table.AddRow("Skipped", skipped.ToString());

    var errorStyle = errors > 0 ? "[red]" + errors + "[/]" : errors.ToString();
    table.AddRow("Errors", errorStyle);

    AnsiConsole.Write(table);
  }

  public void ReportWarning(string message) =>
      AnsiConsole.MarkupLineInterpolated($"  [yellow]⚠[/] {Markup.Escape(message)}");

  public void ReportError(string message) =>
      AnsiConsole.MarkupLineInterpolated($"  [red]✗[/] {Markup.Escape(message)}");

  public void ReportDryRunItem(string filePath, long sizeBytes, string reason) =>
      AnsiConsole.MarkupLineInterpolated(
          $"  [grey]→[/] {Markup.Escape(Path.GetFileName(filePath))} [grey]({FormatSize(sizeBytes)})[/] [blue][[{Markup.Escape(reason)}]][/]");

  private static string FormatSize(long bytes) => bytes switch {
    < 1024 => $"{bytes} B",
    < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
    < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
    _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
  };
}
