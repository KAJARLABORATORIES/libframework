---
name: arch-check
description: Run a read-only architecture and API boundary audit on kajarlabs.Framework by delegating to the isolated arch-checker subagent. Triggered via /arch-check or when asked to audit project dependencies, cycles, SDL3 isolation, InternalsVisibleTo, or public API leakage.
---

# `/arch-check` — Delegate Read-Only Architecture Audit

Invoke the `arch-checker` subagent via `invoke_subagent` (passing `TypeName: "arch-checker"`, `Role: "Architecture Auditor"`) with any specific scope requested by the user (default: full solution `kajar-framework.slnx`).

Do **not** edit any files as part of this audit. Present the subagent's report directly to the user once it finishes.
