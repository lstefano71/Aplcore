# ADR-005: AOT-Compiled Single-File Deployment

**Status:** Accepted  
**Date:** 2026-04-27

## Context

The tool must run on Windows servers and developer machines, some of which may not have the .NET runtime installed. It must also run in TeamCity CI agents and GitHub Actions runners.

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **AOT single-file** | No runtime dependency; fast startup (~50ms); single .exe; optimal for CLI tools | Larger binary (~15-30MB); some libraries incompatible; no reflection-based features |
| **Framework-dependent** | Small binary (~1MB); full .NET feature set | Requires .NET 10 runtime on every machine; version conflicts; deployment friction |
| **Self-contained (non-AOT)** | No runtime dependency; full feature set | Large bundle (~60MB+); slower startup than AOT; still needs JIT |
| **ReadyToRun (R2R)** | Good startup time; full compatibility | Still needs runtime or self-contained bundle; compromise solution |

## Decision

**AOT-compiled, self-contained, single-file executable** targeting `win-x64`.

## Rationale

- The tool is deployed to production servers where installing .NET runtimes requires change management. A single .exe eliminates this friction.
- CLI tools benefit most from AOT: startup time matters when the tool may process zero files and exit.
- All chosen libraries (DotNet.Glob, Spectre.Console 0.49+, System.Text.Json with source generators) are AOT-compatible.
- `win-x64` is the only required target — all Dyalog APL production environments are Windows.

## Consequences

- Must use `System.Text.Json` source generators (no reflection-based serialization).
- Must verify AOT compatibility of every NuGet dependency.
- Binary size will be larger than framework-dependent (~15-30MB vs ~1MB), acceptable for a server-deployed tool.
- Cannot use `System.Reflection.Emit` or dynamic code generation.
