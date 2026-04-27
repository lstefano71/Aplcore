using AplcoreHandler.Models;
using AplcoreHandler.Output;
using AplcoreHandler.Pipeline;

using System.Reflection;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "unknown";

// Parse CLI arguments
var configPath = "aplcore_config.json";
var dryRun = false;
var ciMode = false;

for (int i = 0; i < args.Length; i++) {
  switch (args[i]) {
    case "--dry-run":
      dryRun = true;
      break;
    case "--ci":
      ciMode = true;
      break;
    case "--help" or "-h":
      PrintUsage(version);
      return 0;
    default:
      if (!args[i].StartsWith('-'))
        configPath = args[i];
      else {
        Console.Error.WriteLine($"Unknown option: {args[i]}");
        PrintUsage(version);
        return 2;
      }
      break;
  }
}

// Select output renderer
IOutputRenderer output = SelectRenderer(ciMode);

output.ReportBanner(version);

// Load configuration
AppConfig config;
try {
  if (!File.Exists(configPath)) {
    output.ReportError($"Config file not found: {configPath}");
    return 2;
  }

  var json = File.ReadAllText(configPath);
  config = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig)
           ?? throw new JsonException("Config deserialized to null");
} catch (JsonException ex) {
  output.ReportError($"Invalid config file: {ex.Message}");
  return 2;
}

// Validate config
if (config.SourceDirectories is not { Length: > 0 }) {
  output.ReportError("Config must specify at least one source directory.");
  return 2;
}

if (string.IsNullOrWhiteSpace(config.TargetDirectory)) {
  output.ReportError("Config must specify a target directory.");
  return 2;
}

// Run pipeline
output.ReportConfig(config, configPath, dryRun);

try {
  var pipeline = new ArchivePipeline(config, output, dryRun);
  return pipeline.Run();
} catch (InvalidOperationException ex) when (ex.Message.Contains("lock")) {
  output.ReportError(ex.Message);
  return 2;
} catch (Exception ex) {
  output.ReportError($"Fatal error: {ex.Message}");
  return 2;
}

static IOutputRenderer SelectRenderer(bool ciFlag)
{
  if (ciFlag || Environment.GetEnvironmentVariable("TEAMCITY_VERSION") is not null) {
    return Environment.GetEnvironmentVariable("TEAMCITY_VERSION") is not null
        ? new TeamCityRenderer()
        : new PlainRenderer();
  }

  if (Console.IsOutputRedirected)
    return new PlainRenderer();

  return new SpectreRenderer();
}

static void PrintUsage(string version)
{
  Console.WriteLine($"AplcoreHandler v{version}");
  Console.WriteLine();
  Console.WriteLine("Usage: AplcoreHandler [config-path] [options]");
  Console.WriteLine();
  Console.WriteLine("Arguments:");
  Console.WriteLine("  config-path    Path to JSON config file (default: aplcore_config.json)");
  Console.WriteLine();
  Console.WriteLine("Options:");
  Console.WriteLine("  --dry-run      Scan and report without archiving");
  Console.WriteLine("  --ci           Force non-interactive output mode");
  Console.WriteLine("  --help, -h     Show this help");
}
