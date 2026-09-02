# Terminal Command V1 — Unified Architecture

## Mission

Terminal Command V1 is a Windows-first, deterministic-first autonomous computer engineering and assurance system that also functions as an excellent everyday terminal.

It must be able to inspect, understand, configure, build, test, debug, attack-test explicitly authorized systems, scan, fuzz, diagnose, repair, update, recover, verify, maintain, automate, quarantine, and operate software and machines with minimal babysitting.

AI is optional reasoning acceleration. AI never owns authority, execution, success determination, rollback claims, trust decisions, or security scope.

## Optimization target

> Maximum verified capability and autonomy with the minimum permanent machinery required to achieve it.

## Non-negotiable architectural rule

Terminal is **one system**.

Debugging, security, settings, updates, privacy, quarantine, automation, repair, normal shell use, and future capabilities are not separate autonomous subsystems. They are capabilities using the same state, planner, authority, Action, execution, evidence, verification, and recovery path.

```text
User / terminal / automation trigger
                |
                v
           Intent/Goal
                |
                v
           SystemGraph
                |
                v
     Deterministic planner/router
                |
                v
         Capability manifest
                |
                v
        Immutable Action/Plan
                |
                v
        Policy + authorization
                |
                v
          Execution broker
        /        |         \
   Windows      WSL      Disposable
 Job/ConPTY  Linux agent   isolation
        \        |         /
                v
              Evidence
                |
                v
            Verification
                |
        +-------+-------+
        |               |
      Commit       rollback/repair
        |               |
        +-------+-------+
                |
          update SystemGraph
```

## Platform

- **Authority/control plane:** Windows 11 Home.
- **Linux execution/analysis plane:** Ubuntu on WSL2.
- **Primary implementation:** .NET 10 LTS / C#.
- **Local durable operational state:** SQLite.
- **IPC:** one parent-owned persistent `wsl.exe -d <distro> -- terminal-linux-agent --stdio` process using bounded versioned framed Protobuf messages.
- **AI-off requirement:** normal terminal use and all known deterministic capabilities remain usable with every model disabled.

## Architecture laws

1. Windows owns final authority.
2. Every consequential side effect passes through one brokered immutable Action path.
3. Authorization binds to exact Action identity and current target identity.
4. Revalidate mutable targets immediately before execution.
5. Privilege is narrower than the application; normal Terminal runs as standard user.
6. Execution success is not task success; independent verification decides outcome.
7. Unknown, partial, flaky, environment failure, and oracle failure are explicit non-success states.
8. Rollback is claimed only when a recovery mechanism has been proven.
9. Known work is planned deterministically where practical.
10. External tools are replaceable adapters, never authority roots or truth monopolies.
11. No permanent component survives without measurable capability/safety/recovery value.
12. No capability may create its own duplicate planner, state store, authority path, recovery model, or autonomous brain.
13. External evidence is untrusted until bounded, parsed, provenance-labeled, and redacted.
14. Stronger intelligence never grants stronger machine permissions.
15. Real-machine qualification is mandatory before release claims.

---

# 1. Foundational contracts

## 1.1 ResourceRef

Every material target uses a typed resource identity instead of an opaque target string.

Minimum fields:

- environment owner: Windows / WSL / container / disposable VM / remote;
- resource kind: file, directory, process, service, package, repository, host, network endpoint, device, configuration resource, container, VM, artifact, other;
- canonical identity;
- display identity;
- stable OS/resource identity when available;
- owner/host/distro context;
- observed version or generation;
- freshness timestamp;
- revalidation method.

`C:\x`, `/mnt/c/x`, `\\wsl$\...`, a symlink target, and a remote path are never assumed equivalent by string comparison.

## 1.2 ScopeContract

Every consequential Action declares explicit scope:

- filesystem read/write roots;
- process/service scope;
- network destinations/protocols/listeners;
- data-egress allowance;
- package/repository scope;
- remote hosts/accounts;
- privilege requirements;
- device scope;
- security-test authorization boundary;
- resource/time limits.

Policy evaluates the typed scope, never user prose or model confidence.

## 1.3 Provenance

Every proposal, fact, artifact, finding, and external observation carries:

- source type;
- source identity/version;
- trust class;
- observation time;
- evidence reference;
- transformation/redaction lineage where applicable.

## 1.4 Action

An Action is the exact proposed observation or state transition.

Material identity includes:

