# Terminal V3 Greenfield Architecture

## Status
Approved greenfield redesign. The existing V1/V2 implementation is retained as experimental evidence and historical context, but no implementation detail, language, module, file format, or public interface is preserved unless it re-wins admission under `docs/Q0-Q20-PROGRAM-DESIGN-REVIEW.md`.

## Mission
Terminal is a Windows-first, deterministic-first autonomous computer engineering and assurance system that remains an excellent everyday terminal while being able to inspect, understand, test, attack-test explicitly authorized systems, diagnose, repair, update, recover, verify, and maintain software and machines with minimal babysitting.

AI is optional reasoning acceleration. AI never owns authority, execution, success determination, rollback claims, or trust decisions.

## Optimization target
> Maximum verified capability and autonomy with the minimum permanent machinery required to achieve it.

## Platform target
- Windows 11 Home is the authoritative host and user-control plane.
- Ubuntu on WSL2 is the primary Linux execution, analysis, build, testing, scanning, fuzzing, and containment plane.
- Basic local shell operation must not require WSL, containers, network access, scanners, or any model.
- .NET 10 LTS is the primary implementation platform for V3 core binaries.
- Heavy assurance and isolation mechanisms are lazy/on-demand.

## Architecture laws
1. **Windows owns authority.** WSL is an execution domain, not a second policy brain.
2. **Every consequential side effect passes through one brokered Action path.**
3. **Actions are canonical and immutable before authorization.**
4. **Authorization binds to exact Action identity and exact real-world target meaning.**
5. **Privilege is narrower than the application.** The normal app is never permanently elevated.
6. **Execution success is not task success.** Independent verification decides outcome.
7. **Unknown is not success.** `UNVERIFIED`, `INDETERMINATE`, `ORACLE_FAILURE`, and related states remain explicit.
8. **Rollback is claimed only when a recovery mechanism is proven.**
9. **Known work is planned deterministically where possible.** Models may propose novel plans but gain no authority.
10. **External tools are adapters, not authority roots.** Reuse mature infrastructure rather than rebuilding it.
11. **No bloat without measurable capability gain; no capability loss for simplicity.**
12. **Every escaped failure should produce the smallest reusable detector, invariant, reproducer, or recipe that would catch its class next time.**

---

# 1. Runtime topology

```text
User / Terminal UX
        |
        v
Windows Orchestrator
  |- Intent resolution
  |- System graph
  |- Deterministic planner
  |- Capability registry
  |- Policy / authority
  |- Transaction journal
  |- Evidence / verification
  |- Assurance controller
  |- Network privacy gate
  |- Update manager
  |
  +--> Windows Execution Broker
  |      |- native process supervisor
  |      `- narrow elevated helper
  |
  `--> WSL Transport Supervisor
         `- persistent `wsl.exe -d <distro> -- terminal-linux-agent --stdio`
                |
                v
         Ubuntu Linux Agent
           |- Linux process supervisor
           |- discovery adapters
           |- build/test adapters
           |- scanner/fuzzer adapters
           |- container/sandbox adapters
           `- verifier adapters
```

Windows is the only component that may make final policy/authorization decisions for cross-environment work.

---

# 2. Foundational contracts

V3 keeps the number of permanent contracts intentionally small.

## 2.1 Action
An `Action` is the exact proposed state transition or observation request.

Required material fields:
- `ActionId`
- `Origin`
- `CapabilityId` or explicit shell origin
- typed command/operation and arguments
- `Backend`
- `WorkingDirectory`
- environment delta
- target/resource identity
- filesystem/network/data-egress scope
- resource/time limits
- mutation class
- recovery class
- provenance
- creation/expiry metadata when required

Canonical serialization produces `ActionHash` using SHA-256 over a versioned canonical representation.

Any material field change creates a different hash.

## 2.2 Plan
A `Plan` is a dependency graph of immutable Action proposals plus explicit goal/postconditions. Blanket goal approval never authorizes later changed Actions.

## 2.3 PolicyDecision
Exactly:
- `AllowAuto`
- `RequireApproval`
- `Deny`

Policy evaluates canonical Actions and target state, never user prose or model confidence.

## 2.4 ApprovalTicket
Single-purpose, action-hash-bound, optionally expiring, non-reusable authorization. Revalidation occurs immediately before execution.

## 2.5 Transaction
Durable lifecycle for consequential work:

```text
PREPARED
-> AUTHORIZED
-> STARTED
-> SIDE_EFFECT_OBSERVED (when knowable)
-> VERIFYING
-> COMMITTED
or FAILED / CANCELLED / INDETERMINATE
-> ROLLING_BACK / COMPENSATING
-> ROLLED_BACK / COMPENSATED
```

## 2.6 VerificationResult
Legal terminal states include:
- `VERIFIED`
- `FAILED`
- `PARTIAL`
- `UNVERIFIED`
- `NOT_REPRODUCED`
- `FLAKY`
- `ENVIRONMENT_FAILURE`
- `ORACLE_FAILURE`
- `CANCELLED`
- `INDETERMINATE`
- `ROLLED_BACK`

