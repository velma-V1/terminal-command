# Terminal Command Evidence

This file preserves the durable lessons from the discarded original V1/V2 implementation. The old Python implementation, its tests, benchmarks, install path, and version-specific architecture documents are not production dependencies of the current Terminal Command V1.

## Evidence status

- Original V1/V2 were exercised through repository tests and GitHub CI only.
- They were **not** proven by end-to-end operation on the target Windows 11 Home + WSL2 machine.
- Therefore their passing tests are evidence about individual mechanisms and design mistakes, not proof that the original product worked as a real terminal or autonomous engineering system.

## Lessons that survived

1. **The model is not authority.** Model output may interpret intent, propose goals, plans, repairs, or capabilities. Deterministic policy and the execution broker decide whether anything consequential happens.
2. **One consequential side-effect path.** Filesystem, process, network, package, remote, privileged, configuration, containment, update, and security actions must converge on the same immutable Action → policy → broker → evidence path.
3. **Approval binds to the exact action.** Approval must not authorize changed arguments, changed targets, re-routing, broader scope, or a later action that merely resembles the approved one.
4. **Revalidate the real target immediately before execution.** Paths, symlinks/reparse points, remote identities, DNS, process identities, packages, containers, and other mutable references can change after planning.
5. **Unknown is not success.** Exit code 0, a model claim, a scanner claim, or a single test is insufficient for consequential completion. Only independently verified postconditions are full success.
6. **Rollback is a capability, not a promise.** Automatic mutation requires a recovery class and a recovery mechanism that is proven before the mutation and verified after recovery.
7. **Catastrophic actions remain denyable even with approval metadata.** Approval is not a mechanism for bypassing absolute safety or scope rules.
8. **A real terminal requires real process semantics.** Persistent cwd/environment, streaming I/O, foreground ownership, Ctrl-C/cancellation, terminal resize, process-tree cleanup, PTY/ConPTY, bounded output, and disconnect handling cannot be faked by a captured-command wrapper.
9. **Windows and Linux are execution domains, not separate brains.** Windows owns authority; WSL is an execution/analysis arm. No duplicate planner, policy engine, memory, truth model, or autonomous Linux controller.
10. **Capabilities are not separate systems.** Debugging, repair, scanning, fuzzing, attack-testing of explicitly authorized targets, settings, updates, privacy, quarantine, jobs, file work, and normal shell operation share the same state, planning, authority, transaction, evidence, verification, and recovery machinery.
11. **Known work should not require an LLM.** Deterministic routing/planning should handle known commands, capabilities, dependencies, preconditions, effects, verifiers, and recovery. Models are escalation for ambiguity or genuinely novel synthesis.
12. **External tools are adapters, not truth authorities.** Mature scanners, debuggers, fuzzers, configuration tools, inventory tools, and isolation products should be reused where they win, but their output is untrusted evidence that Terminal normalizes, bounds, redacts, challenges, and verifies.
13. **Do not persist raw external evidence blindly.** Diagnostics, scanner output, model output, traces, and tool logs can contain credentials, secrets, hostile text, or excessive data. Evidence must pass bounded parsing, provenance labeling, and secret redaction before durable storage.
14. **Routing benchmarks are not product benchmarks.** A router can score perfectly while execution, target identity, recovery, verification, or real terminal behavior remains broken.
15. **GitHub CI is not target-machine proof.** Hosted Windows/Linux tests are necessary but cannot replace real Windows 11 Home + WSL2 qualification for WSL integration, ConPTY, desktop install, privilege boundaries, containment, networking/privacy, and recovery.
16. **No hidden daemon by convenience.** Permanent background machinery must prove measurable system-wide value. Prefer parent-owned lifetimes and on-demand adapters.
17. **Updates must be staged and independently validated.** New versions should be downloaded/staged separately, integrity/freshness checked, health-tested, atomically promoted, and rollback-capable. A hash alone is not sufficient protection against rollback/freeze attacks.
18. **Do not build infrastructure already solved better elsewhere.** Terminal owns orchestration, authority, evidence, composition, verification, and recovery; mature primitives should own commodity discovery, desired-state application, tracing, scanning, and specialized sanitization when they outperform custom code.
19. **Strong containment must be named honestly.** A Windows Job Object controls lifecycle, not hostile-code isolation. Linux process groups control descendants, not isolation. cgroups enforce resources/lifecycle, not a security boundary. Containers are not automatically hostile-workload sandboxes.
20. **The implementation must grow as one organism.** Building a "foundation" and then separate debugging/security/privacy/update systems caused architectural drift. Every new capability must immediately enter the same end-to-end Action/policy/evidence/recovery loop.

## Mechanisms retained from the discarded implementation

The old code itself is not retained, but several concepts were independently re-established in the current C# foundation:

- deterministic policy decisions;
- exact-action hashes and single-use approvals;
- explicit transaction/recovery states;
- independent verification outcomes;
- model-optional operation;
- local durable state;
- Windows/WSL separation;
- bounded execution and process-tree ownership;
- fail-closed protocol behavior.

## Mechanisms explicitly rejected

- Python V1/V2 as the production architecture;
- separate capability-pack systems with their own operational logic;
- implicit shell execution as the general executor;
- model-generated commands as an authority path;
- treating CI-only success as release proof;
- treating ordinary containers as the sole hostile-file boundary;
- duplicate Windows/Linux control planes;
- adding permanent daemons, RPC layers, policy engines, or model services without measurable capability gain.

## Governing rule

> Preserve evidence, not obsolete architecture.

Historical Git commits remain available as laboratory record. No current V1 runtime, test, build, or architecture decision may depend on the discarded V1/V2 implementation.