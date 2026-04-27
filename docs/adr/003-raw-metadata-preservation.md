# ADR-003: Raw Metadata Preservation vs Parsed Formatting

**Status:** Accepted  
**Date:** 2026-04-27

## Context

The metadata trailer uses a `!`-delimited encoding proprietary to Dyalog APL. When extracting `!AddressSpace:` and `!APLStack:` sections into separate files, we must decide whether to parse and reformat the data or preserve it verbatim.

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **Raw verbatim copy** | Zero parsing risk; lossless; matches source exactly; trivial implementation | Harder to read for humans unfamiliar with the format |
| **Parsed into clean TSV/aligned text** | More readable; easier to grep/sort | Parsing complexity; risk of data loss on edge cases; must handle unknown format variations; maintenance burden if format changes |
| **Both (raw + parsed view)** | Maximum flexibility | Doubles output size; twice the maintenance; overkill |

## Decision

**Raw verbatim copy.** All extracted metadata lines are copied exactly as they appear in the binary trailer, including `!` delimiters.

## Rationale

- The `!`-encoding, while unusual, is consistent and parseable by anyone who needs to script against it.
- Parsing introduces risk: the format is proprietary and undocumented. Edge cases in field delimiting (some values `!`-terminated, some not) could cause silent data loss.
- The primary consumers (APL developers) are already familiar with this format from `wsdump.exe` output.
- If parsed views are needed later, they can be built as a separate post-processing step without touching the archive format.

## Consequences

- Users unfamiliar with the `!` encoding will find the raw files less readable.
- No data loss risk from format parsing errors.
- Archives are a faithful record of what the aplcore contained.
