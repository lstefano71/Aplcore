# AplcoreHandler — User Guide

## Configuration

AplcoreHandler is configured via a JSON file. By default it looks for `aplcore_config.json` in the current directory, or you can pass a path as the first argument.

### Config File Format

```json
{
  "sourceDirectories": [
    "\\\\server1\\aplcores",
    { "path": "\\\\server2\\dumps", "label": "Production" },
    "D:\\local-dumps"
  ],
  "targetDirectory": "D:\\archive\\aplcores",
  "filePattern": "aplcore*",
  "zipNameTemplate": "{timestamp}_{label}_{name}_d{major}.{minor}.{revision}"
}
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `sourceDirectories` | array | Yes | — | Directories to scan. Each entry is either a plain path string or an object `{"path": "…", "label": "…"}` |
| `targetDirectory` | `string` | Yes | — | Where to write zip archives and the tracking database |
| `filePattern` | `string` | No | `aplcore*` | Glob pattern for matching files |
| `zipNameTemplate` | `string` | No | `{timestamp}_{label}_{name}_d{major}.{minor}.{revision}` | Template for zip file names (see below) |

### Source Directory Labels

Each entry in `sourceDirectories` can be a plain string path or an object with an optional `label`:

```json
"sourceDirectories": [
  "D:\\dumps",
  { "path": "\\\\server\\aplcores", "label": "Production" },
  { "path": "D:\\dev-dumps", "label": "Dev" }
]
```

The label becomes part of the zip file name via the `{label}` template token.

### Zip Name Template

The `zipNameTemplate` field controls how archive names are formed. Available tokens:

| Token | Value |
|-------|-------|
| `{timestamp}` | File's last modified time in UTC: `yyyyMMdd_HHmmss` |
| `{name}` | Original aplcore filename |
| `{label}` | Source directory label (empty string if not set) |
| `{major}` | Interpreter major version (from trailer) |
| `{minor}` | Interpreter minor version (from trailer) |
| `{revision}` | Interpreter SVN revision (from trailer) |

`.zip` is always appended automatically. Consecutive `_` characters that result from empty tokens are collapsed to a single `_`.

**Examples:**

| Template | Label | Version | Result |
|----------|-------|---------|--------|
| `{timestamp}_{label}_{name}_d{major}.{minor}.{revision}` | `Production` | 20.0.53273 | `20260401_112337_Production_aplcore_10_d20.0.53273.zip` |
| `{timestamp}_{label}_{name}_d{major}.{minor}.{revision}` | *(empty)* | 20.0.53273 | `20260401_112337_aplcore_10_d20.0.53273.zip` |
| `{timestamp}_{name}` | *(any)* | *(any)* | `20260401_112337_aplcore_10.zip` |

### File Pattern Details

The glob pattern is matched against **file names only** (not full paths). Additionally, files with extensions are automatically excluded. This means:

| File | Matches `aplcore*`? | Archived? |
|------|---------------------|-----------|
| `aplcore_10` | ✓ | ✓ Yes |
| `aplcore_22` | ✓ | ✓ Yes |
| `aplcore` | ✓ | ✓ Yes |
| `aplcore_10.zip` | ✓ | ✗ No (has extension) |
| `aplcore_report.msg` | ✓ | ✗ No (has extension) |
| `crash_dump_1` | ✗ | ✗ No (doesn't match pattern) |

### Path Conventions

- Use UNC paths for network shares: `\\\\server\\share\\path`
- Forward and backward slashes are both accepted
- Paths are resolved to their full absolute form internally

## How It Works

### Discovery

1. Each directory in `sourceDirectories` is scanned recursively
2. Files matching the glob pattern (minus those with extensions) are collected
3. The `targetDirectory` is automatically excluded from scanning to prevent self-interference
4. Inaccessible directories are logged as warnings and skipped

### Change Detection

A JSON database (`aplcore_db.json`) in the target directory tracks every processed file by its **absolute path**, recording:
- File size in bytes
- Last modified timestamp (UTC)

A file is queued for archival when:
- Its path is not in the database (**new file**)
- Its size or timestamp differs from the database entry (**changed file**)

### Archival

For each file to archive:

1. **Trailer extraction** — the tool seeks to near the end of the binary file (1MB from EOF) and searches for the marker `========================== Interesting Information`. No need for `wsdump.exe`.
2. **Metadata splitting** — the trailer text is split into files:
   - `dyalog{major}.{minor}.{revision}-{edition}{bits}.txt` — general crash info (serial, version, config, exception, registers, C stack). Named `metadata.txt` when version cannot be extracted.
   - `address_space.txt` — all `!AddressSpace:` lines (virtual memory map with loaded DLLs)
   - `apl_stack.txt` — all `!APLStack:` lines (APL-level call stacks per green thread)
3. **Zip creation** — a zip archive is created in the target directory containing:
   - The aplcore file (streamed, never loaded fully into memory), renamed with a timestamp prefix
   - The three metadata files
4. **Database update** — the DB is saved after each successful archive (crash-safe)

### Zip Naming

Archives are named using the `zipNameTemplate` from the config (default: `{timestamp}_{label}_{name}_d{major}.{minor}.{revision}`). With no label and a known version this produces `yyyyMMdd_HHmmss_<filename>_d<major>.<minor>.<revision>.zip`.

The timestamp is the file's **last modified time in UTC**. Inside the zip, the aplcore is also renamed with the same computed base name.

Example: an `aplcore_10` from a directory labelled `Production`, interpreter v20.0.53273, last modified at 2026-04-01 11:23:37 UTC produces:
```
20260401_112337_Production_aplcore_10_d20.0.53273.zip
├── 20260401_112337_Production_aplcore_10_d20.0.53273    # renamed aplcore
├── dyalog20.0.53273-U64.txt                              # metadata (version + edition + bits)
├── address_space.txt
└── apl_stack.txt
```

## CLI Reference

```
AplcoreHandler [config-path] [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `config-path` | Path to the JSON config file. Default: `aplcore_config.json` in the current directory. |

### Options

| Option | Description |
|--------|-------------|
| `--dry-run` | Scan and report what would be archived, without creating any files |
| `--ci` | Force non-interactive output (no ANSI codes, no progress bars) |
| `--help`, `-h` | Show usage information |

### Dry Run

Use `--dry-run` to preview what the tool would do:

```
> AplcoreHandler.exe config.json --dry-run --ci

AplcoreHandler v0.1.42
Scanning 2 source directories...
Found 5 file(s), 2 to process.
  → aplcore_15 (423.7 MB) [new]
  → aplcore_10 (614.2 MB) [changed]

=== DRY RUN SUMMARY ===
  Archived: 0
  Skipped:  0
  Errors:   0
```

## TeamCity Integration

### Automatic Detection

When the `TEAMCITY_VERSION` environment variable is set (standard in all TeamCity agents), the tool automatically:
- Switches to plain text output
- Emits TeamCity service messages for progress and statistics

### Service Messages

| Message | When |
|---------|------|
| `##teamcity[progressMessage '...']` | During each file archive |
| `##teamcity[buildStatisticValue key='AplcoreHandler.Archived' value='N']` | In summary |
| `##teamcity[buildStatisticValue key='AplcoreHandler.Skipped' value='N']` | In summary |
| `##teamcity[buildStatisticValue key='AplcoreHandler.Errors' value='N']` | In summary |
| `##teamcity[message text='...' status='WARNING']` | On warnings |
| `##teamcity[message text='...' status='ERROR']` | On errors |

### TeamCity Job Setup

1. Add a **Command Line** build step
2. Set the executable to the path of `AplcoreHandler.exe`
3. Pass the config file path as the first argument
4. Use exit code checking: the build step will fail on exit code 2 (fatal errors)

## Troubleshooting

### "DB lock busy, retrying..."

Another instance of AplcoreHandler is running against the same target directory. The tool retries with exponential backoff up to 10 times. If you're sure no other instance is running, delete the `aplcore_db.lock` file in the target directory.

### "Corrupt database, backing up and starting fresh"

The `aplcore_db.json` file contains invalid JSON. The tool automatically backs it up (as `aplcore_db.json.corrupt.<timestamp>`) and creates a fresh database. This means previously archived files will be re-archived.

### "No trailer found in <file>"

The file doesn't contain the `========================== Interesting Information` marker. This can happen with:
- Very old Dyalog versions that use a different format
- Truncated/corrupted aplcore files
- Non-aplcore files that matched the glob pattern

The file is still archived, but without metadata files in the zip.

### "Skipping locked file after 3 retries"

The file is held open by another process (e.g., the Dyalog runtime is still writing it). The tool retries 3 times with increasing delays. If it remains locked, it's skipped and will be picked up on the next run.

### File appears to be re-archived every run

Check if:
- The file's timestamp is changing between runs (another process modifying it)
- Path normalization issues (different casing or relative/absolute variations in config)
