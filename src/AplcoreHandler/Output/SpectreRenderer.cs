using AplcoreHandler.Models;
using Spectre.Console;

namespace AplcoreHandler.Output;

public sealed class SpectreRenderer : IOutputRenderer
{
    public void ReportBanner(string version)
    {
        AnsiConsole.Write(new FigletText("AplcoreHandler")
            .Color(Color.CadetBlue));
        AnsiConsole.MarkupLineInterpolated($"[grey]v{version}[/]");
        AnsiConsole.WriteLine();
    }

    public void ReportConfig(AppConfig config, string configPath, bool dryRun)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[blue]Configuration[/]")
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Config file", Markup.Escape(Path.GetFullPath(configPath)));
        table.AddRow("Pattern", Markup.Escape(config.FilePattern));
        table.AddRow("Target", Markup.Escape(Path.GetFullPath(config.TargetDirectory)));
        foreach (var dir in config.SourceDirectories)
            table.AddRow("Source", Markup.Escape(dir));
        if (dryRun)
            table.AddRow("Mode", "[yellow]DRY RUN[/]");

        AnsiConsole.Write(table);
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

    public void ReportArchiveProgress(string filePath, long sizeBytes, DateTime lastModifiedUtc, int current, int total)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"  [grey][[{current}/{total}]][/] [white]{Markup.Escape(Path.GetFileName(filePath))}[/]");
        AnsiConsole.MarkupLineInterpolated(
            $"           [grey]Path:[/]     {Markup.Escape(filePath)}");
        AnsiConsole.MarkupLineInterpolated(
            $"           [grey]Size:[/]     {FormatSize(sizeBytes)}");
        AnsiConsole.MarkupLineInterpolated(
            $"           [grey]Modified:[/] {lastModifiedUtc:yyyy-MM-dd HH:mm:ss} UTC");
    }

    public void ReportArchiveComplete(string fileName, bool hadTrailer, string zipPath)
    {
        if (hadTrailer)
            AnsiConsole.MarkupLineInterpolated($"  [green]✓[/] {Markup.Escape(fileName)} [grey](with metadata)[/]");
        else
            AnsiConsole.MarkupLineInterpolated($"  [yellow]✓[/] {Markup.Escape(fileName)} [grey](no trailer)[/]");

        AnsiConsole.MarkupLineInterpolated($"    [grey]→[/] {Markup.Escape(zipPath)}");
    }

    public void ReportSummary(int archived, int skipped, int errors, long totalSourceBytes, long totalZipBytes, bool dryRun)
    {
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title(dryRun ? "[yellow]DRY RUN SUMMARY[/]" : "[green]SUMMARY[/]")
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Archived", archived.ToString());
        table.AddRow("Skipped", skipped.ToString());
        table.AddRow("Errors", errors > 0 ? $"[red]{errors}[/]" : errors.ToString());
        table.AddRow("Source total", FormatSize(totalSourceBytes));
        table.AddRow("Zip total", FormatSize(totalZipBytes));
        if (totalSourceBytes > 0)
            table.AddRow("Compression", $"{(1.0 - (double)totalZipBytes / totalSourceBytes) * 100:F1}%");

        AnsiConsole.Write(table);
    }

    public void ReportWarning(string message) =>
        AnsiConsole.MarkupLineInterpolated($"  [yellow]⚠[/] {Markup.Escape(message)}");

    public void ReportError(string message) =>
        AnsiConsole.MarkupLineInterpolated($"  [red]✗[/] {Markup.Escape(message)}");

    public void ReportDryRunItem(string filePath, long sizeBytes, string reason) =>
        AnsiConsole.MarkupLineInterpolated(
            $"  [grey]→[/] {Markup.Escape(Path.GetFileName(filePath))} [grey]({FormatSize(sizeBytes)})[/] [blue][[{Markup.Escape(reason)}]][/]");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
