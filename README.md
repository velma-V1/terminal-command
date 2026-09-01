# terminal-command

Terminal-native personal operating layer for natural-language, shell, and selectable command workflows.

## Stage 1
The first vertical slice is under active development on the Stage-1 feature branch. Its architecture keeps authority deterministic:

```text
user input -> router/model -> policy -> executor -> evidence
```

The router/model proposes what the user means; deterministic policy decides whether an action may run.

Development install:

```bash
python -m venv .venv
# Windows: .venv\Scripts\activate
# Linux/WSL: source .venv/bin/activate
python -m pip install -e ".[test]"
terminal-command --doctor
terminal-command
```

Input supports normal shell commands, natural language, and selectable `/` commands. See [`docs/STAGE-1.md`](docs/STAGE-1.md) for the exact implemented/non-implemented boundary.

## Design review
Read [`docs/Q0-Q20-PROGRAM-DESIGN-REVIEW.md`](docs/Q0-Q20-PROGRAM-DESIGN-REVIEW.md) **only** when making architectural decisions, adding major capabilities, changing core behavior, or performing release-readiness review. Do **not** load it for routine implementation, debugging, small fixes, or normal maintenance.
