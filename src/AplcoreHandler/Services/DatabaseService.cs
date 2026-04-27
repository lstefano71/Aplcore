using AplcoreHandler.Models;
using AplcoreHandler.Output;

using System.Text.Json;

namespace AplcoreHandler.Services;

public sealed class DatabaseService : IDisposable
{
  private const string DbFileName = "aplcore_db.json";
  private const string LockFileName = "aplcore_db.lock";
  private const int MaxLockRetries = 10;
  private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(500);

  private readonly string _dbPath;
  private readonly string _lockPath;
  private readonly IOutputRenderer _output;
  private FileStream? _lockStream;

  public DatabaseService(string targetDirectory, IOutputRenderer output)
  {
    _dbPath = Path.Combine(targetDirectory, DbFileName);
    _lockPath = Path.Combine(targetDirectory, LockFileName);
    _output = output;
  }

  public AplcoreDb AcquireAndLoad()
  {
    AcquireLock();
    return LoadDb();
  }

  public void Save(AplcoreDb db)
  {
    var tmpPath = _dbPath + ".tmp";
    try {
      var json = JsonSerializer.Serialize(db, AppJsonContext.Default.AplcoreDb);
      File.WriteAllText(tmpPath, json);
      File.Move(tmpPath, _dbPath, overwrite: true);
    } catch {
      try { File.Delete(tmpPath); } catch { /* best effort */ }
      throw;
    }
  }

  public static string NormalizePath(string path) =>
      Path.GetFullPath(path);

  public static List<FileInfo> DetectChanges(AplcoreDb db, List<FileInfo> discovered)
  {
    var toProcess = new List<FileInfo>();

    foreach (var file in discovered) {
      var key = NormalizePath(file.FullName);

      if (db.Entries.TryGetValue(key, out var entry)) {
        if (entry.Size != file.Length ||
            entry.LastModifiedUtc != file.LastWriteTimeUtc) {
          toProcess.Add(file);
        }
      } else {
        toProcess.Add(file);
      }
    }

    return toProcess;
  }

  public void RecordProcessed(AplcoreDb db, FileInfo file)
  {
    var key = NormalizePath(file.FullName);
    db.Entries[key] = new DbEntry {
      Size = file.Length,
      LastModifiedUtc = file.LastWriteTimeUtc
    };
  }

  private void AcquireLock()
  {
    var delay = InitialRetryDelay;

    for (int attempt = 0; attempt <= MaxLockRetries; attempt++) {
      try {
        _lockStream = new FileStream(
            _lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        return;
      } catch (IOException) when (attempt < MaxLockRetries) {
        _output.ReportWarning(
            $"DB lock busy, retrying in {delay.TotalSeconds:F1}s (attempt {attempt + 1}/{MaxLockRetries})...");
        Thread.Sleep(delay);
        delay *= 2;
      }
    }

    throw new InvalidOperationException(
        $"Could not acquire database lock after {MaxLockRetries} attempts: {_lockPath}");
  }

  private AplcoreDb LoadDb()
  {
    if (!File.Exists(_dbPath))
      return new AplcoreDb();

    try {
      var json = File.ReadAllText(_dbPath);
      return JsonSerializer.Deserialize(json, AppJsonContext.Default.AplcoreDb)
             ?? new AplcoreDb();
    } catch (JsonException ex) {
      _output.ReportWarning($"Corrupt database, backing up and starting fresh: {ex.Message}");
      var backupPath = _dbPath + $".corrupt.{DateTime.UtcNow:yyyyMMdd_HHmmss}";
      try { File.Move(_dbPath, backupPath); } catch { /* best effort */ }
      return new AplcoreDb();
    }
  }

  public void Dispose()
  {
    _lockStream?.Dispose();
    _lockStream = null;

    // Clean up lock file
    try { File.Delete(_lockPath); } catch { /* best effort */ }
  }
}
