# ADR-006: Advisory File Locking for Database Concurrency

**Status:** Accepted  
**Date:** 2026-04-27

## Context

Multiple instances of the tool may run concurrently (e.g., two TeamCity agents scanning different source directories but writing to the same target/DB). The JSON database file must not be corrupted by concurrent writes.

## Options Considered

| Option | Pros | Cons |
|--------|------|------|
| **Advisory file lock (FileStream with FileShare.None)** | Simple; OS-native; no external deps; works on network shares | Blocking; no queuing; must handle lock timeout/retry |
| **SQLite** | Built-in WAL concurrency; ACID transactions | Heavy dependency; native interop for AOT; overkill |
| **Named mutex** | Fast; well-understood on Windows | Doesn't work across network shares; Windows-only API |
| **No locking (single instance assumption)** | Simplest | Data corruption if assumption violated; fragile |
| **Optimistic concurrency (read-modify-write with version check)** | No blocking | Complex; requires file versioning; race window still exists |

## Decision

**Advisory file lock** using `FileStream` opened with `FileShare.None`. The tool acquires an exclusive lock on the DB file before reading, holds it through the processing cycle, and releases after writing.

## Rationale

- `FileStream` locking works on both local disks and Windows network shares (SMB).
- The lock duration is bounded: read DB → process files → write DB. For typical runs (seconds to minutes), this is acceptable.
- If a lock cannot be acquired, the tool retries with exponential backoff before failing gracefully.
- SQLite would solve concurrency more elegantly but adds significant AOT complexity and a native dependency — disproportionate for a simple JSON ledger.

## Consequences

- A crashed/killed instance may leave a stale lock (OS releases it on process exit, but network share locks may linger). The retry mechanism handles this.
- Long-running instances block other instances from starting. Acceptable given the expected usage pattern.
- If concurrent load increases significantly in the future, migrating to SQLite should be considered (see ADR-001).
