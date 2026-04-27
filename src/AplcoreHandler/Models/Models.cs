using System.Text.Json.Serialization;

namespace AplcoreHandler.Models;

public record AppConfig(
    string[] SourceDirectories,
    string TargetDirectory,
    string FilePattern = "aplcore*"
);

public sealed class DbEntry
{
  public required long Size { get; set; }
  public required DateTime LastModifiedUtc { get; set; }
}

public sealed class AplcoreDb
{
  public Dictionary<string, DbEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TrailerData
{
  public required string Metadata { get; init; }
  public required string AddressSpace { get; init; }
  public required string AplStack { get; init; }
}

[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(AplcoreDb))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppJsonContext : JsonSerializerContext;
