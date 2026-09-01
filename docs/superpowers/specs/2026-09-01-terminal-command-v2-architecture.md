# Terminal Command V2 Architecture

## Status
Approved redesign derived from the completed V1 prototype. This document supersedes the original product architecture for future implementation decisions. The V1 spec remains historical evidence of what the prototype assumed before implementation exposed deeper requirements.

## Irreducible purpose
Terminal Command is a Windows-first personal operating layer that lets shell commands, natural language, workflows, and AI use the same controlled execution system without granting intelligence direct authority over the machine.

The program should exist only if it can make ordinary terminal work easier while making consequential automation more observable, reversible, and provable than using an AI coding agent or shell alone.

## Central hypothesis
A small model can deliver disproportionately high useful capability when execution, authority, state, recovery, verification, and evidence are external deterministic services rather than responsibilities of the model.

V2 strengthens the hypothesis: the model is not the foundation. The foundation must remain useful and safe with no model running.

## Architectural law
> Every permanent layer must prove that it reduces total system risk or complexity, or provides system-wide value that cannot be cleanly supplied by an existing operating-system primitive, adapter, capability, workflow, or external tool.

Terminal Command owns **authority and orchestration**. It should reuse mature infrastructure rather than reimplement terminals, schedulers, sandboxes, databases, or transport protocols unnecessarily.

---

# Runtime architecture — most valuable order

```text
L9  Operator experience
    shell | natural language | /commands | approvals | status

L8  Intelligence
    deterministic rules -> tiny local model -> stronger model escalation

L7  Capability platform
    capabilities | workflows | projects | jobs | adapters

L6  State + evidence
    redaction | concurrency | provenance | history | metrics

L5  Verification
    postconditions | independent checks | typed verified outcome

L4  Transaction + recovery
    journal | checkpoint | crash recovery | rollback/compensation

L3  Execution + containment
    process supervisor | native | WSL | container | remote

L2  Policy + scope
    deny | least privilege | trust boundary | approval requirement

L1  Immutable Action contract
    canonical action | action hash | nonce | approval ticket

L0  Terminal/session substrate
    ConPTY/PTY | cwd | env | interactive I/O | signals | process state
```

The important change from V1 is that **routing and capabilities are no longer foundational**. A request is only safe and meaningful after the system can preserve terminal semantics, identify the exact action, bind authority to that action, execute it in an appropriate boundary, recover from interruption, and independently determine what happened.

---

# Layer requirements

## L0 — Terminal/session substrate
Terminal Command must behave like a real terminal host, not a sequence of captured subprocesses.

Required semantics:
- persistent working directory;
- persistent environment changes where explicitly supported;
- Windows ConPTY-backed interactive process support and an equivalent PTY abstraction where applicable;
- streaming stdin/stdout/stderr instead of unbounded `capture_output`;
- terminal resize propagation;
- foreground process ownership;
- Ctrl-C/cancellation and child-process-tree termination;
- background process representation without silently creating a daemon;
- bounded output buffers with optional spill-to-file;
- clean recovery when the UI disconnects from a child process.

**Build-vs-reuse rule:** use Windows Terminal/ConPTY and OS process primitives. Do not build a terminal emulator.

## L1 — Immutable Action contract
Every executable request becomes a canonical `Action` before authorization.

Minimum identity fields:
- action ID;
- capability ID or shell origin;
- canonical command/arguments;
- backend/trust boundary;
- cwd;
- relevant environment delta;
- requested resources/time limits;
- declared mutation scope;
- provenance/source;
- creation time and expiry where approval is required.

Canonical serialization produces an `action_hash`.

Once policy evaluation or approval begins, the action is immutable. Any changed argument, cwd, backend, environment, capability, or scope creates a new hash and invalidates prior authorization.

## L2 — Policy, scope, and exact-action authorization
Policy evaluates the canonical action, not user prose and not mutable model output.

Decision order:
1. hard deny/catastrophic boundary;
2. target/scope validity;
3. privilege/trust-boundary selection;
4. mutation and reversibility classification;
5. automatic allow vs explicit approval;
6. approval ticket issuance for the exact `action_hash`.

An approval ticket is single-purpose, bounded in lifetime, and cannot authorize a subsequently re-routed action. Approval is checked immediately before execution.

The model may recommend risk. Model-provided risk labels are never authoritative.