## 2.7 Evidence
Bounded, provenance-labelled facts and artifacts that support planning, policy, diagnosis, verification, rollback, and audit.

## 2.8 Capability
Declarative producer of Actions/Plans. Capability construction is side-effect-free.

Minimum useful metadata where applicable:
- typed inputs
- preconditions
- effects/postconditions
- dependencies and health checks
- required backend
- scope/permissions
- autonomy tier
- resource budget
- recovery class/checkpoint strategy
- verifier
- provenance/version

## 2.9 SystemFact
A machine/project fact with source, timestamp, freshness/invalidation semantics, and stable identity where available.

---

# 3. Deterministic planning

Known goals should be solved from `SystemFact + Capability preconditions/effects + policy` before invoking any model.

Resolution order:

```text
explicit command/capability
-> deterministic intent/rule
-> deterministic planner over known capabilities
-> tiny local model for ambiguous intent/argument extraction
-> stronger model for genuinely novel synthesis
-> operator / unresolved
```

Models may return proposed goals, constraints, candidate Plans, repair hypotheses, or new capability drafts. They never directly execute.

---

# 4. Windows / WSL boundary

## 4.1 IPC winner
Use a persistent WSL child process with framed STDIO, not TCP as the foundational control plane.

Windows launches:

```text
wsl.exe -d <configured-distro> -- terminal-linux-agent --stdio
```

The Windows parent owns the child lifetime and exchanges versioned length-delimited protocol frames over stdin/stdout. This avoids localhost ports, NAT/mirrored-mode dependencies, firewall configuration, and duplicate service discovery.

## 4.2 Protocol
Use versioned Protocol Buffers messages carried in a small length-delimited framing layer.

Core message types:
- hello/handshake
- health
- action prepare
- action execute
- stdout/stderr stream
- signal/cancel
- verification request/result
- heartbeat
- system fact update
- terminal result/error

No protocol message is itself authority. Windows re-evaluates policy and tickets before consequential execution.

## 4.3 Path identity
Never treat `C:\x`, `/mnt/c/x`, and `\\wsl$\...` as equivalent strings.

Use a typed `ResourceRef` containing:
- environment owner (`Windows` / `Wsl`)
- distro when applicable
- canonical path
- display path
- filesystem kind
- stable identity where available
- freshness/version

Linux-heavy projects should normally live in the Linux filesystem; Windows-heavy projects should normally live on NTFS. Cross-boundary artifact transfer is explicit and evidenced.

---

# 5. Process supervision

V3 is a real terminal/process host, not a captured command runner.

Required semantics:
- streaming stdin/stdout/stderr
- interactive Windows ConPTY support
- Linux PTY support
- foreground ownership
- Ctrl-C/cancellation
- child process-tree termination
- terminal resize propagation
- bounded output with optional digest-addressed spill files
- backend health detection
- disconnect/reconciliation behavior
- resource budget hooks

Capabilities do not implement their own process lifecycle.

---

# 6. Privilege

## Windows
Normal app runs as the standard user. A short-lived elevated helper accepts only a validated exact privileged Action ticket and exits after that Action.

## Linux
Linux agent runs as a normal user. Privileged Linux operations should be typed and brokered through a narrow helper/sudo policy. Avoid `sudo sh -c <arbitrary model output>`.

---

# 7. State and evidence

Use SQLite for concurrent operational truth:
- actions
- plans
- approval tickets
- transactions
- executions
- verifications
- system facts
- evidence references
- jobs
- projects
- repair history
- update history

Use versioned TOML/JSON for human-editable configuration, policies, workflow definitions, and declarative capability manifests.

Large logs, traces, fuzz corpora, snapshots, and artifacts are bounded content-addressed files referenced by digest.

Secrets are redacted before persistence. Known-secret persistence rate must be zero in adversarial regression tests.

---

# 8. Assurance and repair plane

The heavy assurance engine remains dormant until needed.

Failure pipeline:

```text
DETECT
-> CAPTURE
-> REPRODUCE
-> CLASSIFY
-> MINIMIZE
-> LOCALIZE
-> GENERATE COMPETING REPAIR HYPOTHESES
-> ISOLATE CANDIDATES
-> ATTACK CANDIDATES
-> INDEPENDENTLY VERIFY
-> CHECKPOINT REAL TARGET
-> PROMOTE
-> POST-PROMOTION VERIFY
-> COMMIT or ROLLBACK
```

Applicable detector portfolio may include:
- existing/generated tests
- lint/type/static analysis
- data/taint flow
- property/state-machine testing
- mutation testing
- coverage-guided fuzzing
- sanitizers/runtime fault detectors
- metamorphic/differential testing
- API contracts/database invariants
- browser/visual/accessibility/performance testing
- dependency/supply-chain checks
- explicitly authorized DAST
- chaos/fault injection
- formal/model checking where proportionate
- runtime telemetry

No single green suite means general health.

---

# 9. Repair hierarchy

Prefer the least novel, most provable repair source:

1. known deterministic repair recipe
2. upstream-known fix/advisory
3. semantic transform/refactoring recipe
4. dependency/configuration correction
5. deterministic synthesis/search
6. tiny local model
7. stronger model/research
8. operator

