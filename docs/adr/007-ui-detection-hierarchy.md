# ADR-007: UI Detection Hierarchy and Dual Output Modes

**Status:** Accepted  
**Date:** 2026-04-27

## Context

The tool must provide a rich interactive experience when run manually by developers (Spectre.Console) and clean, parseable output when run in CI (TeamCity, GitHub Actions) or when stdout is piped/redirected.

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **Detection hierarchy: flag → env var → redirection check → interactive** | Covers all cases; explicit override available; auto-degrades | Slightly complex detection logic |
| **Always plain text + optional `--fancy` flag** | Simplest | Poor default UX for interactive use; developers must remember flag |
| **Always Spectre.Console (let it degrade)** | Spectre handles some detection internally | TeamCity service messages need explicit handling; incomplete degradation |
| **Separate binaries (CLI vs CI)** | Clean separation | Double maintenance; deployment confusion |

## Decision

**Four-tier detection hierarchy:**

1. `--ci` flag → plain text (+ TeamCity service messages if `TEAMCITY_VERSION` is set)
2. `TEAMCITY_VERSION` environment variable → TeamCity mode (plain + service messages)
3. `Console.IsOutputRedirected` → plain text (no ANSI, no progress bars)
4. Default → full Spectre.Console interactive mode

## Rationale

- The `--ci` flag provides an explicit escape hatch for any CI system, not just TeamCity.
- `TEAMCITY_VERSION` is the standard way TeamCity agents identify themselves; auto-detecting it avoids requiring every job config to pass `--ci`.
- `Console.IsOutputRedirected` catches piping to files or other tools — Spectre.Console's progress bars would produce garbage in this context.
- The fallback to interactive mode means developers get a great UX without flags.

## Consequences

- TeamCity service messages (`##teamcity[progressMessage '...']`, `##teamcity[buildStatisticValue ...]`) are only emitted when TeamCity is detected.
- The output abstraction layer must support three rendering backends: Spectre.Console, TeamCity, and plain text.
- Testing requires exercising all four detection paths.
