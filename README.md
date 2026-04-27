# AplcoreHandler

A CLI tool that automatically discovers, catalogs, and archives [Dyalog APL](https://www.dyalog.com/) crash dump files (`aplcore` files). It extracts embedded crash metadata directly from the binary and packages everything into timestamped zip archives for long-term storage.

[![Unlicense](https://img.shields.io/badge/license-Unlicense-blue.svg)](UNLICENSE)

## Features

- **Automatic discovery** — recursively scans configured directories for aplcore files using glob patterns
- **Change tracking** — JSON database tracks which files have been processed; only new or modified files are archived
- **Metadata extraction** — reads the binary trailer directly (no `wsdump.exe` dependency), splits it into separate consultable files
- **Timestamped archives** — creates `yyyyMMdd_HHmmss_<filename>.zip` with the aplcore, metadata, address space map, and APL stack
- **Rich CLI** — [Spectre.Console](https://spectreconsole.net/) progress bars and tables when interactive; plain text in CI
- **TeamCity integration** — auto-detects TeamCity, emits service messages with build statistics
- **AOT-compiled** — single self-contained `.exe`, no .NET runtime required
- **Crash-safe** — advisory file locking, incremental DB saves, partial zip cleanup

## Quick Start

1. Download `AplcoreHandler.exe` from the [latest release](../../releases/latest).

2. Create a config file (`aplcore_config.json`):

```json
{
  "sourceDirectories": [
    "\\\\server\\aplcores",
    "D:\\dumps"
  ],
  "targetDirectory": "D:\\archive",
  "filePattern": "aplcore*"
}
```

3. Run:

```
AplcoreHandler.exe aplcore_config.json
```

## CLI Usage

```
AplcoreHandler [config-path] [options]

Arguments:
  config-path    Path to JSON config file (default: aplcore_config.json)

Options:
  --dry-run      Scan and report without archiving
  --ci           Force non-interactive output mode
  --help, -h     Show this help
```

### Examples

```bash
# Interactive mode (progress bars, colors)
AplcoreHandler.exe

# Dry run — see what would be archived
AplcoreHandler.exe my_config.json --dry-run

# CI mode — plain output, no ANSI
AplcoreHandler.exe my_config.json --ci
```

## Archive Structure

Each aplcore is packaged into a zip:

```
20260401_112337_aplcore_10.zip
├── 20260401_112337_aplcore_10    # renamed aplcore file
├── metadata.txt                   # crash info, versions, config
├── address_space.txt              # virtual memory map (!AddressSpace lines)
└── apl_stack.txt                  # APL call stacks (!APLStack lines)
```

## Output Modes

| Condition | Mode | Features |
|-----------|------|----------|
| Terminal attached | Interactive | Spectre.Console progress bars, summary table, colors |
| `--ci` flag | Plain | Clean text, no ANSI |
| `TEAMCITY_VERSION` env var | TeamCity | Plain + `##teamcity[...]` service messages |
| Stdout redirected | Plain | No ANSI, no progress bars |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All files processed successfully |
| 1 | Some files skipped due to errors |
| 2 | Fatal error (bad config, DB lock failure) |

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Build
dotnet build src/AplcoreHandler/AplcoreHandler.csproj -c Release

# Publish AOT single-file
dotnet publish src/AplcoreHandler/AplcoreHandler.csproj -c Release -o publish

# Run
./publish/AplcoreHandler.exe --help
```

## Versioning

Uses [MinVer](https://github.com/adamralph/minver). Version is driven by git tags (`v{major}.{minor}.{patch}`). The tool always displays its version in the startup banner.

## Documentation

- [User Guide](docs/user-guide.md) — detailed configuration and usage
- [Architecture](docs/architecture.md) — technical design for maintainers
- [Product Requirements](docs/prd.md) — full requirements specification
- [Architecture Decision Records](docs/adr/) — key design decisions and rationale

## License

This project is released into the public domain under the [Unlicense](UNLICENSE).