- origin and capability ID;
- operation and typed arguments;
- backend;
- working-directory `ResourceRef`;
- environment delta;
- target `ResourceRef` set;
- `ScopeContract`;
- time/resource budget;
- mutation class;
- recovery class;
- provenance;
- expiry/version constraints when relevant.

Canonical serialization produces a deterministic SHA-256 `ActionHash`. Any material change creates a different Action identity.

## 1.5 Plan

A Plan is a dependency graph of immutable Action proposals plus goal/postconditions. Goal approval never authorizes later mutated Actions.

## 1.6 Capability

A capability is a declarative producer of Actions/Plans, not an executor.

Required metadata where applicable:

- typed inputs;
- preconditions;
- effects/postconditions;
- dependencies/health checks;
- required backend;
- scope/permissions;
- autonomy tier;
- resource budget;
- idempotency/retry semantics;
- recovery method;
- verifier;
- provenance/version.

## 1.7 SystemFact / SystemGraph

Facts are typed, provenance-labeled, freshness-aware observations. The SystemGraph relates projects, files, packages, processes, services, ports, containers, WSL distros, hardware, CI/CD, configurations, repositories, dependencies, security exposure, and current health.

External changes invalidate dependent facts/plans rather than silently reusing stale assumptions.

## 1.8 Evidence

All evidence uses a common normalized model. Core evidence classes include:

- `SystemFact`;
- `Finding`;
- `TraceEvent`;
- `TestObservation`;
- `Vulnerability`;
- `PropertyViolation`;
- `PerformanceObservation`;
- `ArtifactEvidence`;
- `VerificationResult`.

Pipeline:

```text
raw external output
→ size/resource bound
→ parser
→ provenance
→ secret/content redaction
→ normalization
→ evidence store
→ independent verifier/challenge
```

Raw external output is never automatically durable truth.

---

# 2. Planning and intelligence

Resolution order:

```text
explicit command/capability
→ deterministic intent/rule
→ deterministic planner over known capabilities/SystemGraph
→ tiny local model for ambiguous intent/argument extraction
→ stronger model for genuinely novel synthesis
→ operator/unresolved
```

Models may propose goals, constraints, candidate plans, repair hypotheses, or new capability drafts. They never execute directly.

Planner evaluation must benchmark the smallest custom deterministic planner against mature external planning engines before importing a heavy planner into runtime. Unified Planning / Fast Downward may serve as evaluation oracles first, not core dependencies by default.

---

# 3. Authority and autonomy

Policy outputs exactly:

- `AllowAuto`;
- `RequireApproval`;
- `Deny`.

Autonomy tiers:

```text
T0 OBSERVE
read/search/inspect/analyze/test
→ automatic

T1 EPHEMERAL / DISPOSABLE
candidate branches, temp files, disposable sandboxes/services
→ automatic

T2 VERIFIED REVERSIBLE LOCAL MUTATION
known repair/update/config transform + proven checkpoint + verifier
→ automatic when deterministic prerequisites pass

T3 VERIFIED REVERSIBLE CONTAINMENT
quarantine/stop/block exact authorized target
→ automatic only under strict deterministic policy

T4 CONSEQUENTIAL
privileged/system-wide/production/remote/security-boundary changes
→ explicit approval

T5 IRREVERSIBLE / UNKNOWN
unbounded destructive/root-of-trust/identity/uncertain external effects
→ approval or deny
```

Approval tickets are exact-action-bound, one-shot, optionally expiring, and revalidated against current target evidence immediately before execution.

---

# 4. Execution and containment

## Windows

- explicit executable + argv; no implicit shell as the general executor;
- suspended process creation;
- exact inherited-handle whitelist;
- Job Object assignment before untrusted code runs;
- job-wide lifecycle/resource accounting;
- ConPTY for interactive sessions;
- short-lived narrowly scoped elevated helper only for an already-authorized privileged Action.

A Job Object is lifecycle/resource containment, not hostile-code isolation.

## WSL/Linux

Windows launches one persistent Linux agent over framed stdio. Windows remains authority.

Preferred Linux lifecycle mechanism must be selected by measured tournament:

1. systemd transient unit/cgroup when available and it proves equivalent/stronger with less custom machinery;
2. direct cgroup v2 subtree;
3. process group/session fallback.

Containment strength is always reported honestly.

## Disposable hostile-workload boundary

Policy selects the least-risk boundary that can complete the work.

