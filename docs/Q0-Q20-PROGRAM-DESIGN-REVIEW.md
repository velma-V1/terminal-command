# Q0–Q20 V2 Program Design Review

Use this review **only** for architecture, core-behavior changes, consequential capability design, major lifecycle changes, or release-readiness review. Do **not** load it for routine implementation, debugging, small fixes, or normal maintenance.

This version was redesigned after the V1 prototype exposed a more important truth: **AI routing is not the foundation. Terminal fidelity, exact-action identity, authority, containment, recovery, verification, and evidence must work first.**

## Q0 — What irreducible problem justifies the program?
State in one sentence why Terminal Command should exist instead of simply combining Windows Terminal, PowerShell, WSL, Claude Code/Codex, and existing tools manually.

Also define the **kill condition**: what evidence would prove the extra operating layer is not worth its complexity?

## Q1 — Does it behave like a real terminal before AI is involved?
Prove the base experience preserves the terminal semantics users expect:
- persistent cwd;
- intentional environment/session state;
- interactive programs;
- streaming I/O;
- Ctrl-C/cancellation;
- terminal resize;
- child-process cleanup;
- foreground/background process state;
- bounded output.

Prefer ConPTY/PTY and operating-system primitives over building a terminal emulator.

## Q2 — What exactly is an Action?
Define one canonical, immutable executable object containing every field that can materially change what happens: command/arguments, cwd, backend, environment delta, capability, scope, resource limits, provenance, and mutation/recovery metadata.

Can the same action serialize deterministically and produce the same `action_hash`?

## Q3 — Is authorization bound to the exact Action?
Policy and approval must authorize the canonical `action_hash`, not the original user sentence.

Ask:
- Can anything re-route or mutate after approval?
- Does any changed field invalidate authorization?
- Is approval checked immediately before execution?
- Are tickets single-purpose and time-bounded where appropriate?
- Do catastrophic denies outrank every approval mechanism?

If YES is not provable, execution is unsafe.

## Q4 — What trust boundary should execute this Action?
For each action choose the least risky boundary that still works:

```text
native Windows
→ WSL
→ disposable container/sandbox
→ explicitly scoped remote target
```

Trust-boundary selection must come from deterministic policy/capability requirements, not model preference alone.

## Q5 — How is the process actually supervised?
Define process lifecycle independently from task logic:
- interactive vs noninteractive;
- streaming stdin/stdout/stderr;
- timeout;
- cancellation;
- child process tree cleanup;
- resource/output limits;
- disconnect/reconnect behavior where supported;
- backend health/unavailability.

A command runner that captures everything and waits is not sufficient terminal infrastructure.

## Q6 — What is the transaction lifecycle?
Every consequential action needs a durable state model such as:

```text
PREPARED
→ AUTHORIZED
→ STARTED
→ VERIFYING
→ COMMITTED
```

with explicit FAILED, CANCELLED, INDETERMINATE and recovery paths.

Can the system determine what happened after a crash at every transition?

## Q7 — What can actually be recovered?
Classify every mutation honestly:
- reversible;
- checkpointable;
- compensatable;
- irreversible.

Require the appropriate checkpoint/compensation strategy **before** execution when one is claimed. Never advertise generic rollback for arbitrary shell commands.

## Q8 — How is success independently verified?
Execution status is not task success.

Define a verifier/postcondition for every consequential typed capability where practical. Outcomes should distinguish:
- VERIFIED;
- FAILED;
- PARTIAL;
- UNVERIFIED;
- CANCELLED;
- INDETERMINATE.

The model cannot self-certify success.

## Q9 — Is evidence trustworthy without becoming dangerous?
Before persistence ask:
- Are secrets redacted before disk writes?
- Are outputs bounded?
- Are large artifacts stored separately and referenced by digest?
- Is action hash/execution/verifier/checkpoint/escalation provenance recorded?
- Can concurrent writes corrupt or silently overwrite evidence?
- Would optional hash chaining provide useful tamper evidence, or merely complexity?

Never confuse tamper evidence with immutable storage.

## Q10 — What state exists, who owns it, and how is concurrency controlled?
Separate:
- terminal/session state;
- project state;
- workflow definitions;
- job state;
- configuration;
- transaction state;
- evidence/history;
- large artifacts.

Choose storage by semantics. Human-editable state may remain JSON/TOML; concurrent operational state should use transactional storage/locking. Define schema migration and multi-instance behavior explicitly.

## Q11 — What truly belongs in core?
Run the core-admission test on **every component, including components already implemented**:
- Do many unrelated capabilities require it?
- Does centralization eliminate duplicated safety/correctness logic?
- Would omission create a global correctness/security hole?
- Is its interface stable?
- Could an OS/library primitive solve it better?
- Can measurable global value be shown?

