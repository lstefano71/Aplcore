using AplcoreHandler.Models;
using AplcoreHandler.Output;

using DotNet.Globbing;

namespace AplcoreHandler.Services;

public static class FileDiscoveryService
{
  public static List<DiscoveredFile> Discover(
      SourceDirectory[] sourceDirectories,
      string filePattern,
      string? excludeDirectory,
      IOutputRenderer output)
  {
    var glob = Glob.Parse(filePattern);
    var results = new List<DiscoveredFile>();

    foreach (var sourceDir in sourceDirectories) {
      var dir = sourceDir.Path;

      if (!Directory.Exists(dir)) {
        output.ReportWarning($"Source directory not found: {dir}");
        continue;
      }

      try {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) {
          // Exclude target directory to avoid self-interference
          if (excludeDirectory is not null &&
              file.StartsWith(excludeDirectory, StringComparison.OrdinalIgnoreCase))
            continue;

          var fileName = Path.GetFileName(file);

          // Match glob pattern and exclude files with extensions
          if (glob.IsMatch(fileName) && !Path.HasExtension(fileName)) {
            results.Add(new DiscoveredFile(new FileInfo(file), sourceDir.Label));
          }
        }
      } catch (UnauthorizedAccessException) {
        output.ReportWarning($"Access denied to directory: {dir}");
      } catch (IOException ex) {
        output.ReportWarning($"I/O error scanning {dir}: {ex.Message}");
      }
    }

    return results;
  }
}