- ordinary trusted work: native Windows or WSL;
- untrusted build/test/fuzz inputs: disposable container/sandbox where appropriate;
- suspicious supported documents: dedicated document sanitization adapter such as Dangerzone rather than custom parser sandboxing;
- arbitrary hostile/unknown executable formats: disposable boundary with a separate kernel/VM-class isolation when required.

Ordinary shared-kernel containers are not treated as equivalent to hostile-workload VM isolation.

---

# 5. Durable state, transaction, recovery

SQLite remains the local operational journal.

Consequential lifecycle:

```text
PREPARED
→ AUTHORIZED
→ STARTED
→ SIDE_EFFECT_OBSERVED
→ VERIFYING
→ COMMITTED
or FAILED / CANCELLED / INDETERMINATE
→ ROLLING_BACK / COMPENSATING
→ ROLLED_BACK / COMPENSATED
```

Startup reconciliation inspects incomplete transactions and real machine state before declaring outcomes.

Recovery classes:

- none/observation;
- reversible;
- checkpointable;
- compensatable;
- irreversible.

T2/T3 automation requires a proven recovery path and independent post-recovery verification.

---

# 6. Unified capability portfolio

These are capabilities of Terminal, not separate systems.

## Everyday terminal

- PowerShell/Windows commands;
- WSL/Linux commands;
- persistent cwd/environment;
- files/search/open/launch;
- processes/services;
- `/` command palette;
- natural-language intent;
- foreground/background jobs;
- streaming I/O, Ctrl-C, resize, PTY/ConPTY.

## System discovery

Terminal owns fact identity/freshness/relationships. Commodity inventory should use mature sensors where they win.

Candidate preferred adapters:

- osquery on demand for cross-platform inventory/state;
- native Windows/PowerShell/WMI/CIM/ETW probes where stronger;
- Linux `/proc`, systemd/journald, package manager, Git/build tools;
- explicit capability probes before assuming optional tools exist.

No discovery daemon is required by default.

## Desired-state settings/configuration

Use Terminal policy/planning around a mature declarative engine when it wins. DSC v3 is the preferred candidate for Windows/Linux desired-state operations.

Flow:

```text
goal
→ planner
→ desired state
→ immutable Action
→ policy
→ DSC/native resource
→ independent verification
→ commit/rollback
```

DSC is an execution primitive, not Terminal's authority.

## Engineering/debug/repair

- project/language/build/test/lint/type discovery;
- reproduce/minimize failures;
- ETW Windows tracing;
- Linux strace/perf/gdb/lldb/journald/sanitizers as capability-probed adapters;
- evidence graph and causal localization;
- competing candidate repairs;
- disposable candidate execution;
- independent regression/adversarial verification;
- promote only verified repair.

Every escaped failure should create the smallest reusable detector/invariant/reproducer/recipe that catches its class next time.

## Authorized security assurance

Examples of replaceable adapters:

- Opengrep/Semgrep-class SAST/taint;
- OSV-Scanner/dependency vulnerability analysis;
- secret scanning;
- fuzzers/sanitizers;
- mutation testing;
- ZAP-class web DAST;
- Nuclei-class scoped template scanning only in disposable/least-privilege execution;
- native project security tests.

Security testing is limited to explicitly authorized targets and `ScopeContract` boundaries. A scanner never gets to define truth or expand scope.

## Privacy and quarantine

Privacy is another capability using normal policy/actions.

- VPN state/gate where configured;
- isolated Tor Browser workflow when requested;
- explicit download handoff into immutable quarantine;
- supported dangerous documents → sanitization adapter → verified safe derivative;
- unknown executables/binaries → stronger disposable isolation;
- no automatic host export from quarantine;
- network/egress restrictions are explicit policy state and tested for leaks.

## Updates and maintenance

- signed/fresh update metadata using TUF-style rollback/freeze-resistant design principles;
- stage new version separately;
- verify integrity/provenance/freshness;
- health-test candidate;
- atomic promotion;
- verified rollback;
- package/system maintenance through normal Action/policy/recovery path.

## Jobs and automation

Scheduled/recurring work is stored as declarative goals/capabilities. Execution occurs through the same planner, policy, broker, evidence, verification, and recovery path. No hidden alternate automation authority exists.

---

# 7. Tool isolation and evidence safety

Third-party tools may be buggy, compromised, overprivileged, or output secrets.

Therefore:

