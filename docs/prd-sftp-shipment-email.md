# PRD: Add post-archive SFTP shipment and shipment-summary email notifications

## Problem Statement

AplcoreHandler currently archives aplcore files locally but does not ship those archives to a remote destination and does not notify stakeholders when shipment is completed. This leaves manual operational steps, risk of duplicate transfers, and low visibility on what was actually delivered.

The goal is to add automated post-archive shipment and email notification while preserving the project’s existing crash-safe, JSON-based, AOT-compatible architecture and CI-friendly output behavior.

## Solution

Extend the current archive pipeline with two additive capabilities:

1. A shipment phase that uploads unshipped zip archives from the archive target directory to SFTP.
2. A notification phase that sends a compact shipment summary email after successful shipment activity.

From the user’s perspective:

- The tool archives aplcores as today.
- Then it uploads all not-yet-shipped archives (including historical backlog in the target directory).
- It marks confirmed shipments in the local DB to avoid retransmission.
- It sends one summary email when at least one upload succeeds.
- It provides a dedicated transfer UI in interactive mode and plain/CI-safe logs in non-interactive modes.
- Passwords can come from config or environment variables, with env vars taking precedence.

## User Stories

1. As an operations engineer, I want archived aplcore zips to be shipped automatically, so that I avoid manual transfer work.
2. As an operations engineer, I want shipment to include old unshipped archives, so that backlog is cleared automatically.
3. As an operations engineer, I want shipment to run after archival in the same run, so that one command handles end-to-end flow.
4. As an operations engineer, I want each successful upload marked in local DB, so that already shipped files are not retransmitted.
5. As an operations engineer, I want shipment identity tied to archive path, size, and modified time, so that regenerated files can be re-evaluated correctly.
6. As an operations engineer, I want conflict handling when remote file already exists, so that behavior is deterministic.
7. As an operations engineer, I want “remote exists with same size” treated as already shipped, so retries remain idempotent.
8. As an operations engineer, I want “remote exists with different size” treated as an error, so that data mismatch is visible.
9. As a deployer, I want SFTP credentials configurable, so that the tool can run across environments.
10. As a deployer, I want SFTP password optionally overridden by env var, so that secrets can stay out of config files.
11. As a deployer, I want SMTP password optionally overridden by env var, so that mail credentials can be managed securely.
12. As a maintainer, I want a clear precedence rule (env over config), so that credential resolution is predictable.
13. As an operator, I want transfer progress feedback in interactive mode, so that I can monitor large shipment runs.
14. As a CI maintainer, I want plain, parseable transfer output in CI/redirected mode, so that logs remain machine-friendly.
15. As an operator, I want transfer summary counters (uploaded/failed/pending), so that outcomes are obvious.
16. As an operator, I want retry with exponential backoff for network operations, so that transient failures self-recover.
17. As an operator, I want dry-run to avoid all network actions, so that I can preview safely.
18. As an operator, I want dry-run to show what would be uploaded and whether email would be sent, so that change reviews are safer.
19. As a stakeholder, I want one summary email after shipment success, so that I am informed automatically.
20. As a stakeholder, I want the email to include both successful and failed uploads, so that partial failures are visible.
21. As a stakeholder, I want bounded per-file details in the email, so that emails stay readable.
22. As an operator, I want To and Cc recipient lists, so that notifications reach both primary and secondary audiences.
23. As an operator, I want configurable sender and SMTP endpoint settings, so that company email infrastructure is supported.
24. As a maintainer, I want exit codes to represent operational health, so that schedulers and CI can react appropriately.
25. As a maintainer, I want fatal config/runtime errors separated from partial operation failures, so troubleshooting is clear.
26. As a maintainer, I want all JSON contracts in source-generated serialization context, so Native AOT behavior stays correct.
27. As a maintainer, I want new dependencies to be AOT-compatible, so single-file deployment constraints are preserved.
28. As a maintainer, I want shipment persistence to keep crash-safe incremental updates, so interrupted runs do not lose confirmed state.

## Implementation Decisions

- Extend the pipeline from `discover -> detect -> archive` to `discover -> detect -> archive -> ship -> notify`.
- Shipment source of truth is zip archives in the target archive directory (not raw aplcore inputs).
- Confirm shipment immediately after successful SFTP upload in the same run.
- Use shipment identity of normalized archive path + size + last-modified UTC.
- Apply SFTP remote conflict policy: skip and treat as confirmed when same name + same size exists; report error on size mismatch.
- Use `Renci.SshNet` (SSH.NET, MIT) for SFTP transfer (password auth only in v1).
- Use `MailKit` (MIT) for SMTP email delivery.
- SMTP default security mode is STARTTLS / port 587 (overridable by config).
- Email is sent only if at least one upload succeeds.
- Email body is plain text and bounded: include totals + first 20 shipped items + first 10 failed items, then truncate with “and N more…”.
- Recipient model includes To and Cc.
- Sender model requires `fromAddress`; `fromDisplayName` is optional.
- Password precedence is env var over config.
- Hardcoded env var names:
  - `APLCORE_SFTP_PASSWORD`
  - `APLCORE_SMTP_PASSWORD`
- Keep JSON-based config and DB approach per ADR-001.
- Keep AOT-safe dependency and serialization discipline per ADR-005.
- Keep lock/crash-safe persistence behavior per ADR-006.
- Extend output abstraction/renderers to support transfer-phase UI and summaries while preserving detection hierarchy per ADR-007.
- Exit code policy:
  - `0`: all attempted operations succeeded.
  - `1`: at least one archive/upload/email operation failed.
  - `2`: fatal config/runtime error.
- Keep module boundaries deep and testable:
  - ShipmentCoordinator (selection/state decisions)
  - SftpTransferService (transfer + retries + conflict policy)
  - NotificationService (email trigger + summary generation + send)
  - ConfigResolver (effective settings + credential precedence)
  - Persistence extension for shipment ledger data

## Testing Decisions

- Good tests assert external behavior and stable contracts, not internal implementation details.
- Strong tests are required for:
  - ShipmentCoordinator decision matrix (eligible/unshipped/confirmed/error states).
  - SftpTransferService retry behavior and conflict handling outcomes.
  - NotificationService trigger logic and summary truncation rules.
  - ConfigResolver precedence and validation behavior.
  - Shipment ledger persistence and matching rules (path+size+timestamp identity).
- Renderer tests should be lightweight contract/smoke tests for new transfer reporting paths in interactive/plain/teamcity implementations.
- Reuse existing repository testing style for isolated service-level behavior over end-to-end UI detail assertions.

## Out of Scope

- SFTP private-key authentication (password only in v1).
- HTML email formatting.
- Email attachments.
- Unbounded verbose email detail (no full stacks or large dumps in email body).
- Reworking core trailer extraction and archival semantics beyond what is needed for shipment/notification integration.
- Any shift away from Native AOT single-file deployment constraints.

## Further Notes

- This is an additive enhancement: archival remains primary and shipment/notification layers on top.
- Backlog shipment is explicit: all unshipped zips in target directory are eligible in non-dry-run mode.
- Dry-run remains network-free.
- The design intentionally favors deterministic, idempotent operational behavior and clear observability.
