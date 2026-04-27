using AplcoreHandler.Models;
using AplcoreHandler.Output;
using AplcoreHandler.Services;

namespace AplcoreHandler.Pipeline;

public sealed class ArchivePipeline
{
  private readonly AppConfig _config;
  private readonly IOutputRenderer _output;
  private readonly bool _dryRun;

  public ArchivePipeline(AppConfig config, IOutputRenderer output, bool dryRun)
  {
    _config = config;
    _output = output;
    _dryRun = dryRun;
  }

  /// <summary>
  /// Runs the full archive pipeline. Returns exit code (0=success, 1=partial, 2=fatal).
  /// </summary>
  public int Run()
  {
    // Ensure target directory exists
    Directory.CreateDirectory(_config.TargetDirectory);

    var targetDirFull = Path.GetFullPath(_config.TargetDirectory);

    // Phase 1: Discover files
    _output.ReportScanStart(_config.SourceDirectories.Length);

    var discovered = FileDiscoveryService.Discover(
        _config.SourceDirectories,
        _config.FilePattern,
        excludeDirectory: targetDirFull,
        _output);

    // Phase 2: Load DB and detect changes
    using var dbService = new DatabaseService(_config.TargetDirectory, _output);
    var db = dbService.AcquireAndLoad();

    var toProcess = DatabaseService.DetectChanges(db, discovered);

    _output.ReportScanResult(discovered.Count, toProcess.Count);

    if (toProcess.Count == 0) {
      _output.ReportSummary(0, 0, 0, 0, 0, _dryRun);
      return 0;
    }

    // Phase 3: Dry run or archive
    if (_dryRun)
      return RunDryRun(db, toProcess);

    return RunArchive(db, dbService, toProcess);
  }

  private int RunDryRun(AplcoreDb db, List<DiscoveredFile> toProcess)
  {
    long totalSourceBytes = 0;
    foreach (var item in toProcess) {
      var key = DatabaseService.NormalizePath(item.File.FullName);
      var reason = db.Entries.ContainsKey(key) ? "changed" : "new";
      _output.ReportDryRunItem(item.File.FullName, item.File.Length, reason);
      totalSourceBytes += item.File.Length;
    }

    _output.ReportSummary(0, 0, 0, totalSourceBytes, 0, dryRun: true);
    return 0;
  }

  private int RunArchive(AplcoreDb db, DatabaseService dbService, List<DiscoveredFile> toProcess)
  {
    int archived = 0, skipped = 0, errors = 0;
    long totalSourceBytes = 0, totalZipBytes = 0;

    _output.ReportArchiveStart(toProcess.Count);

    for (int i = 0; i < toProcess.Count; i++) {
      var item = toProcess[i];
      var file = item.File;
      _output.ReportArchiveProgress(file.FullName, file.Length, file.LastWriteTimeUtc, i + 1, toProcess.Count);

      try {
        // Extract trailer
        var rawTrailer = TrailerExtractor.Extract(file.FullName);
        TrailerData? trailer = null;

        if (rawTrailer is not null) {
          trailer = MetadataSplitter.Split(rawTrailer);
        } else {
          _output.ReportWarning($"No trailer found in {file.Name}, archiving without metadata.");
        }

        // Create zip archive
        var zipPath = ArchiveService.Archive(
            file,
            _config.TargetDirectory,
            item.Label,
            trailer,
            _config.ZipNameTemplate,
            _output);

        if (zipPath is not null) {
          _output.ReportArchiveComplete(file.Name, rawTrailer is not null, zipPath);

          totalSourceBytes += file.Length;
          var zipInfo = new FileInfo(zipPath);
          totalZipBytes += zipInfo.Length;

          // Update DB after each successful archive (crash-safe)
          dbService.RecordProcessed(db, file);
          dbService.Save(db);

          archived++;
        } else {
          errors++;
        }
      } catch (Exception ex) {
        _output.ReportError($"Unexpected error processing {file.Name}: {ex.Message}");
        errors++;
      }
    }

    _output.ReportSummary(archived, skipped, errors, totalSourceBytes, totalZipBytes, dryRun: false);

    // Exit code: 0=all OK, 1=some skipped/errors
    return errors > 0 ? 1 : 0;
  }
}
