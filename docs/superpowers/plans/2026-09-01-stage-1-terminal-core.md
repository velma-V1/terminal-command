# Stage 1 Terminal Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first usable terminal-command vertical slice: shell passthrough, natural-language routing, slash menu, deterministic policy, native/WSL execution, local evidence history, doctor checks, optional Ollama routing, and Windows/Linux CI.

**Architecture:** Keep the core deterministic and small. All inputs normalize into a stable `Action`; routing proposes, policy authorizes, execution performs, and history records. Optional model routing is an adapter and cannot become authority or a startup dependency.

**Tech Stack:** Python 3.11+, stdlib dataclasses/enums/sqlite3/subprocess/urllib, `prompt_toolkit`, `rich`, `pytest`.

**Spec:** `docs/superpowers/specs/2026-09-01-terminal-core-design.md`

## Global Constraints
- Python 3.11+.
- Local-first; ordinary terminal use must work without network, Docker, WSL, or Ollama.
- Model output never executes directly and never determines final authorization.
- Every execution attempt must have a policy decision and evidence record.
- High-impact or unknown mutations require explicit approval.
- No Stage-1 dependency on Authentik, OPA, cloud services, or a large model.
- CI tests run without network-dependent services.

---

### Task 1: Package skeleton and stable contracts

**Files:**
- Create: `pyproject.toml`
- Create: `src/terminal_command/__init__.py`
- Create: `src/terminal_command/contracts.py`
- Create: `tests/test_contracts.py`
- Create: `.github/workflows/ci.yml`

**Produces:** `Action`, `RouteResult`, `ExecutionResult`, `InputKind`, `RiskLevel`, `PolicyDecision`.

- [ ] Write tests proving enum values serialize predictably and `Action` can round-trip through `to_dict()/from_dict()`.
- [ ] Push tests/workflow before production implementation and verify GitHub Actions fails because the package/contracts are missing.
- [ ] Implement only the package metadata and contracts needed to pass.
- [ ] Verify Windows/Linux CI on Python 3.11/3.12.

### Task 2: Deterministic input router

**Files:**
- Create: `src/terminal_command/routing.py`
- Create: `tests/test_routing.py`

**Consumes:** `Action`, `RouteResult`, `InputKind`.

**Produces:** `Router.route(text: str) -> RouteResult` with routing precedence slash -> obvious shell -> deterministic natural language -> optional model -> unresolved.

- [ ] Add failing tests for `/doctor`, obvious shell commands (`git status`, `python --version`, `ls`/`dir`), deterministic phrases (`show git status`, `show current directory`, `list files`), and unresolved language.
- [ ] Verify failures are due to missing router.
- [ ] Implement minimal deterministic rules with named rule IDs for telemetry.
- [ ] Verify tests and full CI.

### Task 3: Deterministic policy engine

**Files:**
- Create: `src/terminal_command/policy.py`
- Create: `tests/test_policy.py`

**Consumes:** `Action`.

**Produces:** `PolicyEngine.evaluate(action: Action) -> PolicyDecision` plus authoritative `RiskLevel` assignment.

- [ ] Add failing tests showing read-only commands auto-allow, mutating commands require approval, unknown commands require approval, and catastrophic patterns are denied.
- [ ] Include command families such as read-only Git/status/listing, file deletion, recursive deletion, privilege/elevation, package installation, process termination, and shell redirection.
- [ ] Implement pure deterministic policy; ignore model-provided risk as authority.
- [ ] Verify policy tests and full CI.

### Task 4: Native and WSL execution abstraction

**Files:**
- Create: `src/terminal_command/execution.py`
- Create: `tests/test_execution.py`

**Consumes:** approved `Action`.

**Produces:** `Executor.execute(action: Action) -> ExecutionResult` and backend-specific command construction.

