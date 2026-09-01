# Complete Terminal Command Product Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the remaining product stages on one feature branch with hard CI checkpoints, preserving a small deterministic core and moving breadth into reusable capabilities, workflows and adapters.

**Architecture:** Stage 1 remains the authority/execution foundation. The remaining work adds four reusable abstractions—capabilities, projects, workflows and checkpoints—then implements engineering, daily-use, security/web/remote, and lifecycle functionality on top. Optional tools are runtime-discovered; no SaaS or large model becomes a startup dependency.

**Tech Stack:** Python 3.11+, stdlib, `prompt_toolkit`, `rich`, `pytest`, SQLite, PowerShell installer scripts.

**Spec:** `docs/superpowers/specs/2026-09-01-terminal-command-product.md`

## Global Constraints
- Work only on `feat/stage-1-terminal-core` until final integration.
- Every checkpoint must pass Ubuntu/Windows CI on Python 3.11/3.12 before advancing.
- Model output never authorizes or executes directly.
- Prefer stdlib and runtime-discovered external tools over new hard dependencies.
- New breadth must enter as capabilities/workflows/adapters, not core conditionals.
- Mutations that claim rollback support must checkpoint first.
- Remote/security functionality is explicit, scoped and approval-gated.
- No external SaaS is required for normal startup or deterministic local tasks.

---

### Checkpoint 2: Capability engine and capability-first intent routing

**Files:**
- Create: `src/terminal_command/capabilities.py`
- Create: `tests/test_capabilities.py`
- Modify: `src/terminal_command/model_router.py`
- Modify: `src/terminal_command/routing.py`
- Modify: `src/terminal_command/commands.py`
- Modify: `src/terminal_command/app.py`
- Create: `tests/test_capability_routing.py`

**Interfaces:**
- Produces `Capability`, `ArgumentSpec`, `CapabilityRegistry`, `CapabilityInvocation`.
- Model router may emit `capability` + `arguments`; shell fallback remains compatibility-only.

- [ ] Add failing tests for capability registration, typed argument validation, aliases, capability-first model parsing, malformed/unknown capability rejection, `/capabilities`, and `/explain`.
- [ ] Verify the new tests fail for missing capability interfaces.
- [ ] Implement the registry and capability-first router without widening model authority.
- [ ] Run the complete CI matrix and do not advance until green.

### Checkpoint 3: Projects, workflows and checkpoints

**Files:**
- Create: `src/terminal_command/projects.py`
- Create: `src/terminal_command/workflows.py`
- Create: `src/terminal_command/checkpoints.py`
- Create: `tests/test_projects.py`
- Create: `tests/test_workflows.py`
- Create: `tests/test_checkpoints.py`
- Modify: `src/terminal_command/commands.py`
- Modify: `src/terminal_command/app.py`

**Interfaces:**
- Produces `ProjectStore`, `WorkflowStore`, `WorkflowRunner`, `CheckpointManager`.
- Workflow steps invoke registered capabilities through the same policy/execution boundary.

- [ ] Add failing tests for register/discover/resume project, project notes/state, JSON workflow validation, stop-on-failure behavior, Git checkpoints, explicit-file backup checkpoints, and slash surfaces.
- [ ] Verify RED.
- [ ] Implement minimal persistent local stores under the application state directory.
- [ ] Run full CI and advance only when green.

### Checkpoint 4: Engineering capability pack

**Files:**
- Create: `src/terminal_command/packs/engineering.py`
- Create: `src/terminal_command/packs/__init__.py`
- Create: `tests/test_engineering_pack.py`
- Modify: `src/terminal_command/app.py`

**Interfaces:**
- Registers `git.status`, `git.diff`, `git.log`, `test.run`, `build.run`, `lint.run`, `deps.inspect`, `logs.tail`, `process.inspect`.
- Defines bounded `engineering.diagnose` workflow that stops after evidence-producing steps; it does not grant autonomous mutation authority.

