# Terminal Command Core Design

## Purpose
Build a terminal-native personal operating layer where the user can type ordinary shell commands, natural language, or `/` commands; the system interprets intent, applies deterministic policy, executes through a controlled backend, verifies outcomes where possible, and records evidence.

## Central architectural hypothesis
> A mostly deterministic external system can let a tiny always-on model deliver substantially more useful task capability than the model alone by moving execution, tools, policy, memory, verification, recovery, and escalation outside the model.

The product must preserve enough telemetry to test this hypothesis later against tiny-model-alone and larger-agent baselines.

## Architectural law
Every feature, dependency, model, service, and architectural layer must prove the program is materially better with it than without it. Anything that does not belong in the core must remain a plugin, workflow, adapter, or view.

## Stage 1 goal
Deliver a real, testable terminal core—not the complete mature product. Stage 1 provides:

1. Python 3.11+ installable package and `terminal-command` entry point.
2. Input classification for normal shell commands, `/` commands, and natural-language requests.
3. Structured `Action` contract shared by routers, policy, executors, history, and future models.
4. Deterministic routing for high-confidence common requests before any model is consulted.
5. Optional Ollama router adapter behind a stable interface; the application remains usable with no model running.
6. Deterministic risk policy that auto-allows read-only/low-risk actions and requires approval for mutation/destructive/privileged actions.
7. Native-shell and WSL execution backends behind one executor interface.
8. SQLite history/evidence ledger containing request, route, action, policy decision, execution status, timestamps, and duration.
9. Slash-command registry and selectable completion menu using `prompt_toolkit`.
10. `--doctor` and `/doctor` health checks for Python, platform, WSL, Git, Docker, and Ollama availability without requiring them all.
11. Machine-readable telemetry sufficient to compare routing source (`deterministic`, `model`, `shell`, `slash`) and escalation behavior later.
12. GitHub Actions tests on Windows and Linux.

## Non-goals for Stage 1
- No autonomous self-modification.
- No broad security/pentesting pack.
- No persistent background monitoring.
- No remote-machine control.
- No Authentik/OPA dependency.
- No Docker execution backend yet; Docker is health-detected only.
- No automatic software updates yet.
- No large-model coding agent embedded in the core.
- No permanent conversational-memory dump.

## Core units

### `contracts.py`
Defines stable enums/dataclasses: `InputKind`, `RiskLevel`, `PolicyDecision`, `Action`, `RouteResult`, `ExecutionResult`.

### `routing.py`
Classifies raw input. Precedence: explicit `/` command → known high-confidence natural-language rule → obvious/installed shell command → optional model router → safe unresolved result. Deterministic natural-language rules are deliberately narrow so normal terminal commands still pass through. The model produces structured JSON only; it never executes.

### `model_router.py`
Defines `ModelRouter` protocol and optional `OllamaRouter`. Failure, timeout, malformed JSON, or unavailable Ollama returns no route and never blocks ordinary terminal use.

### `policy.py`
Pure deterministic policy over structured actions. Read-only actions can auto-run. Mutating, privileged, destructive, or unknown actions require explicit approval; known catastrophic patterns are denied by default.

### `execution.py`
Executes approved actions using native shell or WSL. Captures stdout, stderr, exit code, timing, and backend. It does not decide permission.

### `history.py`
SQLite append-oriented ledger. Stores normalized request/action/policy/execution evidence. Schema migrations are versioned from the beginning.

### `commands.py`
Slash-command registry and handlers for `/help`, `/doctor`, `/history`, `/exit` plus future extension points.

### `doctor.py`
Fast dependency/platform checks. Missing optional tools are reported, not treated as application failure.

### `app.py`
Interactive prompt loop. Shell commands remain shell commands. Natural language becomes a structured action. Approval is requested only when policy requires it.

## Input decision order
```text
raw input
  -> slash command?             -> slash registry
  -> known deterministic NL?   -> structured action
  -> installed/shell command?  -> shell action
  -> model available?          -> structured intent JSON
  -> unresolved                -> explain/ask; never guess-execute
```

## Authority separation
```text
router/model = interprets or proposes
policy       = permits / requires approval / denies
executor     = performs
verifier     = later workflow-specific proof; Stage 1 always records exit status
history      = records evidence
```

## Risk policy
- `READ_ONLY`: auto-allow.
- `LOW`: auto-allow if target is local and command is in an allowlisted family.
- `MUTATING`: require approval.
- `PRIVILEGED`: require approval.
- `DESTRUCTIVE`: require approval, with command preview.
- `CATASTROPHIC`: deny by default; future explicit expert override may be designed separately.
- `UNKNOWN`: require approval rather than assuming safety.

Model output may suggest a risk level but policy computes the authoritative level from action/tool/arguments.

## Persistence
Default database location is platform-appropriate user state storage. Tests can inject an explicit path. No secrets, environment API keys, or raw credentials are persisted.

## UX
Startup should be fast and visually distinctive but immediately usable. The default screen remains a terminal prompt, not a permanent dashboard. Typing `/` opens selectable completions. Mouse support is enabled when the terminal supports it.

## Failure behavior
- Ollama unavailable -> deterministic/shell/slash functionality continues.
- WSL unavailable -> native execution continues; WSL actions report unavailable.
- Docker unavailable -> doctor warning only.
- malformed model output -> unresolved route, never execution.
- history DB unavailable -> action does not silently lose evidence; report the persistence failure.
- command failure -> record exit code/stdout/stderr and return control to user.
- interrupted session -> prior committed history remains readable.

## Stage 1 verification gates
- Unit tests cover contracts, routing precedence, policy, execution command construction, history, doctor, and slash registry.
- Integration tests prove platform-neutral native execution and history recording on Windows/Linux CI.
- Model-router tests use a fake HTTP transport; CI does not require Ollama.
- No test depends on network access.
- Windows and Linux CI must pass on Python 3.11 and 3.12.

## Future experimental instrumentation
Each routed request records at minimum: input kind, router source, deterministic-match identifier when applicable, model identifier when applicable, confidence when available, action type, policy result, execution result, and latency. This makes later comparisons of deterministic-only vs tiny-model-assisted vs larger-model-assisted routing possible without redesigning Stage 1.