- tool versions and provenance are recorded;
- stdout/stderr/artifacts are bounded;
- secrets are redacted before durable storage;
- tools run at the least privilege and containment needed;
- dangerous scanners/parsers run disposable where justified;
- important conclusions receive independent challenge where practical;
- capability adapters remain replaceable.

Test-only mechanisms such as Microsoft Coyote may be used to systematically explore concurrency in approval consumption, transaction journaling, cancellation, heartbeat/disconnect, and state invalidation. They do not become runtime dependencies unless independently justified.

---

# 8. Completion sequence

This is a single integrated build sequence, not separate product systems.

## Gate 0 — strengthen the foundation

- finish WSL heartbeat and request-response correlation;
- typed `ResourceRef`, `ScopeContract`, and `Provenance`;
- evidence redaction/normalization boundary;
- crash reconciliation;
- hosted CI plus target Windows/WSL qualification.

## Gate 1 — real terminal + live SystemGraph

- ConPTY/PTY;
- persistent interactive sessions;
- foreground/background ownership;
- streaming/cancellation/resize;
- live discovery with osquery/native probes;
- freshness/invalidation.

## Gate 2 — deterministic capability composition

- capability manifests;
- deterministic planner;
- desired-state/settings via DSC/native adapters;
- known multi-step work without model planning.

## Gate 3 — engineering assurance loop

- project discovery;
- build/test/lint/type;
- tracing;
- reproduce/minimize/localize;
- candidate repairs;
- sandbox candidates;
- independent verification;
- verified promotion/rollback.

## Gate 4 — authorized security assurance

- SAST/dependency/secrets;
- fuzzing/sanitizers;
- mutation/oracle-strength checks;
- web DAST/scoped template scanning;
- strict attack scope verification.

## Gate 5 — privacy/quarantine

- VPN/Tor workflows;
- download quarantine;
- document sanitization;
- heavy disposable isolation for unsupported hostile binaries;
- leak/escape/export tests.

## Gate 6 — update/maintenance/jobs

- rollback/freeze-resistant updates;
- configuration/maintenance automation;
- scheduled goals;
- proven recovery.

## Gate 7 — AI escalation

- deterministic-first;
- tiny local ambiguity resolver;
- stronger model only for genuinely novel synthesis;
- AI-off baseline remains strong.

## Gate 8 — whole-program qualification

- Q−1/Q0–Q32/Q∞ review;
- fault injection;
- concurrency exploration;
- hostile/malformed protocol tests;
- real Windows 11 Home + WSL2 tests;
- containment/escape/privacy leak tests;
- recovery drills;
- verified task-success/autonomy/resource benchmarks.

---

# 9. Admission tournament

Every new permanent component competes against:

1. do nothing;
2. mature primitive;
3. thin adapter around mature primitive;
4. composition of capabilities already present;
5. deterministic custom mechanism;
6. AI-assisted mechanism;
7. strongest competing architecture found through Q−1;
8. best hybrid.

Then perform ablation, adversarial challenge, simplicity challenge, and capability challenge.

Winner rule:

> Choose the smallest surviving architecture that preserves the highest verified capability.

## Current architectural decisions

**Keep:**

- Windows authority;
- WSL execution arm;
- .NET 10/C# core;
- immutable Actions;
- exact approvals;
- SQLite journal;
- Windows Job Objects;
- parent-owned framed WSL stdio;
- independent verification;
- explicit recovery states;
- model-optional operation.

**Strengthen:**

- typed target/scope/provenance identity;
- evidence normalization/redaction;
- crash reconciliation;
- real terminal semantics;
- live SystemGraph.

**Tournament / preferred mature adapters:**

- systemd transient units vs direct custom cgroup management;
- osquery for inventory;
- DSC v3 for desired state;
- Dangerzone-class sanitization for supported dangerous documents;
- ETW for Windows causal evidence;
- Coyote as test-only concurrency exploration;
- TUF-style update metadata;
- Unified Planning/Fast Downward as planner benchmark/oracle before any runtime adoption.

**Reject by default:**

- separate autonomous capability systems;
- duplicate Windows/Linux policy brains;
- foundational TCP/gRPC daemon architecture;
- permanent privileged main process;
- ordinary containers as the only hostile-code boundary;
- LLM-as-authority or LLM-required known planning;
- raw external tool output as durable truth;
- bloat without measured capability gain.

## Release truth

GitHub CI can prove repository-level properties. It cannot alone prove Terminal works on the target machine.

A release claim requires the real Windows 11 Home + WSL2 qualification matrix to pass.