If it cannot prove itself, move it to a capability, workflow, adapter, or view.

## Q12 — What is the minimum complete Capability contract?
A capability may need to declare:
- typed arguments;
- dependencies/health requirements;
- permissions/scope;
- mutation/risk class;
- preferred trust boundary;
- timeout/resource budget;
- idempotency/retry behavior;
- verifier;
- recovery/checkpoint strategy;
- provenance/version.

Keep metadata optional when meaningless. The contract should eliminate duplicated safety logic, not create bureaucracy.

## Q13 — How do workflows, projects, and jobs compose without gaining hidden authority?
Workflows must invoke normal capabilities through the same action/policy/execution/verification pipeline.

Ask:
- Are retries bounded and evidence-driven?
- Does changed workflow state invalidate previous approvals?
- Can projects resume without permanent transcript dumps?
- Do jobs use an explicit runner/OS scheduler rather than a hidden daemon?
- Does every triggered job still receive normal policy and evidence handling?

## Q14 — How should intent become an Action?
Use the cheapest trustworthy resolution path:

```text
explicit /command
→ exact deterministic intent
→ obvious shell command
→ tiny local model selecting typed capability
→ stronger model escalation
→ clarification/unresolved
```

Measure wrong-action rate, not only intent-classification accuracy. Raw model-generated shell is compatibility-only and approval-gated.

## Q15 — What intelligence earns the right to run?
Define separately:
- tiny-model responsibilities;
- stronger-model escalation triggers;
- context budget;
- allowed outputs;
- failure/timeout behavior;
- model-unavailable behavior.

A larger model may reason better; it does **not** receive more machine authority merely because it is larger.

## Q16 — Which capability breadth creates the most value after the foundation is trustworthy?
Rank additions by:

```text
frequency × usefulness × uniqueness × verifiability
─────────────────────────────────────────────────
complexity × risk × maintenance
```

Recommended order:
1. daily terminal/system/files/projects;
2. build/test/debug/fix;
3. prove/recovery;
4. monitoring/change intelligence;
5. defensive security;
6. web/API/search/TLS diagnostics;
7. remote operations;
8. external integrations.

## Q17 — What automation/monitoring model avoids turning the app into a daemon platform?
Decide when work runs:
- only while Terminal Command is open;
- explicit `run due jobs`;
- operating-system scheduler;
- persistent watcher only when its value clearly justifies it.

Prefer Windows Task Scheduler/OS primitives for persistent scheduling. Every automated trigger must re-enter the normal authority pipeline.

## Q18 — What should be integrated instead of built?
Before custom infrastructure, search for mature primitives for:
- terminal/ConPTY handling;
- sandbox/container execution;
- transactional storage/locking;
- scheduling;
- Git/file recovery;
- SSH/remote transport;
- scanning/testing;
- observability;
- model runtime;
- updates/signing.

Own orchestration and authority. Reuse infrastructure when it is safer and simpler.

## Q19 — Can the program update or extend itself without creating a supply-chain hole?
Require:

```text
trusted release provenance
→ manifest/version validation
→ artifact authentication/hash
→ prepare
→ pre-update journal/checkpoint
→ re-verify immediately before install
→ install into final versioned path
→ health verification
→ atomic activation
→ rollback
```

Stable/development channels must actually map to different trusted release sources. No silent startup mutation. Apply the same provenance logic to third-party capability packs if they are ever supported.

## Q20 — What evidence proves the architecture is ready?
Release evidence should cover more than unit tests:
- terminal/session fidelity;
- exact-action approval binding;
- catastrophic-action prevention;
- process cancellation/tree cleanup;
- output bounds;
- containment boundary selection;
- crash transaction reconciliation;
- rollback/compensation success by declared class;
- verifier-confirmed task success;
- secret persistence rate = zero for known test cases;
- concurrent-state correctness;
- deterministic/tiny/strong-model comparison;
- unsafe-action rate;
- model-use/escalation rate;
- latency/CPU/RAM overhead;
- offline degradation;
- Windows/WSL/container compatibility;
- installer/update/provenance/rollback reliability;
- real-machine validation.

The key experimental measure is **verified useful capability per model size/compute without sacrificing safety or hiding complexity in the harness**.

## Final architectural law
> **The model understands and proposes. The system identifies the exact action, decides authority, chooses the execution boundary, executes, recovers, verifies, and records what was actually proven.**

Run Q0–Q20 before locking architecture, after meaningful prototypes expose new facts, before admitting anything into core, and before a production release.
