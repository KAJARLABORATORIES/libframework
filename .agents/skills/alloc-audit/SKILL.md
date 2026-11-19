---
name: alloc-audit
description: Run a read-only hidden heap allocation audit on the current git diff or specified C# files by delegating to the isolated alloc-auditor subagent. Triggered via /alloc-audit or when asked to audit code for boxing, closures, LINQ, or per-frame allocations.
---

# `/alloc-audit` — Delegate Read-Only Allocation Audit

Invoke the `alloc-auditor` subagent via `invoke_subagent` (passing `TypeName: "alloc-auditor"`, `Role: "Allocation Auditor"`) with the exact files, directory, or git diff scope requested by the user (default: working tree diff against `develop`).

Do **not** edit any files as part of this audit. Present the subagent's report directly to the user once it finishes.
