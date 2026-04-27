# ADR-008: DotNet.Glob for File Pattern Matching

**Status:** Accepted  
**Date:** 2026-04-27

## Context

The tool must match aplcore files using glob patterns (e.g., `aplcore*`) across recursive directory scans. The matching library must be AOT-compatible and handle the key requirement of matching files **without extensions** while excluding files with extensions (`.zip`, `.msg`, etc.).

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **DotNet.Glob** | Pure C#; no native deps; AOT-safe; well-maintained; familiar API | External NuGet dependency |
| **Microsoft.Extensions.FileSystemGlobbing** | Official Microsoft library; part of .NET extensions | Designed for file system traversal (not standalone matching); heavier; less intuitive API for single-pattern matching |
| **Manual regex/string matching** | Zero dependencies | Error-prone; must handle glob syntax ourselves; reinventing the wheel |
| **Directory.EnumerateFiles with searchPattern** | Built into .NET; no deps | Limited glob support (no `**`); inconsistent behavior across OS; no negation |

## Decision

Use **DotNet.Glob** (`Glob.Parse(pattern).IsMatch(filename)`) for pattern matching, combined with a **post-filter that excludes files containing a `.` in the filename portion** (after the matched prefix).

## Rationale

- DotNet.Glob is a pure managed library with no reflection usage — fully AOT-compatible.
- The glob pattern `aplcore*` naturally matches `aplcore_10` but also matches `aplcore_10.zip`. The post-filter for extension exclusion is simple and reliable: `!Path.HasExtension(filePath)`.
- The library is small (~30KB), actively maintained, and has 2M+ downloads.
- `Microsoft.Extensions.FileSystemGlobbing` is designed for "give me matching files from a directory tree" — it couples matching with I/O, which is less flexible than DotNet.Glob's pure matching API.

## Consequences

- One additional NuGet dependency (acceptable trade-off for correctness and maintainability).
- Extension exclusion is handled at the application level, not in the glob pattern itself. This is documented in the config file's comments/documentation.
- If more complex exclusion patterns are needed in the future, an `excludePatterns` config field can be added.
