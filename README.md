# Terminal Command

Terminal Command is a terminal-native personal operating layer for **normal shell commands, natural language, and selectable `/` commands**. The model interprets intent; deterministic code retains authority over execution, permissions, evidence, and recovery.

```text
input
  ├─ shell
  ├─ natural language
  └─ /commands
       ↓
deterministic router / tiny optional model
       ↓
typed capability
       ↓
policy gate
       ↓
executor (native / WSL)
       ↓
result + history + checkpoints
```

## Core behavior

- Normal shell commands remain usable; session `cd` persists.
- Natural language resolves deterministic rules and typed capabilities first, then optionally asks Ollama.
- Model output is a proposal, never authorization.
- Known read-only capabilities may run automatically.
- Mutating, remote, security, and explicitly approval-marked actions require confirmation.
- Catastrophic patterns are denied even if an action also carries approval/security metadata.
- Projects, workflows, recurring job definitions, history, and recovery checkpoints persist locally.
- No hidden automation daemon is installed.

## Capability packs

**Daily:** file search/hash/duplicates, archive inspection/creation, disk/system information, URL/path launching.

**Engineering:** Git status/diff/log, project test/build/lint discovery, dependency inspection, log tailing, process inspection, bounded diagnose workflows.

**Defensive security:** local secret scanning, dependency-audit adapters, static scanning, and local network inspection. Security actions always require approval. Optional installed tools such as Gitleaks, pip-audit, npm audit, cargo-audit, and Semgrep are used when present; bounded local fallbacks are explicit about degraded coverage.

**Web/remote:** bounded HTTP(S) retrieval and validated SSH/Tailscale SSH adapters. Network/remote actions require approval. Terminal Command does not collect or embed passwords.

## Slash commands

Run `/help` inside the application. Current command families include:

`/benchmark`, `/capabilities`, `/checkpoint`, `/doctor`, `/explain`, `/history`, `/jobs`, `/project`, `/update`, `/workflow`, `/exit`.

`/benchmark` scores routing only; it never executes proposed actions.

## Windows install

From a checked-out repository in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer creates a versioned release under `%LOCALAPPDATA%\TerminalCommand`, validates it with `terminal-command --doctor`, atomically sets the active release, writes a stable launcher, and creates a **Terminal Command** desktop shortcut. The shortcut opens the application in one terminal window.

For CI or custom locations:

```powershell
.\install.ps1 -InstallRoot C:\Tools\TerminalCommand -NoShortcut
```

Uninstall the owned install directory while preserving user history/state:

```powershell
.\uninstall.ps1
```

Add `-RemoveState` only when you also want `~/.terminal-command` deleted.

## Updates and rollback

Configuration lives in `~/.terminal-command/config.json`. Updates are disabled until `update_manifest_url` is set to an accessible HTTPS JSON manifest:

```json
{
  "version": 1,
  "model_enabled": true,
  "model": "qwen3.5:2b",
  "update_channel": "stable",
  "update_manifest_url": "https://example.com/terminal-command/update-manifest.json"
}
```

Manifest shape:

```json
{
  "version": "0.2.0",
  "artifact_url": "https://example.com/terminal_command-0.2.0-py3-none-any.whl",
  "sha256": "<64 hex characters>"
}
```

Update flow:

```text
/update check
/update prepare
/update apply 0.2.0
/update rollback
```

`check` and `prepare` require approval before network access. `prepare` verifies SHA-256 and stages the wheel without changing the active release. `apply` installs into a new release directory and runs that release's `--doctor`; only a healthy release can become current. Rollback switches the atomic release pointer back to the previous healthy release.

**Private-repository note:** Terminal Command intentionally does not store GitHub credentials. A private GitHub release URL will therefore need an authenticated/access-controlled distribution mechanism outside the app, or an HTTPS manifest/artifact endpoint the machine can access.

## Model

Default config targets `qwen3.5:2b` through local Ollama. The program remains usable if Ollama or the model is absent; deterministic routing, shell, slash commands, policy, projects, workflows, and local capabilities still work. Start explicitly without model routing using:

```powershell
terminal-command --no-model
```

## Development

```bash
python -m venv .venv
# Windows: .venv\Scripts\activate
# Linux/WSL: source .venv/bin/activate
python -m pip install -e ".[test]"
python -m pytest -q
terminal-command --doctor
```

Run the checked-in routing corpus:

```bash
python -m terminal_command.benchmark benchmarks/router_tasks.json
```

CI tests Windows and Ubuntu on Python 3.11 and 3.12, then performs package/install and Windows installer smoke validation.

## Boundaries

- The application is not an unrestricted autonomous shell.
- Security capabilities are for authorized defensive use and remain approval-gated.
- Model-generated raw shell is a compatibility fallback and does not bypass policy.
- Recurring jobs are definitions only; no hidden background daemon is created.
- Update activation is explicit; the application does not silently rewrite itself at startup.
- Optional third-party scanners and runtimes may be absent; `/doctor` reports optional availability.

See [`docs/REAL-PC-VALIDATION.md`](docs/REAL-PC-VALIDATION.md) for the final machine-validation checklist and [`docs/Q0-Q20-PROGRAM-DESIGN-REVIEW.md`](docs/Q0-Q20-PROGRAM-DESIGN-REVIEW.md) for architecture review criteria.
