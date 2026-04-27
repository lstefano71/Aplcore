using System.Text.Json;
using System.Text.Json.Serialization;

namespace AplcoreHandler.Models;

public record SourceDirectory(string Path, string Label = "");

/// <summary>
/// Deserializes a source directory entry from either a plain string or a
/// <c>{"path": "…", "label": "…"}</c> object, so both forms are valid in config.
/// </summary>
public sealed class SourceDirectoryConverter : JsonConverter<SourceDirectory>
{
  public override SourceDirectory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.String)
      return new SourceDirectory(reader.GetString()!);

    if (reader.TokenType != JsonTokenType.StartObject)
      throw new JsonException("Expected string or object for sourceDirectory entry.");

    string? path = null;
    string label = "";

    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
      if (reader.TokenType != JsonTokenType.PropertyName)
        throw new JsonException("Expected property name.");

      var propName = reader.GetString()!;
      reader.Read();

      if (propName.Equals("path", StringComparison.OrdinalIgnoreCase))
        path = reader.GetString();
      else if (propName.Equals("label", StringComparison.OrdinalIgnoreCase))
        label = reader.GetString() ?? "";
      else
        reader.Skip();
    }

    if (path is null)
      throw new JsonException("Source directory object must have a 'path' field.");

    return new SourceDirectory(path, label);
  }

  public override void Write(Utf8JsonWriter writer, SourceDirectory value, JsonSerializerOptions options)
  {
    if (string.IsNullOrEmpty(value.Label)) {
      writer.WriteStringValue(value.Path);
    } else {
      writer.WriteStartObject();
      writer.WriteString("path", value.Path);
      writer.WriteString("label", value.Label);
      writer.WriteEndObject();
    }
  }
}

public record AppConfig(
    [property: JsonConverter(typeof(SourceDirectoryConverter))]
    SourceDirectory[] SourceDirectories,
    string TargetDirectory,
    string FilePattern = "aplcore*",
    string ZipNameTemplate = "{timestamp}_{label}_{name}_d{major}.{minor}.{revision}"
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

/// <summary>
/// Interpreter version parsed from the metadata trailer lines.
/// </summary>
public sealed class InterpreterVersion
{
  public required int Major { get; init; }
  public required int Minor { get; init; }
  public required int Revision { get; init; }
  public required string Edition { get; init; }
  public required int Bits { get; init; }

  /// <summary>First letter of edition, uppercased (e.g. "U" for Unicode).</summary>
  public string EditionInitial => Edition.Length > 0 ? Edition[..1].ToUpperInvariant() : "?";
}

public sealed class TrailerData
{
  public required string Metadata { get; init; }
  public required string AddressSpace { get; init; }
  public required string AplStack { get; init; }
  public InterpreterVersion? Version { get; init; }
}

/// <summary>A discovered aplcore file paired with the label of its source directory.</summary>
public sealed record DiscoveredFile(FileInfo File, string Label);

[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(AplcoreDb))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppJsonContext : JsonSerializerContext;