- [ ] Add failing unit tests for Windows native command construction, POSIX native execution, WSL invocation, unavailable WSL behavior, timeout capture, and stdout/stderr/exit-code normalization.
- [ ] Add a platform-neutral integration test using Python itself to print a known marker rather than relying on `echo` shell syntax.
- [ ] Implement subprocess execution with `shell=False` wherever actions are structured; explicit shell actions use the platform shell deliberately.
- [ ] Verify execution tests and full CI.

### Task 5: SQLite evidence/history ledger

**Files:**
- Create: `src/terminal_command/history.py`
- Create: `tests/test_history.py`

**Consumes:** request text, `RouteResult`, `PolicyDecision`, optional `ExecutionResult`.

**Produces:** `HistoryStore.record(...)`, `HistoryStore.recent(limit)`, schema version table.

- [ ] Add failing tests for database creation, append/retrieve ordering, serialization of action/policy/execution, schema version, and absence of environment-secret persistence.
- [ ] Implement schema version 1 using stdlib `sqlite3` with transactions.
- [ ] Ensure incomplete/failed executions can still be represented explicitly.
- [ ] Verify tests and full CI.

### Task 6: Optional tiny-model router adapter

**Files:**
- Create: `src/terminal_command/model_router.py`
- Create: `tests/test_model_router.py`

**Consumes:** natural-language text.

**Produces:** `ModelRouter` protocol and `OllamaRouter.route(text) -> RouteResult | None`.

- [ ] Add failing tests with an injected fake transport for valid structured JSON, timeout, connection failure, malformed JSON, unrecognized action, and confidence propagation.
- [ ] Define a strict JSON response contract containing intent, action/tool, arguments, confidence, and explanation; risk is advisory only.
- [ ] Implement Ollama HTTP adapter using stdlib networking so Ollama itself is not a Python dependency.
- [ ] Ensure every adapter failure returns `None` and cannot break shell/slash/deterministic routing.
- [ ] Verify tests and full CI.

### Task 7: Doctor, slash registry, and interactive application

**Files:**
- Create: `src/terminal_command/doctor.py`
- Create: `src/terminal_command/commands.py`
- Create: `src/terminal_command/app.py`
- Create: `src/terminal_command/__main__.py`
- Create: `tests/test_doctor.py`
- Create: `tests/test_commands.py`
- Create: `tests/test_app_flow.py`

**Produces:** `terminal-command`, `/help`, `/doctor`, `/history`, `/exit`, selectable slash completions, approval flow.

- [ ] Add failing tests for dependency health states, slash registry lookup/completion, low-risk auto-execution, approval-required refusal without approval, denial, unresolved natural language, and history recording.
- [ ] Implement `prompt_toolkit` prompt with slash completion and mouse support, Rich startup/header/output, and injectable input/output for tests.
- [ ] Keep the startup header short; no permanent dashboard.
- [ ] Add `terminal-command --doctor` noninteractive mode for support/CI.
- [ ] Verify tests and full CI.

### Task 8: Product documentation and Stage-1 evidence benchmark hooks

**Files:**
- Modify: `README.md`
- Create: `docs/STAGE-1.md`
- Create: `tests/test_telemetry_contract.py`

**Produces:** clear install/run/test instructions and telemetry fields required by the central hypothesis.

- [ ] Add failing telemetry-contract test requiring routing source, deterministic rule ID, model ID/confidence when applicable, policy decision, backend, duration, and execution outcome in history records.
- [ ] Implement missing fields/migrations without adding speculative telemetry.
- [ ] Document current capabilities, explicit non-capabilities, architecture law, and future comparison: deterministic-only vs tiny-model-assisted vs larger-agent-assisted.
- [ ] Verify complete CI and inspect changed files for scope drift/placeholders.

## Completion gates
1. Windows and Linux CI green on Python 3.11 and 3.12.
2. No network service required by tests or normal shell/slash operation.
3. Model failures cannot execute actions or crash ordinary terminal use.
4. Policy unit tests demonstrate deny/approval/allow separation.
5. Every executed action receives an evidence record.
6. README does not claim unimplemented Stage-2+ capabilities.
7. Stage-1 remains installable as a normal Python package with one entry point.
