# Terminal Command V1 Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the active mixed V1/V2/V3 repository tree with one coherent Terminal Command V1: preserve legacy lessons as evidence, remove obsolete Python V1/V2 implementation, rename the current C# architecture from V3 to V1, incorporate the approved unified improvement architecture, and return the C# foundation to green.

**Architecture:** The existing C# foundation remains the production lineage. Windows is the sole authority/control plane; WSL is an execution arm. Every future capability uses one SystemGraph → deterministic planner → immutable Action → policy → broker → evidence → verification → recovery loop. The cleanup deletes obsolete implementation, not the historical Git record.

**Tech Stack:** .NET 10 LTS / C# 14, SQLite, Protocol Buffers over framed stdio, Windows Job Objects, WSL2/Ubuntu, GitHub Actions Windows + Ubuntu.

**Spec:** `docs/ARCHITECTURE.md`

## Global Constraints

- Work only on `feat/v1-unified-terminal`; do not modify or merge `main`.
- The current C# implementation is the new Terminal V1 production lineage.
- `docs/EVIDENCE.md` is the only required preservation artifact from the discarded Python V1/V2 implementation, besides the Q−1/Q0–Q32/Q∞ admission question set.
- Remove active Python V1/V2 runtime, tests, benchmark corpus, installer/update path, legacy CI, and obsolete version-specific architecture/plan documents.
- Preserve current C# source and tests.
- Rename active V3 references to V1 or neutral names.
- Do not claim real target-machine validation from GitHub CI.
- Do not merge any pull request without explicit user approval.

---

### Task 1: Preserve legacy evidence and canonical V1 architecture

**Files:**
- Create: `docs/EVIDENCE.md`
- Create: `docs/ARCHITECTURE.md`
- Create: `docs/superpowers/plans/2026-09-01-terminal-v1-unification.md`

**Interfaces:**
- Produces: the canonical evidence record and architecture used by all later tasks.

- [x] **Step 1: Record only durable V1/V2 lessons in `docs/EVIDENCE.md`.**
- [x] **Step 2: Write the unified Terminal V1 architecture in `docs/ARCHITECTURE.md`.**
- [x] **Step 3: Write this implementation plan.**

---

### Task 2: Rename the active V3 identity to V1

**Files:**
- Create: `.github/workflows/v1-foundation.yml`
- Delete: `.github/workflows/v3-foundation.yml`
- Create: `docs/superpowers/specs/2026-09-01-terminal-v1-architecture.md` only if a versioned spec alias is still required by tooling; otherwise use `docs/ARCHITECTURE.md` as canonical and delete the V3 spec.
- Delete: `docs/superpowers/specs/2026-09-01-terminal-v3-greenfield-architecture.md`
- Delete: `docs/superpowers/plans/2026-09-01-terminal-v3-foundation.md`
- Delete: `docs/superpowers/plans/2026-09-01-terminal-v3-execution-journal.md`
- Modify: current README.

**Interfaces:**
- Produces: one active version identity, `V1`.

- [ ] **Step 1: Copy the .NET workflow semantics into `v1-foundation.yml`, changing only active naming/path filters required by the cleanup.**
- [ ] **Step 2: Delete the V3 workflow.**
- [ ] **Step 3: Delete obsolete V3 plans/spec after `docs/ARCHITECTURE.md` has absorbed their still-valid decisions.**
- [ ] **Step 4: Rewrite README so it describes only the C# V1 architecture and current implementation truth.**

---

### Task 3: Remove the discarded Python V1/V2 product

**Files:**
- Delete: `src/terminal_command/**`
- Delete: root Python tests `tests/test_*.py`
- Delete: `pyproject.toml`
- Delete: `benchmarks/router_tasks.json`
- Delete: `install.ps1`
- Delete: `uninstall.ps1`
- Delete: `.github/workflows/ci.yml`
- Delete obsolete V1/V2 product/stage docs and plans after evidence extraction.

**Interfaces:**
- Preserves: `src/Terminal.*`, `tests/Terminal.*`, `Terminal.slnx`, `Directory.Build.props`, `global.json`, V1 architecture/evidence/Q review.

