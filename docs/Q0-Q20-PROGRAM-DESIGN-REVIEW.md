# Q0–Q32 + Q∞ Maximum-Value Program Admission Review

This is the **hard architectural admission gate** for Terminal Command.

The target is a **deterministic-first autonomous computer engineering and assurance system** that remains an excellent everyday terminal while being able to inspect, test, attack-test authorized systems, diagnose, repair, update, recover, verify, and maintain software and machines with minimal babysitting. AI is optional reasoning acceleration—not the operating foundation, authority source, or substitute for deterministic structure.

## Non-negotiable laws

- **UNKNOWN IS NOT SUCCESS.** Unknown, conflicting, partial, or insufficient evidence stays explicit.
- **MOSTLY WORKS IS NOT COMPLETE.** Unsupported boundaries must be named and measured.
- **“THE MODEL CAN HANDLE IT” IS NOT ARCHITECTURE.** Use deterministic structure or proven tools whenever they can solve the class reliably.
- **EXIT CODE 0 IS NOT PROOF.** Consequential outcomes require independent postconditions.
- **NO ROLLBACK CLAIM WITHOUT A PROVEN RECOVERY METHOD.**
- **NO AUTHORITY BY INTELLIGENCE.** Bigger models never gain broader machine permissions because they reason better.
- **NO CORE ADMISSION BY CONVENIENCE.** Core exists only for cross-cutting invariants or proven system-wide value.
- **NO CUSTOM REIMPLEMENTATION WHEN A BETTER MATURE PRIMITIVE EXISTS.** Own orchestration, authority, evidence, and composition; reuse infrastructure.
- **NO SILENT DEFECT ESCAPE.** If health cannot be proven, return a non-success state.
- **NO FEATURE COUNTS AS HIGH VALUE IF IT DAMAGES DAILY USABILITY.** Heavy assurance machinery stays dormant until needed.
- **NO BLOAT WITHOUT CAPABILITY GAIN.** Permanent machinery must remove duplicated complexity/risk or add measurable capability unavailable through a simpler composition.
- **NO CAPABILITY LOSS FOR SIMPLICITY.** Reduce duplication and ceremony—not coverage, proof strength, autonomy, recovery, or useful breadth.

A failed hard gate means reject, redesign, isolate outside core, or explicitly downgrade the capability claim.

---

# A — Mission, no-AI baseline, and architectural density

## Q0 — Is Terminal Command materially more valuable than combining existing terminals, agents, scanners, CI, and scripts manually?
**PASS requires:** one authority/orchestration/evidence layer that measurably reduces human babysitting while increasing verified completion, defect discovery, recovery, and everyday usefulness.

**KILL CONDITION:** if a simpler existing composition achieves the same verified outcomes with lower permanent complexity, do not build or keep the layer.

## Q1 — Can the system remain highly useful with every model disabled?
AI-off mode should still provide, where applicable:
- real terminal behavior;
- system/project discovery;
- deterministic planning of known work;
- files/search/launch/system operations;
- build/test/lint/type workflows;
- known diagnostics and repair recipes;
- scanners/fuzzers/property tests;
- monitoring/jobs;
- safe updates;
- checkpoint/rollback;
- evidence/verification.

**FAIL if:** AI-off mode merely starts but loses the system's central value.

## Q2 — Can routine known work be planned deterministically rather than by an LLM?
Capabilities should expose machine-readable preconditions, effects/postconditions, dependencies, scope, cost, risk, trust boundary, verifier, recovery class, and idempotency/retry semantics.

Use graph/HTN/classical planning or equivalent deterministic composition when it is simpler and more reliable than model planning.

**FAIL if:** known workflows require an LLM merely to choose the next known step.

## Q3 — Does every permanent component pass the capability-density test?
Evaluate:

```text
system-wide capability × frequency × verifiability × autonomy gain
───────────────────────────────────────────────────────────────
complexity × risk × maintenance × duplication
```

If centralization does not remove duplicated correctness/safety logic or provide measurable global value, move it to a capability/workflow/adapter/view or remove it.

**FAIL if:** the program becomes more impressive on paper without becoming more capable in use.

---

# B — Real terminal and live machine understanding

## Q4 — Does it behave like a real terminal before autonomous machinery is involved?
Prove persistent cwd/environment where supported, interactive programs, streaming I/O, Ctrl-C/cancellation, resize, foreground/background process ownership, child-tree cleanup, bounded output, and reliable Windows behavior.

