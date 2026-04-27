# ADR-004: Seek-from-End Trailer Extraction Strategy

**Status:** Accepted  
**Date:** 2026-04-27

## Context

The "Interesting Information" text section is located near the end of aplcore binary files that can be multiple gigabytes in size. The tool must find and extract this section efficiently without reading the entire file.

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **Seek from end, 1MB buffer with overlap** | Near-instant; constant memory; handles 99.9% of cases | Must handle marker straddling buffer boundary; fixed buffer may miss extremely large trailers |
| **Scan backwards in growing chunks** | More robust for unusual trailer sizes | More complex; multiple seeks/reads; unlikely to be needed |
| **Sequential scan from start** | Guaranteed to find marker | Reads entire multi-GB file; unacceptably slow |
| **Maintain an index/offset cache** | Fastest on repeat access | Adds state; fragile if file changes; overkill for one-shot processing |

## Decision

**Seek from end with a 1MB initial buffer.** If the marker is not found, extend with a second overlapping read. The overlap region is at least 100 bytes (longer than the marker string) to handle straddling.

## Rationale

- In our 614MB sample, the entire text trailer was ~164KB. The marker was found ~160KB from EOF.
- A 1MB buffer provides 6x headroom over the observed trailer size.
- Even for significantly larger aplcores, the trailer size is bounded by crash metadata (thread count, address space entries, APL stack depth) — not by workspace size.
- The overlap technique is simple and eliminates the straddling risk completely.
- If the 1MB buffer fails (extremely unlikely), a fallback to 2MB with overlap provides a second chance before logging a warning.

## Consequences

- Memory usage is bounded to ~2MB regardless of aplcore file size.
- In the unlikely event of a trailer larger than the buffer, the tool falls back gracefully (archive without metadata + warning).