- [ ] **Step 1: Delete every file under `src/terminal_command/`.**
- [ ] **Step 2: Delete every root Python test under `tests/`.**
- [ ] **Step 3: Delete Python packaging, benchmark, legacy installer/uninstaller, and legacy Python CI.**
- [ ] **Step 4: Delete obsolete Stage-1/V1/V2 docs/specs/plans whose durable content is now in `EVIDENCE.md` or `ARCHITECTURE.md`.**
- [ ] **Step 5: Inspect the branch tree and prove no active Python runtime or V2/V3 architecture artifact remains except historical Git commits.**

---

### Task 4: Close the current RED WSL protocol checkpoint

**Files:**
- Modify: `src/Terminal.Windows/WslTransport.cs`
- Modify: `src/Terminal.LinuxAgent/LinuxAgentProtocolHandler.cs`
- Tests already RED: `tests/Terminal.Windows.Tests/WslTransportTests.cs`
- Tests already RED: `tests/Terminal.LinuxAgent.Tests/LinuxAgentProtocolTests.cs`

**Interfaces:**
- Produces: correlated Hello/Health/Heartbeat protocol semantics over one persistent stdio child.

- [ ] **Step 1: Confirm existing RED tests require `HeartbeatAsync`, request-ID correlation, unhealthy heartbeat fail-closed, malformed protocol fail-closed, and protocol-major fail-closed.**
- [ ] **Step 2: Add strict response `RequestId` equality check in `RequestAsync` before accepting Error or expected message type.**
- [ ] **Step 3: Add `HeartbeatAsync`, sharing probe logic with `HealthAsync` where doing so reduces duplication without changing semantics.**
- [ ] **Step 4: Add Linux agent `Heartbeat` handling returning `HealthResponse` with the request's exact ID/type.**
- [ ] **Step 5: Push and require Windows + Ubuntu V1 Foundation CI green.**

---

### Task 5: Preserve the Q review as the single architecture admission gate

**Files:**
- Rename/create: `docs/Q-1-Q32-PROGRAM-DESIGN-REVIEW.md`
- Delete: `docs/Q0-Q20-PROGRAM-DESIGN-REVIEW.md`

**Interfaces:**
- Produces: the same question-set content under a name matching its current Q−1/Q0–Q32/Q∞ scope.

- [ ] **Step 1: Copy the existing full question-set content unchanged except references to active V3/V2 naming, which become V1/Terminal where required.**
- [ ] **Step 2: Delete the stale filename.**
- [ ] **Step 3: Update README/architecture references to the new path.**

---

### Task 6: Verify repository identity and CI

**Files:**
- No new runtime files unless verification exposes a defect.

**Interfaces:**
- Consumes: all prior tasks.
- Produces: a clean V1 branch ready for continued implementation, not merge.

- [ ] **Step 1: Inspect the complete branch tree. Expected: only C# production/test code plus current V1 docs/tools/workflow; no Python V1/V2 runtime.**
- [ ] **Step 2: Search active filenames/content for `V2`, `V3`, `terminal_command`, `pyproject`, and stale Python install/benchmark references; resolve active leftovers. Historical evidence may mention V1/V2 deliberately.**
- [ ] **Step 3: Check GitHub Actions for the latest commit. Require V1 Foundation success on Windows and Ubuntu.**
- [ ] **Step 4: Treat real WSL/ConPTY/Windows-machine qualification as pending until actually run on the target PC.**
- [ ] **Step 5: Create a new draft PR from `feat/v1-unified-terminal`; close the obsolete V3 draft PR without merging it.**

## Completion definition

This plan is complete only when:

- the active branch calls the current architecture V1, not V3;
- legacy Python V1/V2 implementation is absent from the active tree;
- its durable lessons exist in `docs/EVIDENCE.md`;
- the Q admission set remains intact;
- `docs/ARCHITECTURE.md` contains the approved unified improvement design;
- C# WSL protocol tests return green on Windows + Ubuntu CI;
- no claim is made that hosted CI substitutes for target Windows 11 Home + WSL2 qualification;
- no merge to `main` occurs.