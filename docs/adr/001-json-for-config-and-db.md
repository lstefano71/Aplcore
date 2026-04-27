# ADR-001: JSON for Configuration and Persistent Database

**Status:** Accepted  
**Date:** 2026-04-27

## Context

The tool needs a configuration file format and a persistent database format. Both must be AOT-compatible, human-readable, and dependency-light.

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **JSON** | Native `System.Text.Json` in .NET; AOT-safe with source generators; zero external deps; universal familiarity | No comments in standard JSON; verbose for deep nesting |
| **TOML** | Clean for config files; supports comments | Requires `Tomlyn` NuGet; limited AOT testing; less familiar to .NET devs |
| **YAML** | Human-friendly; widely used in DevOps | `YamlDotNet` has AOT issues; whitespace-sensitive; error-prone |
| **SQLite** (for DB) | Battle-tested for concurrent writes; SQL queries | Heavy dependency; native interop complicates AOT; overkill for a simple key-value ledger |

## Decision

Use **JSON** for both configuration and the persistent database.

## Rationale

- `System.Text.Json` is built into .NET and has first-class AOT support via `JsonSerializerContext` source generators.
- No additional NuGet dependencies for serialization.
- The database is a simple dictionary of file entries — JSON handles this naturally.
- The database will grow linearly with the number of aplcore files processed. Even at thousands of entries over decades, a JSON file remains fast to parse (< 1MB).
- SQLite's concurrency advantages are unnecessary — advisory file locking is sufficient for the expected single-instance-with-rare-overlap usage pattern.

## Consequences

- No comments allowed in config JSON (acceptable — the config is simple enough to be self-documenting).
- Advisory file locking must be implemented manually for concurrent access.