## L3 — Execution supervisor and containment
Replace the V1 single `subprocess.run` abstraction with a process supervisor.

Execution backends:
- `native` — trusted local Windows operations;
- `wsl` — Linux tooling and isolated Linux context;
- `container` — unknown/untrusted project code where practical;
- `remote` — explicit scoped SSH/Tailscale targets.

The supervisor owns:
- process lifecycle;
- streaming I/O;
- timeout and cancellation;
- output limits;
- process-tree cleanup;
- backend health checks;
- execution IDs;
- resource-budget hooks;
- interactive/noninteractive mode.

Containment is selected by trust and task requirements, not by AI preference alone.

## L4 — Transaction and recovery
Consequential operations follow a durable lifecycle:

```text
PREPARED
  -> AUTHORIZED
  -> STARTED
  -> MUTATED (when observable)
  -> VERIFYING
  -> COMMITTED
or
  -> FAILED / CANCELLED / INDETERMINATE
  -> ROLLED_BACK / COMPENSATED when supported
```

A crash-safe journal records transitions before and after consequential boundaries.

Every mutation declares one recovery class:
- `reversible` — deterministic rollback exists;
- `checkpointable` — restore from Git/file/config/environment snapshot;
- `compensatable` — inverse action is possible but not exact rollback;
- `irreversible` — no rollback claim is made and approval must say so.

Never imply universal rollback for arbitrary shell commands.

## L5 — First-class verification
Execution success and task success are separate facts.

Every typed capability may provide a verifier contract. Verification can inspect files, Git state, process state, HTTP responses, package state, tests, checksums, or other independent postconditions.

Typed completion states:
- `VERIFIED` — required postconditions proven;
- `FAILED` — required postconditions disproven or execution failed;
- `PARTIAL` — some required outcomes proven, others not;
- `UNVERIFIED` — action executed but no sufficient verifier exists;
- `CANCELLED` — operator/system cancelled;
- `INDETERMINATE` — crash or evidence loss prevents a trustworthy conclusion.

No model is allowed to convert `UNVERIFIED` into `VERIFIED` by assertion.

## L6 — State and evidence integrity
State should be divided by purpose rather than forcing everything into one storage mechanism.

Recommended split:
- human-editable config/manifests: versioned JSON/TOML where useful;
- concurrent operational state and transaction journal: SQLite;
- large output/artifacts: bounded files referenced by digest;
- project/workflow definitions: human-readable files if editability matters, with locking/version checks.

Required protections:
- redact secrets **before** persistence;
- never persist credentials merely because they appeared in input/output;
- cap inline stdout/stderr and store large artifacts separately;
- inter-process locking or transactional writes;
- schema migrations;
- action hash, execution ID, verifier result, checkpoint ID, model/escalation metadata;
- optional hash chaining for experiment-grade tamper evidence, not falsely described as immutable storage.

## L7 — Capability, workflow, project, and job platform
A V2 `Capability` is a contract, not merely a command builder.

Capability metadata should support, where relevant:
- typed arguments;
- aliases/intents;
- required tools/backend;
- declared permissions/scope;
- risk/mutation class;
- trust-boundary preference;
- idempotency/retry policy;
- timeout/resource budget;
- verifier;
- recovery/checkpoint strategy;
- health check;
- provenance/version.

Fields remain optional when not meaningful; do not turn simple read-only capabilities into ceremony.

### Workflows
Workflows compose capabilities through the exact same action/policy/verification path. They cannot inherit a blanket approval for actions that change after approval. Retries are bounded and evidence-driven.

### Projects
Projects hold resumable context, known commands, evidence references, and project-local workflows without becoming a permanent conversation dump.

### Jobs/monitoring
Stored jobs need an explicit runner. Prefer OS schedulers such as Windows Task Scheduler for persistence rather than installing a custom hidden daemon. Every triggered job still creates normal actions and evidence.

## L8 — Intelligence and escalation
Resolution order:

```text
explicit /command
-> exact/high-confidence deterministic intent
-> obvious shell command
-> tiny local model selecting a typed capability
-> stronger model for genuinely complex reasoning
-> clarification/unresolved
```

The tiny model should primarily perform intent classification, argument extraction, context selection, and escalation. Stronger models may plan and compose workflows but receive no additional authority merely because they are stronger.

Raw model-generated shell remains compatibility-only and approval-gated.