Prefer ConPTY/PTY and OS primitives rather than building a terminal emulator.

**FAIL if:** it is fundamentally only captured subprocess calls.

## Q5 — Can it automatically build and maintain an accurate system graph?
Discover and relate, where available:
- repositories/worktrees;
- languages/frameworks;
- build/test/lint/type systems;
- packages/dependencies;
- processes/services;
- ports/network bindings;
- WSL/containers;
- databases/queues/caches;
- frontend/backend/API boundaries;
- CI/CD;
- environment/configuration;
- hardware/resource constraints;
- update state;
- security exposure.

Every fact needs provenance, freshness, and invalidation semantics.

## Q6 — Can external changes safely invalidate stale assumptions?
Files, Git state, packages, processes, DNS, services, environment, remote hosts, or configuration can change outside Terminal Command.

**PASS requires:** freshness/version checks, target revalidation, plan invalidation, and transactional/locking behavior where required.

---

# C — Exact Actions and non-bypassable authority

## Q7 — Is every executable operation represented by one immutable canonical Action?
The Action contains every material execution dimension: origin, typed arguments/command, cwd/environment delta, backend, target identity, filesystem/network/data-egress scope, resource limits, mutation/recovery class, provenance, and expiry where relevant.

Canonical serialization must produce a deterministic `action_hash`.

## Q8 — Is authorization bound to that exact Action and exact real-world target?
Any changed command, argument, cwd, environment, backend, target, scope, capability, path/DNS resolution, or other material meaning creates a new Action/hash.

**PASS requires:** an approval ticket binds to one immutable Action and is checked immediately before execution.

**AUTOMATIC FAIL:** route → approval → route again → execute a different result.

## Q9 — Is there exactly one supported path to consequential side effects?
Models, planners, workflows, capability builders, views, and plugins may request Actions but cannot directly perform consequential filesystem/process/network/package/remote/privileged mutations.

**PASS requires:** a non-bypassable execution broker through supported interfaces.

## Q10 — Is privilege narrower than the application?
Normal operation stays unprivileged. Elevated work uses a short-lived helper for one already-authorized exact Action.

**FAIL if:** the whole application routinely runs elevated.

---

# D — Maximum safe autonomy without permission spam

## Q11 — Are autonomy tiers based on consequence and proof rather than AI confidence?
Minimum policy:

```text
T0 OBSERVE
read/search/inspect/analyze/test
→ automatic

T1 SAFE + EPHEMERAL/REVERSIBLE
sandbox work, temporary files, candidate branches, disposable services
→ automatic

T2 VERIFIED LOCAL MUTATION
known repair/update/config transform with checkpoint + verifier + proven rollback
→ automatic when deterministic gates pass

T3 REVERSIBLE CONTAINMENT
quarantine/stop/block an exact verified target with recovery
→ automatic only under high-confidence deterministic policy

T4 CONSEQUENTIAL
privileged/system-wide/production/remote/security-boundary changes
→ explicit approval

T5 IRREVERSIBLE OR UNKNOWN
unbounded destructive/root-of-trust/identity/uncertain external effects
→ approval or deny
```

**FAIL if:** low-risk deterministic work repeatedly asks permission.

## Q12 — Can every automatic Action prove its prerequisites before execution?
Automatic authority requires deterministic proof of target/scope, containment, recovery class, checkpoint when required, verifier availability, resource budget, and dependency/tool health.

**FAIL if:** autonomy primarily rests on model confidence.

## Q13 — Can a plan, workflow, job, or model silently expand authority?
Each consequential Action independently satisfies policy. Changed workflow state/new steps cannot inherit blanket authorization.

**FAIL if:** approving a goal implicitly authorizes unknown future mutations.

---

# E — Execution, containment, transactions, and recovery

## Q14 — Does policy choose the least-risk execution boundary that still satisfies the task?
Select deterministically among native Windows, WSL, disposable container/sandbox, and explicitly scoped remote execution.

Unknown/untrusted code should prefer disposable execution when practical.

## Q15 — Does one process supervisor own execution lifecycle?
It owns interactive/noninteractive execution, streaming I/O, cancellation, timeout, process-tree cleanup, output/resource bounds, backend health, execution IDs, and disconnect/recovery behavior.

**FAIL if:** capabilities reinvent process management.

