# Q0–Q20 Program Design Review

Use this review **only** when making architectural decisions, adding major capabilities, changing core behavior, or performing release-readiness review. Do **not** load it for routine implementation, debugging, small fixes, or normal maintenance.

## Q0 — What is the program’s irreducible purpose?
What single sentence explains why this should exist instead of just using Claude Code, Codex, PowerShell, WSL, and existing tools separately?

## Q1 — What makes it useful almost every day?
Identify the 5–10 highest-frequency jobs that justify opening it even when you are not coding or security testing.

## Q2 — What experience must be frictionless?
Define startup, shutdown, natural-language interaction, `/` menus, mouse selection, project resume, and one-click launching so using it is easier than opening a normal terminal.

## Q3 — What existing systems already solve parts of this better than we could?
Search aggressively for harnesses, TUIs, orchestration engines, workflow systems, scanners, debuggers, monitoring tools, update systems, identity/policy engines, and free services before custom-building anything.

## Q4 — What actually deserves to exist in the core?
For every proposed component ask:
- Is it used across most capabilities?
- Does putting it in core reduce total complexity?
- Is there measurable system-wide value?
- Could it instead be a plugin/workflow?

Anything that cannot prove itself stays outside.

## Q5 — How should user intent become an action?
Determine the optimal hierarchy:

```text
exact shell command
→ deterministic command matcher
→ tiny local model
→ larger reasoning model
→ clarification only when unavoidable
```

Measure routing accuracy, latency, and false-action rate.

## Q6 — What should the always-running small model actually do?
Define the minimum intelligence needed for intent classification, parameter extraction, context selection, risk estimation, and escalation. Find the smallest model that performs those jobs reliably.

## Q7 — When should stronger intelligence be activated?
Create deterministic escalation rules for complex debugging, architecture, coding, research, unknown commands, repeated failures, or low-confidence routing.

## Q8 — What is the universal execution abstraction?
Define one action format capable of targeting:
- Windows
- PowerShell
- WSL/Linux
- Docker
- Git
- APIs
- browser
- remote machines

without every capability inventing its own execution system.

## Q9 — What tools should exist as primitives?
Find the smallest high-value tool vocabulary. Prefer powerful reusable primitives over hundreds of specialized commands.

## Q10 — How is authority separated from intelligence?
Define:

```text
AI = proposes/interprets
policy = permits
executor = performs
verifier = proves
```

Determine what can execute automatically and what requires approval.

## Q11 — How does the system prove an action worked?
Every consequential workflow needs machine-verifiable completion criteria, not “the model thinks it succeeded.”

## Q12 — How does rollback work?
Determine checkpoints for files, Git, environments, configuration, packages, processes, and system changes so failed automation can reliably return to a known-good state.

## Q13 — What should the system remember?
Separate:
- session state
- project state
- machine state
- user preferences
- reusable workflows
- successful fixes
- historical evidence

Avoid dumping entire conversations into permanent memory.

## Q14 — How does it learn without becoming unpredictable?
Determine when successful work becomes:
- saved command
- workflow
- reusable skill
- project-specific procedure

Require evidence before learned behavior becomes automatic.

## Q15 — What everyday capability packs provide the highest value?
Rank candidate modules by:

```text
frequency × usefulness × uniqueness ÷ complexity
```

Likely candidates include Find, System Doctor, Projects, Build/Fix/Test, Automation, Monitor, Files, Security, Remote, Backup, Web/API.

## Q16 — Which external services should augment the system?
Evaluate free-for.dev and other existing infrastructure for observability, security, remote connectivity, testing, backups, AI fallback, automation, search, updates, etc. Every external service must be optional and replaceable.

## Q17 — How should the terminal interface work?
Design the minimal UX combining:
- ordinary shell commands
- natural language
- `/` command palette
- selectable menus
- mouse support
- progress/status
- diffs
- approvals
- evidence

without turning the terminal into a cluttered dashboard.

## Q18 — How does the program survive failure?
Test:
- WSL unavailable
- model unavailable
- network offline
- Docker stopped
- dependency missing
- interrupted update
- malformed AI output
- bad command
- disk full
- crash during mutation

The system should degrade gracefully rather than simply fail.

## Q19 — How do updates remain boring and safe?
Design:

```text
discover update
→ compatibility check
→ download
→ verify
→ checkpoint
→ install
→ health test
→ promote
or rollback
```

Separate core releases, plugins, external tools, models, and dependencies.

## Q20 — What evidence proves the program is ready?
Before release, require measurable thresholds for:
- intent-routing accuracy
- unsafe-action prevention
- successful task completion
- rollback reliability
- startup reliability
- CPU/RAM overhead
- latency
- offline functionality
- Windows/WSL compatibility
- installer/update reliability
- regression coverage

## Final architectural law

> **Every feature, dependency, model, service, and architectural layer must prove the program is materially better with it than without it.**

Run this Q0–Q20 review before architecture is locked, again after meaningful prototypes, and again before release.