Context must be intentionally bounded: current project/session/evidence relevant to the request, not uncontrolled transcript accumulation.

## L9 — Operator experience
The UI remains one terminal window.

Required surfaces:
- ordinary shell behavior;
- natural language;
- `/` palette;
- exact-action approval preview including scope/backend/reversibility;
- `/status` showing session/backend/model/project/job/update health;
- `/explain` showing route -> action -> policy -> containment -> verifier before execution;
- progress/streaming output;
- cancellation;
- evidence and recovery summaries without a dashboard dependency.

---

# Cross-cutting management plane

## Updates and supply-chain trust
V1 SHA-256 verification is necessary but not enough if the manifest itself is compromised.

V2 update design should support:
- authenticated release provenance/signature verification when practical;
- manifest and artifact version binding;
- stable/development channels that actually select different manifests/releases;
- prepare without activation;
- pre-update transaction/checkpoint record;
- staged artifact re-verification immediately before install;
- health validation in the final release path;
- atomic active-version switch;
- rollback to previous healthy release;
- no silent startup modification.

Plugin/capability packs should use the same provenance principle if third-party installation is later allowed.

## External primitives
Prefer integration over reimplementation:
- Windows Terminal/ConPTY for terminal process semantics;
- WSL for Linux tool execution;
- Docker/container runtime for disposable execution where valuable;
- SQLite for transactional local operational state;
- Git/file snapshots for proven rollback domains;
- Windows Task Scheduler for persistent scheduled jobs;
- SSH/Tailscale for explicit remote transport;
- optional scanners/model runtimes behind replaceable adapters.

---

# Capability breadth after the foundation

Only after L0–L6 are trustworthy should breadth expand.

Recommended value order:
1. daily terminal/system/files/project operations;
2. build/test/debug/fix engineering operations;
3. prove/verify/recovery workflows;
4. monitoring/jobs/change intelligence;
5. defensive security inspection;
6. web/API/search/TLS diagnostics;
7. remote authorized-machine operations;
8. larger-model reasoning and workflow generation;
9. optional external service integrations.

Breadth remains removable. Lower-layer authority and evidence do not.

---

# Benchmark and proof system

The benchmark must test the architecture, not just routing.

Use the same task corpus across at least:
- deterministic-only;
- tiny-model-assisted;
- stronger-model-assisted where available.

Measure:
- routing/capability selection accuracy;
- end-to-end task completion;
- verifier-confirmed success;
- unsafe-action attempt and prevention rate;
- approval-binding violations;
- model-use and escalation rate;
- latency and resource overhead;
- output-limit behavior;
- cancellation/process cleanup;
- crash recovery and transaction reconciliation;
- rollback success by declared recovery class;
- secret-leak persistence rate;
- state concurrency correctness;
- offline degradation;
- Windows/WSL/container reliability.

The key experimental metric is **verified useful capability per model size/compute while holding safety and system complexity constant enough to compare**.

---

# Core admission test

Nothing is core merely because V1 already implemented it.

A component enters/stays in core only if most answers are YES:
1. Do many unrelated capabilities require it?
2. Does centralizing it remove duplicate safety/reliability logic?
3. Would omission create a system-wide correctness or security hole?
4. Is its interface stable enough to justify permanent coupling?
5. Is an OS/library primitive insufficient as an adapter instead?
6. Can measurable global value be demonstrated?
7. Is permanent maintenance cost lower than duplicated external implementations?

If not, place it in a capability, workflow, adapter, or view.

---

# Release blockers for V2

Do not call the redesigned architecture production-ready until all of these are demonstrated:
- exact-action approval cannot execute a changed/re-routed action;
- interactive terminal/process behavior works on the target Windows machine;
- cancellation cleans child process trees;
- output is bounded/streamed;
- secrets are redacted before durable evidence storage;
- concurrent state writers cannot silently overwrite each other;
- mutations accurately declare recovery class;
- verifier results are independent from model assertions;
- crash during a consequential action reconciles to a trustworthy state;
- update provenance/hash/health/rollback path is tested;
- no optional model, WSL, Docker, scanner, network service, or remote tool is required for basic local shell operation;
- end-to-end benchmark reports verified outcomes rather than routing accuracy alone.

## Final principle
> The model understands and proposes. The system identifies the exact action, decides authority, chooses the boundary, executes, recovers, verifies, and records what was actually proven.
