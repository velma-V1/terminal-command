# Terminal Command V1

Terminal Command V1 is a Windows-first, deterministic-first autonomous computer engineering and assurance system that also aims to be an excellent everyday terminal.

**One system, one authority path.** Debugging, repair, settings, updates, automation, authorized security testing, privacy/quarantine, normal shell work, and future capabilities all use the same state, planning, Action, policy, execution, evidence, verification, and recovery machinery.

```text
user goal / command
      ↓
SystemGraph
      ↓
deterministic planner/router
      ↓
Capability
      ↓
immutable Action / Plan
      ↓
policy + exact authorization
      ↓
Execution Broker
  ┌───┼─────────────┐
Windows WSL   disposable isolation
  └───┼─────────────┘
      ↓
normalized evidence
      ↓
independent verification
      ↓
commit / rollback / repair
```

## Core laws

- AI is optional reasoning acceleration, never authority.
- Windows owns final policy/authorization; WSL is an execution/analysis arm.
- Every consequential side effect goes through one brokered immutable Action path.
- Approval binds to the exact Action and current real-world target.
- Unknown, partial, flaky, environment failure, and oracle failure are not success.
- Rollback is claimed only when recovery is proven.
- Known work should be planned deterministically where practical.
- External tools are replaceable evidence-producing adapters, not truth authorities.
- Capabilities do not get separate planners, state stores, policy engines, or autonomous brains.
- GitHub CI does not substitute for real Windows 11 Home + WSL2 validation.

## Current implemented foundation

The active production lineage is .NET 10 / C# and currently contains:

- immutable Action identity and canonical hashing;
- deterministic policy/autonomy decisions;
- exact-action approval tickets with durable single-use SQLite consumption;
- durable transaction journal and explicit recovery states;
- independent verification outcome model;
- execution authorization bound to Action hash and target evidence;
- one execution broker before OS side effects;
- Windows Job Object process supervision with suspended child assignment, bounded output, timeout/cancellation, process-tree termination, and accounting;
- Linux process supervision with cgroup v2 when available and process-group fallback;
- framed bounded Protobuf protocol;
- one persistent parent-owned `wsl.exe ... terminal-linux-agent --stdio` transport;
- correlated Hello/Health/Heartbeat probes and fail-closed protocol behavior;
- Windows + Ubuntu foundation CI;
- a real-Windows WSL smoke gate for target-machine execution.

This is **not yet the complete product**. Interactive ConPTY/PTY, live SystemGraph, deterministic capability planning, auto-settings, full engineering/debug/repair, authorized assurance tooling, privacy/quarantine, update/maintenance automation, and AI escalation are completion gates in the canonical architecture.

## Approved completion direction

High-value mature primitives should be reused where they beat custom machinery:

- **osquery/native probes** — live machine/project discovery;
- **DSC v3/native resources** — desired-state settings/configuration;
- **ETW** — Windows causal/debug evidence;
- **systemd transient units** — candidate preferred WSL lifecycle/cgroup wrapper when they beat direct cgroup management;
- **Dangerzone-class sanitization** — supported suspicious document handling;
- **Opengrep/OSV/ZAP/fuzzers/sanitizers/etc.** — replaceable assurance adapters;
- **Coyote** — test-only systematic concurrency exploration candidate;
- **TUF-style metadata** — rollback/freeze-resistant update design;
- **Unified Planning/Fast Downward** — planner benchmark/oracle before any runtime adoption.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the unified design and completion gates.

## Evidence and architecture admission

- [`docs/EVIDENCE.md`](docs/EVIDENCE.md) preserves the durable lessons from the discarded original Python V1/V2 implementation. The old code is not a dependency of current V1.
- [`docs/Q-1-Q32-PROGRAM-DESIGN-REVIEW.md`](docs/Q-1-Q32-PROGRAM-DESIGN-REVIEW.md) is the mandatory Q−1/Q0–Q32/Q∞ admission review for major architecture decisions.
- [`docs/REAL-PC-VALIDATION.md`](docs/REAL-PC-VALIDATION.md) defines target-machine release qualification.

## Build and test

Requires the SDK pinned by `global.json`.

```powershell
dotnet restore Terminal.slnx
dotnet build Terminal.slnx --configuration Release
dotnet test Terminal.slnx --configuration Release --no-build
```

The hosted foundation matrix runs on Windows and Ubuntu.

Real WSL integration on a Windows 11 Home + WSL2 target:

```powershell
.\tools\v1-wsl-smoke.ps1 -Distro Ubuntu
```

## Repository status

Current V1 development occurs on a feature branch until qualification gates pass. **Do not merge to `main` until explicitly approved after verification.**

Historical Git commits remain laboratory evidence; the active repository tree intentionally does not carry the discarded Python V1/V2 product.