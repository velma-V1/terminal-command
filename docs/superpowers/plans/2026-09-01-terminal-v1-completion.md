# Terminal Command V1 Completion Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:executing-plans with TDD for every production behavior change.

**Goal:** Complete the single Terminal Command V1 architecture through every repository-testable gate, leaving only gates that genuinely require the target unactivated Windows 11 Home + WSL2 machine.

**Architecture:** One SystemGraph → deterministic planner → capability → immutable Action → policy/authorization → execution broker → evidence → verification → recovery path. Windows remains the sole authority plane; WSL is an execution arm; disposable isolation is selected by policy and never becomes a second authority system.

**Tech Stack:** .NET 10 LTS / C# 14, SQLite, framed Protobuf stdio, Windows Job Objects + ConPTY, WSL2/Ubuntu, systemd/cgroup/process-group Linux lifecycle, GitHub Actions Windows + Ubuntu.

**Spec:** `docs/ARCHITECTURE.md`

## Global constraints

- Work only on `feat/v1-unified-terminal`; never merge `main` without explicit user approval.
- Target OS is **unactivated Windows 11 Home** with WSL2/Ubuntu. Windows activation may not be required for any runtime capability.
- One authority path, one state/evidence model, one planner, one recovery model.
- AI never executes or authorizes; AI-off remains useful.
- Unknown is not success. Exit code 0 is not task proof.
- Every new permanent component must beat do-nothing, mature primitive, thin adapter, and simpler composition alternatives.
- Security testing is restricted to explicit `ScopeContract` authorization.

---

## Gate 0 — harden foundational contracts

1. Add typed `ResourceRef`, `ScopeContract`, and `Provenance` contracts and make them material Action identity.
2. Add bounded evidence sanitation/normalization before durable persistence.
3. Add crash reconciliation that marks post-start unknown state `Indeterminate` unless deterministic evidence proves a stronger result.
4. Preserve strict WSL heartbeat/request correlation.
5. Require full Windows + Ubuntu CI green.

## Gate 1 — real terminal + live SystemGraph

1. Add persistent session contracts and foreground/background lifecycle ownership.
2. Add Windows ConPTY host with resize, Ctrl-C/cancellation, streaming I/O, and Job Object cleanup.
3. Add Linux PTY execution path through the existing WSL agent without creating a second authority plane.
4. Add typed freshness-aware `SystemFact`/`SystemGraph` and invalidation.
5. Add on-demand native inventory probes; optional osquery adapter only when present.
6. CI tests use deterministic fakes/pure tests where hosted runners cannot expose target WSL behavior; real target gate remains explicit.

## Gate 2 — deterministic capability composition

1. Add declarative capability manifests: inputs, preconditions, effects, scope, backend, autonomy tier, recovery, verifier, resource budget.
2. Add deterministic graph planner for known work.
3. Add desired-state capability abstraction with DSC v3/native adapter discovery; absence is a reported capability state, not a failure of Terminal itself.
4. Prove known multi-step work can plan without an LLM.

## Gate 3 — engineering assurance loop

1. Add project/build/test/lint/type discovery capabilities.
2. Add normalized trace/test/build evidence adapters.
3. Add reproduce → minimize → localize → candidate repair → isolated verification → promotion/rollback controller contracts.
4. Add Windows ETW and Linux trace adapter interfaces with capability probes.
5. Prove failed candidate repairs cannot be promoted.

## Gate 4 — authorized security assurance

1. Add scoped replaceable adapters for SAST, dependency vulnerability analysis, secret scanning, fuzzing/sanitizers, mutation/oracle checks, and web DAST/template scanning.
2. Require exact typed authorization scope before active security testing.
3. Normalize/redact every tool output before persistence.
4. Prove adapters cannot expand target/network/filesystem scope.

## Gate 5 — privacy/quarantine

1. Add network privacy state/gate contracts and explicit egress policy.
2. Add quarantine object lifecycle with no automatic host export.
3. Add document-sanitization adapter contract (Dangerzone preferred when available).
4. Add stronger disposable-isolation contract for unsupported hostile binaries; ordinary containers cannot claim VM-equivalent isolation.
5. Add leak/export/escape negative tests that can run without a real hostile environment.

## Gate 6 — update/maintenance/jobs

1. Add rollback/freeze-resistant update metadata verification (TUF-style principles, minimal native implementation or mature adapter after tournament).
2. Add stage → verify → health-test → atomic promote → verified rollback lifecycle.
3. Add declarative scheduled goals that always re-enter normal planner/policy/broker flow.
4. Add maintenance/configuration through normal recovery semantics.

## Gate 7 — AI escalation

1. Add deterministic intent first.
2. Add model-provider interface with zero authority.
3. Add tiny local ambiguity resolver contract; model absence is supported.
4. Add stronger-model escalation only for unresolved novel synthesis.
5. Prove disabling all models does not disable deterministic capabilities.

## Gate 8 — qualification

Repository/CI gates:
- full build/test on Windows + Ubuntu;
- malformed/fault/protocol tests;
- concurrency tests for approval/journal/cancellation/invalidation;
- recovery and negative-scope tests;
- AI-off tests.

Target-machine gates that cannot be honestly replaced by hosted CI:
- unactivated Windows 11 Home launch/runtime;
- real ConPTY interactive behavior;
- real WSL2 persistent agent + Linux PTY;
- systemd/cgroup tournament in the installed Ubuntu distro;
- privacy leak tests through actual configured VPN/Tor stack;
- disposable hostile-workload boundary/escape tests;
- real reboot/crash recovery drills;
- end-to-end benchmark/autonomy/resource measurements.

## Completion definition

Implementation continues until every CI-testable requirement above is green. Any remaining red/pending item must be identified as a real target-machine gate with an exact command/checklist to run. No release-complete claim is allowed before those target-machine gates pass.
