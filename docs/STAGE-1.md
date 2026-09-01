# Stage 1 — Terminal Core

## What exists
Stage 1 is the first usable vertical slice of terminal-command. It provides:

- ordinary shell command passthrough;
- deterministic natural-language routing for high-confidence common requests;
- optional Ollama-based tiny-model intent routing;
- `/help`, `/doctor`, `/history`, and `/exit`;
- deterministic allow / approval / deny policy separation;
- native and WSL execution backends;
- SQLite evidence/history;
- selectable slash completions with mouse support through `prompt_toolkit`;
- Windows/Linux CI on Python 3.11 and 3.12.

## Install for development
```bash
python -m venv .venv
# Windows: .venv\Scripts\activate
# Linux/WSL: source .venv/bin/activate
python -m pip install -e ".[test]"
```

Run:
```bash
terminal-command
```

Health check:
```bash
terminal-command --doctor
```

Disable model routing completely:
```bash
terminal-command --no-model
```

Select another Ollama router model:
```bash
terminal-command --model MODEL_NAME
```

## Authority model
```text
router/model -> proposes structured Action
policy       -> allow / require approval / deny
executor     -> performs approved action
history      -> records evidence
```

The model is never execution authority. Unknown or malformed model output cannot directly run a command.

## Architectural hypothesis instrumentation
History records routing source, rule ID, model ID/confidence, policy result, backend, execution result, and timing. Later experiments can compare:

1. deterministic-only routing;
2. deterministic + tiny local model;
3. larger-model/agent-assisted routing.

The target question is whether external deterministic structure increases useful capability-per-model-size without unacceptable failure or complexity.

## Explicitly not implemented yet
Stage 1 does not claim:

- autonomous coding/debugging workflows;
- security/pentesting packs;
- Docker execution;
- remote-machine control;
- persistent monitors;
- Authentik/OPA integration;
- automatic application/dependency updates;
- self-modification;
- automatic rollback for arbitrary system mutations;
- large-agent orchestration.

Those remain future modules and must prove they belong before entering the core.
