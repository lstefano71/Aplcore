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

  public static List<DiscoveredFile> DetectChanges(AplcoreDb db, List<DiscoveredFile> discovered)
  {
    var toProcess = new List<DiscoveredFile>();

    foreach (var item in discovered) {
      var key = NormalizePath(item.File.FullName);

      if (db.Entries.TryGetValue(key, out var entry)) {
        if (entry.Size != item.File.Length ||
            entry.LastModifiedUtc != item.File.LastWriteTimeUtc) {
          toProcess.Add(item);
        }
      } else {
        toProcess.Add(item);
      }
    }

    return toProcess;
  }

  /// <summary>Builds the shipment identity key from archive path, size, and last-modified time.</summary>
  public static string BuildShipmentKey(string zipPath, long size, DateTime lastModifiedUtc) =>
      $"{NormalizePath(zipPath)}|{size}|{lastModifiedUtc:O}";

  /// <summary>Returns true if this specific archive (by path+size+timestamp) has already been shipped.</summary>
  public static bool IsShipped(AplcoreDb db, string zipPath, long size, DateTime lastModifiedUtc) =>
      db.Shipments.ContainsKey(BuildShipmentKey(zipPath, size, lastModifiedUtc));

  /// <summary>Records a successful shipment. Call Save() after to persist.</summary>
  public static void RecordShipped(AplcoreDb db, string zipPath, long size, DateTime lastModifiedUtc)
  {
    var key = BuildShipmentKey(zipPath, size, lastModifiedUtc);
    db.Shipments[key] = new ShipmentEntry {
      Size = size,
      LastModifiedUtc = lastModifiedUtc,
      ShippedAtUtc = DateTime.UtcNow
    };
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
