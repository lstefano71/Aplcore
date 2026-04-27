using AplcoreHandler.Models;
using AplcoreHandler.Output;

using System.IO.Compression;

namespace AplcoreHandler.Services;

public static class ArchiveService
{
  private const int MaxRetries = 3;
  private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);

  public static bool Archive(
      FileInfo sourceFile,
      string targetDirectory,
      TrailerData? trailer,
      IOutputRenderer output)
  {
    var timestamp = sourceFile.LastWriteTimeUtc.ToString("yyyyMMdd_HHmmss");
    var baseName = $"{timestamp}_{sourceFile.Name}";
    var zipPath = Path.Combine(targetDirectory, baseName + ".zip");
    var tmpPath = zipPath + ".tmp";

    try {
      using (var zipStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
      using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create)) {
        // Add the aplcore file (streamed, not loaded into memory)
        AddFileEntry(archive, baseName, sourceFile, output);

        // Add metadata files if trailer was extracted
        if (trailer is not null) {
          AddTextEntry(archive, "metadata.txt", trailer.Metadata);
          AddTextEntry(archive, "address_space.txt", trailer.AddressSpace);
          AddTextEntry(archive, "apl_stack.txt", trailer.AplStack);
        }
      }

      // Atomic move from temp to final
      File.Move(tmpPath, zipPath, overwrite: true);
      return true;
    } catch (Exception ex) {
      output.ReportError($"Failed to archive {sourceFile.Name}: {ex.Message}");
      // Clean up partial files
      try { File.Delete(tmpPath); } catch { /* best effort */ }
      try { File.Delete(zipPath); } catch { /* best effort */ }
      return false;
    }
  }

  public static FileStream? OpenWithRetry(FileInfo file, IOutputRenderer output)
  {
    var delay = InitialRetryDelay;

    for (int attempt = 0; attempt <= MaxRetries; attempt++) {
      try {
        return new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
      } catch (IOException) when (attempt < MaxRetries) {
        output.ReportWarning(
            $"File locked: {file.Name}, retrying in {delay.TotalSeconds:F0}s ({attempt + 1}/{MaxRetries})...");
        Thread.Sleep(delay);
        delay *= 2;
      }
    }

    output.ReportWarning($"Skipping locked file after {MaxRetries} retries: {file.Name}");
    return null;
  }

  private static void AddFileEntry(
      ZipArchive archive, string entryName, FileInfo sourceFile, IOutputRenderer output)
  {
    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
    using var entryStream = entry.Open();

    // Verify file is accessible before streaming
    using var sourceStream = OpenWithRetry(sourceFile, output)
        ?? throw new IOException($"Cannot open {sourceFile.Name} after retries");

    sourceStream.CopyTo(entryStream);
  }

  private static void AddTextEntry(ZipArchive archive, string entryName, string content)
  {
    if (string.IsNullOrWhiteSpace(content))
      return;

    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
    using var writer = new StreamWriter(entry.Open());
    writer.Write(content);
  }
}
