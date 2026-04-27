# AplcoreHandler — Architecture Guide

A technical reference for future maintainers.

## Project Structure

```
src/AplcoreHandler/
├── Program.cs                      # Entry point, CLI parsing, renderer selection
├── Models/
│   └── Models.cs                   # AppConfig, DbEntry, AplcoreDb, TrailerData, JsonContext
├── Services/
│   ├── FileDiscoveryService.cs     # Glob-based recursive file scanning
│   ├── DatabaseService.cs          # JSON DB load/save, locking, change detection
│   ├── TrailerExtractor.cs         # Binary seek-from-end trailer reading
│   ├── MetadataSplitter.cs         # Split trailer into metadata/address_space/apl_stack
│   └── ArchiveService.cs           # Zip creation with streaming
├── Output/
│   ├── IOutputRenderer.cs          # Output abstraction interface
│   ├── PlainRenderer.cs            # Console.WriteLine plain text
│   ├── TeamCityRenderer.cs         # Plain + ##teamcity[] service messages
│   └── SpectreRenderer.cs          # Rich Spectre.Console UI
└── Pipeline/
    └── ArchivePipeline.cs          # Orchestrator: discover → detect → extract → archive
```

## Key Design Decisions

All major design decisions are documented as Architecture Decision Records in [docs/adr/](adr/). Key ones:

| ADR | Decision | Why |
|-----|----------|-----|
| [001](adr/001-json-for-config-and-db.md) | JSON for config & DB | AOT-safe, zero deps |
| [002](adr/002-direct-binary-read-vs-wsdump.md) | Direct binary trailer read | No wsdump.exe dependency, milliseconds vs minutes |
| [003](adr/003-raw-metadata-preservation.md) | Raw metadata (no parsing) | Lossless, no format risk |
| [004](adr/004-seek-from-end-extraction.md) | 1MB seek-from-end | Constant memory, instant |
| [005](adr/005-aot-single-file-deployment.md) | AOT single-file | No runtime dependency |
| [006](adr/006-advisory-file-locking.md) | Sidecar lock file | Safe concurrent access |
| [007](adr/007-ui-detection-hierarchy.md) | 4-tier UI detection | Right UX in every context |
| [008](adr/008-dotnet-glob-pattern-matching.md) | DotNet.Glob | AOT-safe, clean API |

## AOT Constraints

This project is published as a Native AOT binary. This imposes hard constraints:

### JSON Serialization
All JSON serialization uses `System.Text.Json` **source generators** via `AppJsonContext`. Every type that touches JSON must be registered with `[JsonSerializable(typeof(T))]` on the context class in `Models.cs`. Do **not** use `JsonSerializer.Serialize<T>()` without the context — it will fail at runtime.

```csharp
// ✓ Correct
JsonSerializer.Serialize(db, AppJsonContext.Default.AplcoreDb);
JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);

// ✗ Will fail at runtime under AOT
JsonSerializer.Serialize(db);
JsonSerializer.Deserialize<AppConfig>(json);
```

### No Reflection
Avoid any APIs that rely on `System.Reflection.Emit` or dynamic type generation. The codebase uses no reflection.

### Dependency Validation
Every NuGet dependency must be AOT-compatible. Before adding a new package:
1. Check if it uses reflection internally
2. Test with `dotnet publish` (AOT) — warnings indicate problems
3. Prefer libraries with explicit AOT support or pure-managed implementations

## Trailer Extraction Algorithm

The aplcore binary contains a text section near the end marked by:
```
========================== Interesting Information
```

### Algorithm

1. Open file as `FileStream` (read-only, shared read)
2. Seek to `max(0, fileLength - 1MB)`
3. Read 1MB buffer
4. Search backwards in the buffer for the UTF-8 marker bytes
5. If not found: seek to `max(0, fileLength - 2MB)`, read with 128-byte overlap, retry
6. If found: seek to the marker's absolute position, read to EOF, decode as UTF-8

### Why Seek From End?
The trailer is crash metadata — its size is bounded by thread count, address space entries, and APL stack depth, **not** by workspace size. In a 614MB aplcore, the trailer was ~164KB. A 1MB buffer provides 6x margin.

### Buffer Boundary Handling
The overlap (128 bytes, longer than the marker) ensures the marker is never split across two non-overlapping reads.

## Output Renderer Pattern

`IOutputRenderer` abstracts all user-facing output. The pipeline never writes to `Console` directly.

```
IOutputRenderer
├── PlainRenderer           # Console.WriteLine, safe for redirection
├── TeamCityRenderer        # Extends PlainRenderer, adds service messages
└── SpectreRenderer         # Spectre.Console rich UI
```

### Selection Logic (Program.cs)

```
--ci flag set?
  └── Yes → TEAMCITY_VERSION set? → TeamCityRenderer / PlainRenderer
  └── No → TEAMCITY_VERSION set? → TeamCityRenderer
            Console.IsOutputRedirected? → PlainRenderer
            Otherwise → SpectreRenderer
```

### Adding a New Renderer
1. Implement `IOutputRenderer`
2. Add detection logic in `SelectRenderer()` in `Program.cs`
3. No other changes needed — the pipeline is renderer-agnostic

## Database & Locking

### Sidecar Lock Pattern
Instead of locking the DB file itself (which prevents atomic replace), we lock a separate file:
- `aplcore_db.lock` — held with `FileShare.None` for the duration of the run
- `aplcore_db.json` — read/written independently using temp-file + atomic move

### Crash Safety
The DB is saved after **each** successful archive, not at the end. If the process is killed:
- Already-archived files are recorded and won't be re-processed
- The file being archived when the crash occurred will be re-archived on next run (zip may be partial — the temp file is cleaned up)

### Atomic Writes
DB saves use the pattern:
1. Write to `aplcore_db.json.tmp`
2. `File.Move(tmp, db, overwrite: true)` — atomic on NTFS and most filesystems

## Adding New Metadata Sections

If a new section needs to be extracted from the trailer:

1. **MetadataSplitter.cs** — add a new `StringBuilder` and a `StartsWith` check in the line loop
2. **TrailerData** (Models.cs) — add a new `required string` property
3. **ArchiveService.cs** — add `AddTextEntry(archive, "new_section.txt", trailer.NewSection)`
4. Update documentation

## Versioning

[MinVer](https://github.com/adamralph/minver) computes the version from the nearest git tag (prefix `v`).

- **Tagged commit** (e.g., `v0.4.0`): `0.4.0`
- **Commits after a tag**: `0.4.1-alpha.0.<height>` (e.g., `0.4.1-alpha.0.3`)

The version is embedded as `AssemblyInformationalVersion` and displayed in the startup banner via:
```csharp
Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion
```

## CI/CD

Three GitHub Actions workflows:

| Workflow | Trigger | Action |
|----------|---------|--------|
| `pr.yml` | Pull requests | Build + AOT publish (verify only) |
| `release.yml` | `v*` tags on main | AOT publish + GitHub Release |
| `preview.yml` | Push to non-main branches | AOT publish + rolling draft release |

The preview workflow creates/updates a single draft release per branch, so testers always download the latest build without release clutter.