- [ ] Add failing tests for registration, command construction across Python/Node/Rust/Go project signals, read-only vs mutation policy classification, and bounded diagnose workflow.
- [ ] Verify RED.
- [ ] Implement only reusable project-aware capabilities and workflow definitions.
- [ ] Run full CI and advance only when green.

### Checkpoint 5: Daily-use capability pack

**Files:**
- Create: `src/terminal_command/packs/daily.py`
- Create: `src/terminal_command/jobs.py`
- Create: `tests/test_daily_pack.py`
- Create: `tests/test_jobs.py`
- Modify: `src/terminal_command/app.py`

**Interfaces:**
- Registers file/search/hash/archive/duplicate-analysis/system/process/disk/launch capabilities.
- Produces `JobStore` for opt-in command monitors/scheduled local jobs; no always-installed daemon.

- [ ] Add failing tests for safe file search, hashing, duplicate analysis, disk/process inspection command construction, platform URL/app launch, job persistence, due-job selection and explicit enable/disable.
- [ ] Verify RED.
- [ ] Implement local-first capabilities and opt-in job storage.
- [ ] Run full CI and advance only when green.

### Checkpoint 6: Security, web and remote adapters

**Files:**
- Create: `src/terminal_command/packs/security.py`
- Create: `src/terminal_command/web_adapter.py`
- Create: `src/terminal_command/remote.py`
- Create: `tests/test_security_pack.py`
- Create: `tests/test_web_adapter.py`
- Create: `tests/test_remote.py`
- Modify: `src/terminal_command/app.py`

**Interfaces:**
- Registers authorized defensive inspection/audit capabilities using installed tools when present.
- Produces bounded HTTP fetch and SSH/Tailscale-aware remote command construction; remote actions require approval.

- [ ] Add failing tests for tool discovery/fallback, secret/dependency/Semgrep audit construction, local port/network inspection, bounded HTTP response size, URL scheme restriction, SSH target validation and mandatory approval metadata.
- [ ] Verify RED.
- [ ] Implement adapters without credential capture, persistence or unrestricted exploitation helpers.
- [ ] Run full CI and advance only when green.

### Checkpoint 7: Installer, updater, rollback, polish and benchmark

**Files:**
- Create: `src/terminal_command/config.py`
- Create: `src/terminal_command/update.py`
- Create: `src/terminal_command/benchmark.py`
- Create: `benchmarks/router_tasks.json`
- Create: `install.ps1`
- Create: `uninstall.ps1`
- Create: `tests/test_config.py`
- Create: `tests/test_update.py`
- Create: `tests/test_benchmark.py`
- Modify: `src/terminal_command/commands.py`
- Modify: `src/terminal_command/app.py`
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces versioned config, update state machine, benchmark runner and Windows install/uninstall entry points.

- [ ] Add failing tests for config migration/defaults, update manifest validation, version comparison, hash verification, rollback-state creation, benchmark scoring and deterministic-vs-model mode separation.
- [ ] Verify RED.
- [ ] Implement update checking as safe metadata/prepare/apply primitives; do not silently self-modify during normal startup.
- [ ] Add PowerShell installer that creates a venv, installs the package, runs `--doctor`, creates a desktop shortcut, and stops on failure; uninstaller removes only files it owns.
- [ ] Extend CI with package-build/install smoke and PowerShell syntax/static checks where supported.
- [ ] Run exact-head full CI, inspect PR diff, scan for placeholders/scope drift, and update docs with final capability boundary.

## Final gates
1. Exact head green on Windows/Ubuntu × Python 3.11/3.12.
2. Package install and `terminal-command --doctor` green.
3. Capability engine is the extension path for all new packs.
4. Model failure or absence does not break ordinary terminal operation.
5. Unknown/mutating/remote/security actions cannot bypass deterministic policy.
6. Project/workflow/checkpoint state is local and versioned.
7. No service in free-for.dev or elsewhere is a hard dependency.
8. Benchmark can compare deterministic-only and tiny-model-assisted routing from the same corpus.
9. README clearly separates implemented capability from future ideas.
10. Final real-PC validation checklist is documented before production release.
