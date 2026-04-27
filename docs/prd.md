# AplcoreHandler — Product Requirements Document

## 1. Overview

**AplcoreHandler** is a command-line tool that automatically discovers, catalogs, and archives Dyalog APL crash dump files (`aplcore` files). It extracts embedded crash metadata directly from the binary files and packages everything into timestamped zip archives for long-term storage and quick consultation.

## 2. Problem Statement

Dyalog APL applications produce crash dumps called `aplcore` files when they encounter unrecoverable errors. These files:

- Are large (typically 300MB–1GB+)
- Accumulate across multiple servers and network shares
- Have no extension, making them hard to manage with standard tools
- Contain valuable crash forensics buried in a binary-encoded text trailer
- Are not systematically tracked, leading to missed dumps and duplicated investigation effort

There is no existing automated pipeline to detect new dumps, extract the embedded metadata, and archive them in a structured, consultable format.

## 3. Target Users

| User | Context |
|------|---------|
| **APL developers / support engineers** | Run the tool manually to archive dumps from dev/test machines |
| **CI/CD pipelines (TeamCity)** | Scheduled or triggered execution to sweep production servers |

## 4. Functional Requirements

### 4.1 Configuration

| ID | Requirement |
|----|-------------|
| FR-01 | The tool reads configuration from a JSON file |
| FR-02 | Config specifies one or more source directories to scan |
| FR-03 | Config specifies a target directory for zip archives |
| FR-04 | Config specifies a glob pattern for matching aplcore files (default: `aplcore*`) |
| FR-05 | Config file path is provided via CLI argument, with fallback to `aplcore_config.json` in the current directory |

### 4.2 File Discovery

| ID | Requirement |
|----|-------------|
| FR-10 | The tool recursively scans all configured source directories |
| FR-11 | Files are matched using the configured glob pattern via DotNet.Glob |
| FR-12 | Files with extensions (containing `.` after the glob-matched prefix) are excluded |
| FR-13 | The tool handles inaccessible directories gracefully (log warning, continue) |

### 4.3 Change Tracking Database

| ID | Requirement |
|----|-------------|
| FR-20 | A JSON database file tracks all processed files |
| FR-21 | The database is stored in the target directory as `aplcore_db.json` |
| FR-22 | Each entry is keyed by absolute file path and stores: file size (bytes), last modified UTC |
| FR-23 | A file is considered "new" if its path is not in the database |
| FR-24 | A file is considered "changed" if its size or lastModifiedUtc differs from the database entry |
| FR-25 | New and changed files are queued for archival |
| FR-26 | The database is updated only after successful archival of each file |

### 4.4 Metadata Extraction

| ID | Requirement |
|----|-------------|
| FR-30 | The tool reads the binary aplcore file from the end, searching for the marker `========================== Interesting Information` |
| FR-31 | The search uses a 1MB buffer with overlap to handle marker straddling buffer boundaries |
| FR-32 | If the marker is not found, the tool logs a warning and archives the file without metadata |
| FR-33 | The text section from the marker to EOF is extracted and split into three files: |
| | — `metadata.txt`: all lines except `!AddressSpace:` and `!APLStack:` lines |
| | — `address_space.txt`: all `!AddressSpace:` lines (raw, verbatim) |
| | — `apl_stack.txt`: all `!APLStack:` lines (raw, verbatim) |
| FR-34 | All extracted text preserves the original `!`-delimited encoding verbatim |

### 4.5 Zip Archival

| ID | Requirement |
|----|-------------|
| FR-40 | Each aplcore is archived into a zip file in the target directory |
| FR-41 | Zip filename format: `yyyyMMdd_HHmmss_<original_filename>.zip` using the file's last modified UTC |
| FR-42 | Inside the zip, the aplcore file is renamed with the same timestamp prefix: `yyyyMMdd_HHmmss_<original_filename>` |
| FR-43 | Metadata files (metadata.txt, address_space.txt, apl_stack.txt) are included at the zip root |
| FR-44 | Compression level: Optimal |
| FR-45 | If zip creation fails, any partial zip file is cleaned up (deleted) |
| FR-46 | Target directory layout is flat (no date-based subdirectories) |

### 4.6 Command-Line Interface

| ID | Requirement |
|----|-------------|
| FR-50 | The tool accepts a config file path as a positional argument |
| FR-51 | `--dry-run` flag: scan and report without archiving |
| FR-52 | `--ci` flag: force non-interactive output mode |
| FR-53 | The tool always displays a banner: `AplcoreHandler v<AssemblyInformationalVersion>` |

### 4.7 User Interface

| ID | Requirement |
|----|-------------|
| FR-60 | **Interactive mode** (default when terminal is attached): Spectre.Console with progress bars, summary table, colorized output |
| FR-61 | **CI mode** (--ci flag, TEAMCITY_VERSION env var, or redirected stdout): plain text output |
| FR-62 | **TeamCity mode** (TEAMCITY_VERSION detected): plain text + TeamCity service messages (`##teamcity[...]`) |
| FR-63 | Detection hierarchy: `--ci` flag → `TEAMCITY_VERSION` env var → `Console.IsOutputRedirected` → interactive |

## 5. Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-01 | AOT-compiled, self-contained single-file executable for `win-x64` |
| NFR-02 | .NET 10, C# 14+ |
| NFR-03 | No dependency on `wsdump.exe` or any external tool |
| NFR-04 | Must handle aplcore files in the multi-GB range without loading them into memory |
| NFR-05 | JSON database must remain performant and reliable for decades of accumulated entries |
| NFR-06 | Advisory file locking on the database for safe concurrent execution |
| NFR-07 | Locked files retried 3 times before skipping |
| NFR-08 | Corrupt database: backed up and recreated automatically |
| NFR-09 | Exit codes: 0 = success, 1 = partial success, 2 = fatal error |

## 6. Versioning

| Aspect | Detail |
|--------|--------|
| Tool | Nerdbank.GitVersioning |
| Base version | 0.1 |
| Pre-release | Branch name + height (default NBGV scheme) |
| Display | `AssemblyInformationalVersion` in startup banner |

## 7. CI/CD (GitHub Actions)

| Pipeline | Trigger | Output |
|----------|---------|--------|
| **PR validation** | Pull requests to any branch | Build + test (no release) |
| **Stable release** | `v*` tags on `main` | GitHub Release with `AplcoreHandler.exe` |
| **Preview release** | Push to any non-main branch | Rolling draft GitHub Release (updated per push) |

## 8. Out of Scope

- Full workspace dump analysis (that's what the Python tools in `/tools` do)
- SHA256 hashing of aplcore files (future consideration)
- Parsing or reformatting the `!`-encoded metadata (kept verbatim)
- Multi-platform builds (win-x64 only for now)
- Log file output (console only; CI systems capture stdout)