Repairs that merely weaken tests, validation, security, error reporting, or functionality are anti-fixes and must be rejected unless the changed requirement itself is explicitly proven correct.

---

# 10. Autonomy tiers

```text
T0 OBSERVE
read/search/inspect/analyze/test
-> automatic

T1 SAFE + EPHEMERAL
sandboxes, temp files, candidate branches, disposable services
-> automatic

T2 VERIFIED LOCAL MUTATION
known local repair/update/config transform with checkpoint + verifier + proven rollback
-> automatic when deterministic gates pass

T3 REVERSIBLE CONTAINMENT
quarantine/stop/block exact verified target
-> automatic only under strict deterministic policy

T4 CONSEQUENTIAL
privileged/system-wide/production/remote/security-root changes
-> explicit approval

T5 IRREVERSIBLE OR UNKNOWN
unbounded destructive/identity/root-of-trust/uncertain external effects
-> approval or deny
```

AI confidence never raises autonomy tier.

---

# 11. Network privacy gate

Network privacy is an adapter-driven precondition, not a custom VPN implementation.

`NetworkPrivacyProvider` exposes status/connect/disconnect/verify-route/verify-DNS/verify-IPv6/health capabilities. Terminal independently verifies effective network state and marks sessions `PROTECTED`, `DEGRADED`, `OFFLINE`, or `UNPROTECTED_BLOCKED` according to policy.

Dark-web browsing is isolated from normal Terminal browsing and automation. Onion browsing should use a dedicated Tor isolation mode rather than routing the ordinary automation engine freely through Tor.

---

# 12. Heterogeneous Disposable Containment Stack (HDCS)

For genuinely untrusted downloads, hostile files, or explicit detonation workloads, ordinary containers are insufficient as the highest boundary.

HDCS is defense-in-depth with materially different consecutive mechanisms:

```text
Windows host guardian
-> disposable hardware-virtualized VM (QEMU/WHPX candidate)
-> userspace-kernel sandbox (gVisor candidate inside Linux guest)
-> rootless container with namespaces/seccomp/MAC restrictions
-> disposable viewer/detonation workload
```

Design principles:
- no host filesystem mounts
- no host credentials, SSH/API keys, browser profile, or Windows/WSL home exposure
- no clipboard, drag/drop, device or USB passthrough
- network removed before ordinary offline viewing
- immutable clean base + ephemeral writable overlay
- resource/process limits
- outer guardian lives outside the suspicious workload
- tripwire first cuts network, freezes/kills the disposable VM, destroys all inner layers and ephemeral storage, then verifies teardown
- file export back toward the host is a separate explicit sanitization path; never automatic

No claim of perfect containment is permitted. Hypervisor/hardware/firmware vulnerabilities remain part of the threat model.

---

# 13. Updates and supply-chain trust

All updates, including Terminal itself, use:

```text
discover
-> provenance/authentication
-> compatibility analysis
-> stage
-> checkpoint/generation
-> isolated update
-> functional/security/assurance tests
-> reverify immediately before activation
-> atomic promote
-> post-promotion health verification
-> rollback on failure
```

Low-consequence, checkpointed, independently verified updates may be automatic. Privileged/system-wide/root-of-trust updates remain consequential.

---

# 14. Source layout

```text
Terminal/
|- src/
|  |- Terminal.Core/
|  |- Terminal.Protocol/
|  |- Terminal.Windows/
|  |- Terminal.LinuxAgent/
|  `- Terminal.Cli/
|- capabilities/
|- policies/
|- workflows/
|- tests/
|  |- Terminal.Core.Tests/
|  |- Terminal.Protocol.Tests/
|  |- Terminal.Windows.Tests/
|  |- Terminal.LinuxAgent.Tests/
|  `- Terminal.Integration.Tests/
|- benchmarks/
|- docs/
`- tools/
```

The old Python implementation remains on historical branches/commits. V3 code does not need to retain its APIs.

---

# 15. Release proof

V3 is not production-ready until evidence demonstrates at least:
- real Windows terminal semantics
- Windows/WSL protocol streaming and cancellation
- exact immutable Action hashing
- approval cannot execute a changed Action
- mutable target revalidation
- broker bypass prevention through supported interfaces
- narrow elevation
- crash reconciliation
- accurate recovery classes
- independent verification
- secret-safe persistence/egress
- concurrency-safe state
- deterministic planning coverage
- AI-off useful task success
- repair reproduction/minimization/localization quality
- false-fix detection
- rollback success
- explicitly authorized attack-test containment
- update rollback/provenance path
- HDCS teardown behavior and boundary tests
- startup/CPU/RAM overhead budgets
- real Windows 11 Home + Ubuntu/WSL2 validation

## Final principle
> The system understands known state deterministically, plans known work deterministically, constructs exact immutable Actions, grants only consequence-appropriate authority, chooses the least-risk execution boundary, executes through a non-bypassable broker, reproduces and attacks failures, repairs through isolated candidates, independently verifies outcomes, rolls back when proof fails, and uses AI only when deterministic knowledge is insufficient.