## Q16 — Is every consequential Action transactionally journaled?
Lifecycle distinguishes at least:

```text
PREPARED → AUTHORIZED → STARTED → SIDE_EFFECT_OBSERVED → VERIFYING → COMMITTED
                                      └→ FAILED / CANCELLED / INDETERMINATE
                                              └→ ROLLED_BACK / COMPENSATED
```

**PASS requires:** trustworthy crash reconciliation at consequential boundaries.

## Q17 — Is recovery honest, predeclared, and verified?
Every mutation is reversible, checkpointable, compensatable, or irreversible. Rollback/compensation is itself verified.

**FAIL if:** arbitrary shell mutation is described as generically rollback-safe.

---

# F — Truth, evidence, and independent verification

## Q18 — Is task success independent from execution success?
Legal outcomes include `VERIFIED`, `FAILED`, `PARTIAL`, `UNVERIFIED`, `NOT_REPRODUCED`, `FLAKY`, `ENVIRONMENT_FAILURE`, `ORACLE_FAILURE`, `CANCELLED`, `INDETERMINATE`, and `ROLLED_BACK`.

Exit code/tool output/model assertion alone cannot create `VERIFIED`.

## Q19 — Can the verifier disagree with the planner/model/repair generator?
Consequential capabilities require structurally independent postconditions where practical.

**FAIL if:** the same model's judgment is the only success oracle.

## Q20 — Is evidence safe, bounded, provenance-rich, and concurrency-safe?
Before persistence: redact secrets, bound output, digest-address large artifacts, record Action/execution/verifier/checkpoint/provenance IDs, label trust source, and use transactional/concurrency-safe writes.

Known secret persistence rate must be zero in adversarial tests.

## Q21 — Is external data egress deterministic and minimal?
External models/services receive only request-relevant policy-permitted data. Credentials, keys, unrelated files, and sensitive evidence are excluded deterministically.

**FAIL if:** a model can broaden its own context or egress permissions.

---

# G — Failure reproduction and causal diagnosis

## Q22 — Does every detected/reported failure enter reproduce → classify → minimize before repair whenever technically possible?
Capture commit/worktree, dependencies, environment, configuration, services, inputs, relevant external conditions, commands, and evidence.

**FAIL if:** the system patches an unverified symptom when reproduction is achievable.

## Q23 — Can it distinguish deterministic defects from flaky/environmental failures and minimize the failing case?
Use controlled reruns plus appropriate delta debugging/input reduction/change isolation/Git bisect. Preserve the smallest practical reproducer as evidence.

## Q24 — Can multiple independent localization methods compete to identify cause rather than symptom?
Use applicable evidence from stack traces, logs/traces/metrics, coverage, spectrum-based localization, Git history, call/dependency graphs, static/data/taint flow, invariants, profiling, configuration/environment differences, and minimized reproducers.

Maintain competing causal hypotheses until evidence eliminates them.

**FAIL if:** one model guess is treated as root cause.

---

# H — Detector portfolio: never trust one test family

## Q25 — Does the assurance engine dynamically select complementary detectors with known blind spots?
Use applicable combinations of:
- existing/generated regression tests;
- types/static analysis;
- data/taint flow;
- property/state-machine testing;
- mutation testing;
- coverage-guided fuzzing;
- sanitizers/runtime fault detectors;
- metamorphic/differential testing;
- API/contracts/database invariants;
- browser/visual/accessibility/performance testing;
- dependency/supply-chain checks;
- authorized DAST/security testing;
- chaos/fault injection;
- formal/model checking;
- runtime telemetry.

**FAIL if:** one green suite is treated as general system health.

## Q26 — Can the system challenge its own test oracles?
Detect weak/missing coverage, stale snapshots, incorrect expectations, tests that encode the bug, flaky assertions, overfitted fixtures, and contradictory contracts. Use mutation/fault injection where value justifies cost.

`ORACLE_FAILURE` must be a real outcome.

---

# I — Autonomous repair and adversarial proof

## Q27 — Can the repair engine generate/rank competing fixes without defaulting to code changes?
Candidate repairs may target code, config, dependency, data, environment, infrastructure, test, contract, or architecture.

Rank by causal fit, minimal change surface, reversibility, compatibility, security, testability, and proof strength.

Search proven upstream fixes/recipes before inventing custom changes.

