# ADR-002: Direct Binary Trailer Reading vs wsdump.exe

**Status:** Accepted  
**Date:** 2026-04-27

## Context

Aplcore files contain a text-encoded metadata section ("Interesting Information") near the end of the binary. Historically, crash analysis required running `wsdump.exe -i` on the aplcore, which:
- Takes ~4 minutes for a 614MB file
- Produces ~166KB of output
- Is a closed-source Dyalog tool that must be distributed separately
- Cannot be invoked without a Dyalog installation

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **Direct binary read** | No external dependencies; near-instant (~ms); works anywhere; simpler deployment | Must handle buffer boundary edge cases; less metadata than full wsdump -i output |
| **Shell out to wsdump.exe** | Richer output (registers, C stack decoded by DLL names); proven tool | 4-minute processing per file; requires Dyalog installation on archive machine; not available on CI runners; blocking for multi-GB files |
| **Hybrid (direct read + optional wsdump)** | Best of both worlds | Complexity; two code paths; conditional dependency |

## Decision

**Direct binary read** of the trailer section. Seek from the end of the file with a 1MB buffer, find the marker string, extract text.

## Rationale

- The trailer contains all the metadata needed for archival purposes: version info, workspace ID, exception details, APL stack, address space map.
- Processing time drops from ~4 minutes to ~milliseconds.
- Zero external dependencies — the tool is fully self-contained.
- The trailer section in our 614MB sample was ~164KB. A 1MB buffer with overlap provides ample margin.
- Any metadata not in the trailer (decoded DLL names in address space, symbol-resolved C stack) is only needed for deep crash analysis, which remains the domain of the Python tools.

## Consequences

- The tool cannot extract information only available through wsdump.exe (e.g., DLL-name-resolved address space entries, decoded object graph).
- If the trailer format changes in future Dyalog versions, the parser will need updating.
- Buffer overlap handling adds minor complexity to prevent missing a marker that straddles two reads.
