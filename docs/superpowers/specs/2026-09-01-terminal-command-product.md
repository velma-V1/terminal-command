> **SUPERSEDED:** This V1 product design is preserved as historical prototype context. Future architecture and implementation decisions must use [`2026-09-01-terminal-command-v2-architecture.md`](2026-09-01-terminal-command-v2-architecture.md) and the V2 Q0–Q20 review.

# Terminal Command Product Design

## Purpose
Build a Windows-first, WSL-capable terminal operating layer where ordinary shell commands, natural language, and selectable `/` commands all resolve into the same controlled action system.

## Central hypothesis
A mostly deterministic external system can let a tiny always-on model deliver substantially more useful task capability than the model alone by moving execution, tools, policy, state, verification, recovery, and escalation outside the model.

## Architectural law
Every feature, dependency, model, service, and architectural layer must prove the program is materially better with it than without it. If it can live as a capability, workflow, adapter, or view, it does not enter the core.

## Core abstractions
1. `Action` — normalized requested operation.
2. `Capability` — named, typed operation with argument schema, risk class, backend and verifier.
3. `Workflow` — ordered capability invocations with stop/verification rules.
4. `Project` — resumable local context: root, repo state, notes, last actions and known commands.
5. `Checkpoint` — recoverable snapshot metadata for mutations that support rollback.
6. `Evidence` — immutable-enough local record of route, policy, execution and verification.

## Stable authority boundary
```text
user -> deterministic/model router -> capability/action -> policy -> approval if required -> executor -> verifier -> evidence
```
The model may interpret or propose. It never authorizes, executes, marks success, or bypasses verification.

## Input resolution
1. `/` command.
2. Exact high-confidence natural-language rule.
3. Obvious/installed shell command.
4. Tiny local model -> capability ID + typed arguments when possible.
5. Model-proposed shell command only as an approval-gated compatibility fallback.
6. Unresolved; never guess-execute.

## Product checkpoints

### Checkpoint 1 — Stage-1 core
Existing: terminal UX, routing, deterministic policy, native/WSL execution, SQLite evidence, optional Ollama routing, doctor, CI.

### Checkpoint 2 — Capability engine and mature intent routing
- capability registry with schemas and deterministic aliases;
- capability-first model routing;
- typed argument validation;
- explicit escalation metadata;
- `/capabilities` and `/explain`;
- optional capabilities never break core startup.

### Checkpoint 3 — Projects, state, workflows and recovery primitives
- project discovery/register/resume;
- project-local state and notes;
- JSON workflow definitions and deterministic workflow runner;
- checkpoint manager using Git when possible and filesystem backups for explicitly supported files;
- `/project`, `/workflow`, `/checkpoint`.

### Checkpoint 4 — Engineering pack
Reusable capabilities/workflows for Git status/diff/log, test discovery/run, build, lint, dependency inspection, log inspection, process inspection and a bounded diagnose -> change -> retest workflow. No arbitrary autonomous self-modification loop.

### Checkpoint 5 — Daily-use pack
Reusable capabilities for files/search, disk/process/system inspection, archive/hash/duplicate analysis, app/url launching, recurring local jobs and lightweight process/command monitors. Persistent monitoring is opt-in and represented as stored jobs, not a hidden daemon dependency.

### Checkpoint 6 — Security, web and remote adapters
Local authorized-system security primitives: secret scanning, dependency audit, Semgrep when installed, port/process/network inspection, hashing, TLS/HTTP checks. Web fetch/search adapters and SSH/Tailscale-aware remote command construction are optional and policy-gated. No credential harvesting, stealth, persistence or unrestricted exploitation module.

### Checkpoint 7 — Install, update, rollback, polish and experiment harness
- Windows PowerShell installer/uninstaller and desktop shortcut;
- deterministic health/bootstrap command;
- GitHub release/update checker with version/hash verification hooks;
- application config and channel selection;
- safe self-update state machine with pre-update checkpoint and rollback metadata;
- benchmark runner comparing deterministic-only vs tiny-model-assisted routing on a versioned task corpus;
- final `/status`, `/update`, `/benchmark` surfaces.

## Dependency policy
Core runtime dependencies remain intentionally small. Prefer stdlib. `prompt_toolkit` and `rich` are presentation dependencies. Optional external tools are discovered at runtime and exposed through adapters/capabilities. No external SaaS is required for startup or ordinary local operation.

## Safety and reliability invariants
- Unknown actions require approval.
- Catastrophic patterns are denied.
- Model output is untrusted data.
- Mutations that claim rollback support must create a checkpoint first.
- Workflows stop on failed required verification.
- No background process is installed without explicit user action.
- External services/tools are optional and replaceable.
- Secrets are not stored in history.
- Remote/security actions are explicit, scoped and approval-gated.

## Evidence needed to prove value
For each capability/workflow record: route source, capability ID, model ID/confidence, policy decision, backend, duration, exit status, verification status, checkpoint ID when applicable and escalation count. The benchmark reports task success, unsafe-action rate, routing accuracy, latency and model-use rate.

## Completion definition
The complete product is installable on Windows, works without a model for deterministic/shell tasks, uses WSL when beneficial but not required, survives unavailable optional tools, exposes all major functions through capabilities/workflows instead of core branching, passes Windows/Linux CI, and has a short real-machine validation checklist before a release is called production-ready.