## Q28 — Does it reject anti-fixes?
Reject candidates that merely hide failure by weakening tests/validation/security, swallowing errors, increasing arbitrary timeouts, returning fake success, disabling functionality, or pinning insecure obsolete dependencies without proof.

## Q29 — Are repair candidates isolated, attacked, and independently verified before promotion?
Minimum proof ladder when applicable:

```text
reproduce before
→ isolated candidate
→ original reproducer passes
→ regression test/property added
→ previous good behavior still passes
→ relevant full suite
→ static/type/security checks
→ domain-specific assurance
→ fuzz/mutation/adversarial challenge
→ independent verifier
→ commit/promote
```

If proof fails, rollback automatically and keep the failure evidence.

---

# J — Autonomous attack-testing and system maintenance

## Q30 — Can the system automatically expose weaknesses in systems it is explicitly authorized to test while remaining bounded?
Technology/system discovery selects applicable SAST, DAST, fuzzing, property testing, dependency/supply-chain checks, auth/authz negative tests, network inspection, chaos/fault injection, and other bounded attack-test techniques.

**PASS requires:** exact target scope, containment, resource/rate limits, evidence, no stealth/persistence/credential theft, and the same authority broker as all other Actions.

## Q31 — Can updates and maintenance be automatic at low consequence and strongly verified?
Desired pipeline:

```text
discover candidate
→ provenance/authentication
→ dependency/compatibility analysis
→ checkpoint/generation
→ isolated update
→ build/test/security/assurance suite
→ VERIFIED?
   yes → promote atomically
   no  → rollback automatically + retain evidence
```

Low-risk verified updates should not require routine permission. Privileged/system-wide/root-of-trust/production updates remain consequential.

The same principle applies to Terminal Command itself: authenticated provenance, prepare, re-verification immediately before install, final-path health test, atomic activation, and rollback.

---

# K — Daily usefulness, learning, and final proof

## Q32 — Does the finished system maximize useful autonomy without becoming slow, fragile, or bloated?
It must preserve instant everyday value—shell, natural-language command translation, find/search, files, launch/open, projects/resume, system doctor, Git, process/disk/network inspection, archives, monitoring/jobs, checkpoints, updates—while lazily activating heavy assurance machinery only when required.

Every **verified** solved defect should attempt to leave reusable knowledge where valuable:
- failure fingerprint;
- minimal reproducer;
- root-cause record;
- regression test/property;
- static/security rule;
- repair recipe;
- environment fingerprint;
- verification recipe.

Learned procedures remain versioned and must re-prove themselves before becoming automatic.

Final release evidence must separately measure:
- terminal/session reliability;
- deterministic-plan coverage;
- AI-off useful task success;
- reproduction/minimization rate;
- root-cause localization quality;
- verified repair rate;
- false-fix/defect-escape rate;
- attack-test weakness discovery;
- rollback success;
- unsafe-action prevention;
- permission-request rate by autonomy tier;
- secret/egress violations;
- state concurrency/crash recovery;
- update success/rollback;
- frontend/backend/security/system coverage;
- model use/escalation rate;
- latency/CPU/RAM/startup overhead;
- real Windows/WSL/container behavior.

Do not collapse these into one flattering composite score.

**PASS only when:** additional architecture cannot obviously increase verified autonomy/capability without disproportionate permanent complexity, and removing a component would measurably reduce capability, safety, proof, recovery, or daily usefulness.

---

# Q∞ — What can still get through?

> **What defect, unsafe action, environmental change, attack condition, false repair, stale assumption, or evidence failure could still pass every detector and safeguard we have—and what fundamentally different evidence source would expose it?**

Every escaped defect or failed repair must trigger this loop:

```text
failure escaped
→ determine why existing layers missed it
→ repair the failure
→ add the smallest reusable detector/invariant/recipe that would have caught the class
→ attack the new detector
→ retain it only if capability gain exceeds complexity
```

Q∞ is never retired.

## Final architectural law

> **Use the smallest architecture capable of the largest verified autonomy: the system understands known state deterministically, plans known work deterministically, executes only exact authorized Actions, contains side effects, reproduces and attacks failures, repairs through evidence, automatically rolls back failed changes, verifies independently, learns reusable proven procedures, and invokes AI only when deterministic knowledge genuinely ends.**

Run this review before locking architecture, after meaningful prototypes reveal new facts, before admitting anything into core, and before production release.