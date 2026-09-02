# Q−1, Q0–Q32 + Q∞ Absolute-Value Program Admission Review

This is the **hard architectural admission gate** for Terminal Command.

Terminal Command's target is a **deterministic-first autonomous computer engineering and assurance system** that remains an excellent everyday terminal while being able to inspect, understand, test, attack-test authorized systems, diagnose, repair, update, recover, verify, and maintain software and machines with minimal babysitting. AI is optional reasoning acceleration—not the operating foundation, authority source, or substitute for deterministic structure.

The optimization target is:

> **maximum verified capability and autonomy with the minimum permanent machinery required to achieve it.**

The review must reject both failure modes:

- **underbuilding** — simplicity that removes useful capability, coverage, autonomy, proof, recovery, or everyday value;
- **overbuilding** — permanent machinery that cannot beat a simpler composition on measurable value.

## Non-negotiable laws

- **UNKNOWN IS NOT SUCCESS.** Unknown, conflicting, partial, or insufficient evidence remains explicit.
- **MOSTLY WORKS IS NOT COMPLETE.** Unsupported boundaries must be named and measured.
- **HYPE/HEARSAY IS NOT FALSE UNTIL FALSIFIED.** Treat unconventional claims as active hypotheses. Do not accept them as truth, but do not discard them because they are unpopular, unreviewed, strange, old, abandoned, proprietary, or difficult to believe. Extract the mechanism and attempt to disprove or validate it.
- **“THE MODEL CAN HANDLE IT” IS NOT ARCHITECTURE.** Use deterministic structure or proven tools whenever they can solve the class reliably.
- **EXIT CODE 0 IS NOT PROOF.** Consequential outcomes require independent postconditions.
- **NO ROLLBACK CLAIM WITHOUT A PROVEN RECOVERY METHOD.**
- **NO AUTHORITY BY INTELLIGENCE.** Bigger models never gain broader machine permissions because they reason better.
- **NO CORE ADMISSION BY CONVENIENCE.** Core exists only for cross-cutting invariants or proven system-wide value.
- **NO CUSTOM REIMPLEMENTATION WHEN A BETTER MATURE PRIMITIVE EXISTS.** Own orchestration, authority, evidence, and composition; reuse infrastructure.
- **NO SILENT DEFECT ESCAPE.** If health cannot be proven, return a non-success state.
- **NO SINGLE TOOL GETS TO DEFINE TRUTH.** Important conclusions require independent challenge where practical.
- **NO AUTOMATION WITHOUT A KNOWN FAILURE BOUNDARY.** Automatic work must know what can go wrong and how failure is detected.
- **NO BLOAT WITHOUT CAPABILITY GAIN.** Permanent machinery must remove duplicated risk/complexity or add measurable capability unavailable through a simpler composition.
- **NO CAPABILITY LOSS FOR SIMPLICITY.** Reduce duplication and ceremony—not coverage, proof strength, autonomy, recovery, or useful breadth.
- **NO WINNER BEFORE DISCOVERY.** A tournament among mediocre candidates is still a mediocre architecture.

A failed hard gate means **reject, redesign, isolate outside core, keep experimental, or explicitly downgrade the capability claim**.

---

# Q−1 — Under-Every-Rock Discovery Gate

Q−1 runs **before Q0–Q32, before every major architectural decision, before adding a major dependency, and before declaring any tournament winner**.

The goal is not to gather many links. The goal is to discover mechanisms we did not already know existed and prevent familiarity, popularity, benchmark hype, or model memory from defining the candidate pool.

## Q−1A — Did we search the full evidence universe?

Where relevant, search across:

- official docs, standards, RFCs, specifications, source code, reference implementations, changelogs and design notes;
- peer-reviewed papers, preprints, theses, dissertations, workshop papers, replication studies, negative results, surveys, citation graphs and cited-by chains;
- conference talks, university lectures, seminars, technical presentations, demos and postmortems;
- GitHub/GitLab/other repositories including source, branches, forks, releases, tags, issues, discussions, pull requests, abandoned experiments and benchmarks;
- benchmark suites, leaderboards, evaluator source, failure datasets and reproducibility reports;
- production incident reports, outage analyses, security advisories, CVEs, exploit analyses, bug-bounty reports and root-cause writeups;
- mailing lists, standards discussions, rejected proposals and engineering design reviews;
- mature commercial systems whose behavior/architecture is publicly inspectable;
- niche technical blogs, personal research pages, obscure project sites, archived pages, forums, Hacker News, Reddit and specialist communities;
- patents and proprietary architecture descriptions when they expose a mechanism that can be independently evaluated and legally/safely reproduced;
- historical systems, discontinued projects and abandoned approaches that may contain useful mechanisms later rediscovered under new names;
- adjacent disciplines including operating systems, distributed systems, databases, compilers, build systems, package management, formal methods, control theory, robotics, automated planning, program synthesis, program repair, reliability, observability, security, testing and verification;
- hype, rumors, hearsay, demos and extraordinary claims as **discovery leads**;
- multilingual sources and alternative terminology when the field is broader than English-language results.

Search by mechanism, not just product names. Expand through authors, citations, forks, dependencies, related projects, competing terminology, historical terminology and “people who disagree.”

**FAIL:** stopping because popular search results begin repeating themselves.

## Q−1B — Did we deliberately search for the unfamiliar?

Run separate passes:

1. **Mainstream pass** — strongest accepted/current solutions.
2. **Obscure pass** — niche repos, little-known tools, prototypes, old systems and abandoned ideas.
3. **Adjacent-field pass** — search other disciplines for the same underlying problem under different terminology.
4. **Failure pass** — CVEs, regressions, incidents, negative research, maintenance collapse and benchmark failures.
5. **Novelty pass** — explicitly search for mechanisms absent from the current Terminal Command design.
6. **Disconfirmation pass** — try to prove the current favorite wrong.
7. **Combination pass** — search whether mechanisms dismissed individually become exceptional when composed.
8. **What-did-we-miss pass** — search again after the architecture appears settled.

A late discovery of a new mechanism class reopens the relevant tournament.

## Q−1C — How are hype and hearsay treated?

Use a **falsification-first hypothesis ledger**.

Every material hype/hearsay claim becomes one of:

- `ACTIVE_HYPOTHESIS` — not yet proven or disproven;
- `SUPPORTED` — independent evidence materially supports the mechanism;
- `PROVEN_WITHIN_SCOPE` — reproducible/formal/production evidence establishes the claim within explicit boundaries;
- `REFUTED` — stronger evidence demonstrates the claim/mechanism does not work as stated;
- `MISLEADING` — some mechanism is real but the advertised conclusion is exaggerated or benchmark-specific;
- `UNTESTABLE_CURRENTLY` — insufficient access/evidence to resolve it.

**Important:** lack of proof is not proof of falsehood. `ACTIVE_HYPOTHESIS` and `UNTESTABLE_CURRENTLY` remain in the research ledger when their potential upside is material.

They may be sandbox-tested or used to inspire mechanisms, but consequential production architecture cannot rely on an unproven claim as if it were fact.

Do not reject a hypothesis merely because:

- the source is Reddit/a forum/a small repo;
- no paper exists;
- the author is unknown;
- the project is abandoned;
- the claim sounds implausible;
- competitors dismiss it;
- it failed in one unrelated environment;
- it is not fashionable.

To **refute** it, identify the actual mechanism and produce stronger contradictory evidence, failed reproduction under valid conditions, or a fundamental constraint that defeats the claimed value.

## Q−1D — What is actually true about each serious candidate?

For every serious candidate or mechanism, produce a master truth record:

1. Exact claim.
2. Underlying mechanism.
3. Strongest supporting evidence.
4. Strongest contradictory evidence.
5. Reproducibility/replication quality.
6. Scope where it actually works.
7. Known blind spots and failure modes.
8. Security/authority implications.
9. Runtime/resource cost.
10. Maintenance/dependency cost.
11. Highest-value capability it adds.
12. Smallest useful part that can be extracted.
13. Whether an adapter gives most value without importing the system.
14. Whether a simpler mechanism produces the same value.
15. Complementary mechanisms it combines well with.
16. Conflicting mechanisms it should not be combined with.
17. Why similar systems succeeded, failed or were abandoned.
18. What would falsify our current conclusion.

Separate **the value of the mechanism** from **the quality of the project that currently implements it**.

## Q−1E — When is discovery complete enough to choose?

Do not use a source-count stopping rule. Stop only when:

- all major mechanism classes discovered so far are represented;
- obscure/adjacent/disconfirmation passes stop yielding new high-value classes;
- citation/repository/author trails converge rather than opening unexplored high-value branches;
- strong contradictions are resolved or explicitly marked UNKNOWN;
- every discarded serious candidate has a recorded reason;
- active hype/hearsay with meaningful upside has either been experimentally challenged, retained as unresolved, or scheduled for a bounded experiment;
- the final “what did we miss?” pass produces only dominated variants, lower-value mechanisms or explicit unresolved unknowns.

**AUTOMATIC FAIL:** architecture chosen from one search query, one benchmark, popularity, marketing, one model's memory, one ecosystem, or an unchallenged favorite.

---

# Mandatory Tournament-to-the-Death Rule

Only candidates discovered through Q−1 may reach a final architectural decision.

Every proposed component, architecture, dependency, workflow, model, service or custom subsystem competes against:

1. **Do nothing.**
2. **Best existing mature primitive.**
3. **Thin adapter around that primitive.**
4. **Composition of capabilities already present.**
5. **Best deterministic custom mechanism.**
6. **Best AI-assisted mechanism.**
7. **Strongest competing architecture discovered under Q−1.**
8. **Best hybrid of complementary mechanisms.**

## Tournament rounds

**Round 1 — Within-class elimination:** compare candidates solving the same mechanism.

**Round 2 — Cross-class elimination:** compare fundamentally different ways to achieve the same outcome.

**Round 3 — Hybrid challenge:** determine whether combining survivors materially exceeds each individual candidate.

**Round 4 — Ablation:** remove each component from the proposed winner. If nothing important becomes measurably worse, that component is bloat.

**Round 5 — Adversarial challenge:** search explicitly for where the winner fails and where a losing candidate wins.

**Round 6 — Simplicity challenge:** attempt to reproduce the same capability with less permanent machinery.

**Round 7 — Capability challenge:** attempt to increase capability without disproportionate added machinery.

Do not hide tradeoffs in one flattering scalar score. Compare at least:

```text
verified capability
autonomy gain
defect/weakness coverage
proof strength
recovery strength
daily usefulness
AI-off usefulness
latency/startup/resource cost
security/authority risk
maintenance burden
failure surface
replaceability
integration complexity
evidence quality
```

Reject Pareto-dominated candidates.

**Winner rule:** choose the smallest surviving architecture that preserves the highest verified capability. A more complex design must prove a meaningful gain in capability, autonomy, coverage, proof, recovery or safety. A simpler design must be rejected if it loses those qualities.

No component enters core merely because it defeated weak alternatives. It must survive the strongest credible alternatives found under Q−1.

---

# A — Mission, AI-off baseline, and architectural density

## Q0 — Does Terminal Command justify existing as one operating layer?

PASS only if combining authority, orchestration, evidence and reusable capabilities measurably reduces babysitting while increasing verified completion, defect discovery, recovery and everyday utility versus the best simpler composition.

**Kill condition:** if an existing composition achieves essentially the same verified outcomes with less permanent complexity, do not build/keep the layer.

## Q1 — Is it an excellent assistant with every AI model disabled?

AI-off mode must retain the central value where technically possible:

- real terminal behavior;
- live system/project discovery;
- deterministic planning of known work;
- files/search/open/launch/system operations;
- Git/build/test/lint/type workflows;
- known diagnosis/repair recipes;
- assurance tools and scanners;
- authorized bounded attack-testing;
- monitoring/jobs;
- safe updates;
- checkpoints/rollback;
- evidence and verification.

**FAIL:** AI-off merely launches but loses the system's core usefulness.

## Q2 — Can known work be planned deterministically?

Capabilities should expose machine-readable preconditions, effects/postconditions, dependencies, scope, cost, risk, trust boundary, verifier, recovery and idempotency/retry semantics.

Use graph/HTN/classical planning or another deterministic composition method when it wins Q−1/tournament comparison.

**FAIL:** an LLM is required merely to select the next already-known step.

## Q3 — Does every permanent component pass capability-density and ablation?

Evaluate:

```text
system-wide capability × frequency × verifiability × autonomy gain
───────────────────────────────────────────────────────────────
complexity × risk × maintenance × duplication
```

Then remove the component experimentally. If capability, proof, safety, recovery, or meaningful daily utility does not measurably drop, remove or externalize it.

---

# B — Real terminal and live-machine understanding

## Q4 — Is it a real terminal rather than a command wrapper?

Require persistent session semantics, interactive processes, streaming I/O, cancellation/Ctrl-C, resize, foreground/background process ownership, process-tree cleanup, bounded output and reliable Windows behavior. Prefer ConPTY/PTY/OS primitives over reimplementation.

## Q5 — Can it continuously build an accurate live system graph?

Discover and relate, when applicable:

repositories/worktrees, languages/frameworks, build/test/type/lint systems, dependencies, processes/services, ports, WSL/containers, databases/queues/caches, frontend/backend/API boundaries, CI/CD, configuration, hardware/resources, update state and security exposure.

Every fact has provenance, freshness and invalidation semantics.

## Q6 — Can external changes invalidate assumptions before they become mistakes?

Files, Git, dependencies, processes, DNS, services, environment and remote state can change outside Terminal Command. Require freshness/version checks, target revalidation, plan invalidation and concurrency-safe state updates.

---

# C — Exact Actions, authority, and autonomy

## Q7 — Is every executable operation an immutable canonical Action?

Include every material execution dimension: origin, typed arguments/command, cwd/environment delta, backend, stable target identity where available, filesystem/network/data-egress scope, resources, mutation/recovery class, provenance and expiry.

Canonical serialization produces deterministic `action_hash`.

## Q8 — Is authorization bound to the exact Action and current real-world target?

Any material change invalidates authorization. Revalidate mutable references such as paths/reparse points/symlinks, DNS, redirects and remote identity immediately before use.

**AUTOMATIC FAIL:** route → approval → route again → execute a changed result.

## Q9 — Is there one non-bypassable path to consequential side effects?

Models, planners, workflows, capability builders, views and plugins request Actions; a controlled broker performs consequential filesystem/process/network/package/remote/privileged side effects.

## Q10 — Does autonomy maximize useful action without permission spam?

Minimum policy:

```text
T0 OBSERVE
read/search/inspect/analyze/test
→ automatic

T1 EPHEMERAL / DISPOSABLE
candidate branches, temporary files, sandboxes, disposable services
→ automatic

T2 VERIFIED REVERSIBLE LOCAL MUTATION
known repair/update/config transform + proven checkpoint + verifier + rollback
→ automatic when deterministic prerequisites pass

T3 VERIFIED REVERSIBLE CONTAINMENT
quarantine/stop/block an exact authorized target with proven recovery
→ automatic only under strict deterministic policy

T4 CONSEQUENTIAL
privileged/system-wide/production/remote/security-boundary changes
→ explicit approval

T5 IRREVERSIBLE / UNKNOWN
unbounded destructive/root-of-trust/identity/uncertain external effects
→ approval or deny
```

AI confidence never increases authority.

---

# D — Execution, containment, transaction, and recovery

## Q11 — Does policy choose the least-risk boundary that still completes the task?

Deterministically select native Windows, WSL, disposable container/sandbox or explicitly scoped remote execution. Unknown/untrusted code prefers disposable execution when practical.

## Q12 — Does one process supervisor own execution lifecycle?

It owns interactive/noninteractive execution, streaming I/O, timeout, cancellation, process-tree cleanup, resource/output bounds, backend health, execution IDs and disconnect/recovery behavior.

## Q13 — Is every consequential action transactionally journaled?

At minimum:

```text
PREPARED → AUTHORIZED → STARTED → SIDE_EFFECT_OBSERVED → VERIFYING → COMMITTED
                                      └→ FAILED / CANCELLED / INDETERMINATE
                                              └→ ROLLED_BACK / COMPENSATED
```

Crash recovery must reconcile to an honest state.

## Q14 — Is recovery predeclared, honest and independently verified?

Every mutation is `reversible`, `checkpointable`, `compensatable` or `irreversible`. Claimed recovery is established before automatic execution and verified after use.

---

# E — Truth, evidence, and verification

## Q15 — Are execution success and task success separated?

Legal outcomes include at least:

`VERIFIED`, `FAILED`, `PARTIAL`, `UNVERIFIED`, `NOT_REPRODUCED`, `FLAKY`, `ENVIRONMENT_FAILURE`, `ORACLE_FAILURE`, `CANCELLED`, `INDETERMINATE`, `ROLLED_BACK`.

No exit code, tool output or model assertion alone creates `VERIFIED`.

## Q16 — Can the verifier disagree with the planner/repair generator/model?

Consequential capabilities require structurally independent postconditions where practical. Model-generated fixes cannot self-certify.

## Q17 — Is evidence safe, bounded, provenance-rich and concurrency-safe?

Redact secrets before persistence, bound inline output, digest-address large artifacts, record action/execution/verifier/checkpoint/provenance identities, label trust source and use transactional/concurrency-safe state.

Known secret persistence rate must be zero under adversarial tests.

## Q18 — Is external data egress deterministic and minimal?

External services/models receive only request-relevant policy-permitted data. Models cannot broaden their own context/egress authority.

---

# F — Failure reproduction and causal diagnosis

## Q19 — Does repair begin with reproduce → classify → minimize whenever possible?

Capture commit/worktree, dependencies, environment, configuration, services, inputs, relevant external conditions, commands and evidence. Do not patch an unverified symptom when reproduction is achievable.

## Q20 — Can it distinguish product defects from flaky/environment/oracle failures and minimize the case?

Use controlled reruns plus applicable delta debugging, input reduction, change isolation and Git bisect. Preserve minimal reproducers as evidence.

## Q21 — Do independent localization methods compete before root cause is declared?

Use applicable stack traces, logs/traces/metrics, coverage, spectrum localization, Git history, call/dependency graphs, static/data/taint flow, invariants, profiling, configuration differences and minimized reproducers.

Maintain competing causal hypotheses until evidence eliminates them.

---

# G — Detector portfolio and oracle quality

## Q22 — Does the assurance engine select complementary detectors instead of trusting one green suite?

Use applicable combinations of:

- existing and generated regression tests;
- types/static analysis;
- data/taint flow;
- property/state-machine testing;
- mutation testing;
- coverage-guided fuzzing;
- sanitizers/runtime detectors;
- metamorphic/differential testing;
- API/contracts/database invariants;
- browser/visual/accessibility/performance testing;
- dependency/supply-chain checks;
- authorized DAST/security tests;
- chaos/fault injection;
- formal/model checking;
- runtime telemetry.

Detector selection must account for each method's blind spots and marginal value.

## Q23 — Can the system challenge its own oracles?

Detect weak coverage, stale snapshots, wrong expectations, tests that encode the bug, flaky assertions, overfit fixtures and contradictory contracts. Use mutation/fault injection when its marginal detection value justifies cost.

`ORACLE_FAILURE` is a real outcome.

## Q24 — Does domain coverage remain broad without duplicating engines?

Frontend, backend/data, native/system, security and distributed/concurrent systems each need appropriate proof methods, but Terminal Command should orchestrate mature specialized engines rather than reimplement them unless a custom mechanism wins the tournament.

---

# H — Autonomous repair and adversarial proof

## Q25 — Can the repair engine generate and rank competing fixes across the true repair surface?

A repair may be code, configuration, dependency, data, environment, infrastructure, test, contract or architecture. Search proven upstream fixes/recipes before custom synthesis.

Rank by causal fit, change size, reversibility, compatibility, security, testability, proof strength and maintenance burden.

## Q26 — Does it automatically reject anti-fixes?

Reject changes that merely hide failure: weakened tests/validation/security, swallowed errors, arbitrary timeout inflation, fake success, disabled functionality or insecure obsolete dependency pinning without proof.

## Q27 — Are candidates isolated, attacked and independently verified before promotion?

When applicable:

```text
reproduce before
→ isolated candidate
→ original reproducer passes
→ regression test/property added
→ previous good behavior passes
→ relevant full suite
→ static/type/security checks
→ domain-specific assurance
→ fuzz/mutation/adversarial challenge
→ independent verifier
→ promote
```

Failed proof triggers automatic rollback and retained evidence.

---

# I — Attack-testing, maintenance, and learning

## Q28 — Can it automatically expose weaknesses in systems explicitly authorized for testing while remaining bounded?

Discovery selects applicable SAST, DAST, fuzzing, property tests, dependency/supply-chain checks, auth/authz negatives, network analysis, chaos/fault injection and other bounded attack-test techniques.

Require exact authorized scope, containment, rate/resource limits, evidence and the normal broker. No stealth, persistence or credential theft.

## Q29 — Can low-consequence maintenance and updates be automatic and strongly verified?

```text
discover candidate
→ provenance/authentication
→ compatibility analysis
→ checkpoint/generation
→ isolated update
→ build/test/security/assurance
→ VERIFIED?
   yes → atomic promotion
   no  → automatic rollback + evidence
```

Low-risk verified updates should not cause permission spam. Privileged/system-wide/root-of-trust/production updates remain consequential.

## Q30 — Does every verified failure improve future deterministic capability without uncontrolled self-modification?

Where valuable, retain versioned:

- failure fingerprint;
- minimal reproducer;
- root-cause record;
- regression test/property;
- static/security detector;
- repair recipe;
- environment fingerprint;
- verification recipe.

Learned procedures must re-prove themselves before gaining automatic authority.

---

# J — Intelligence, everyday usefulness, and final proof

## Q31 — Is AI used only where it produces a measurable win over deterministic structure?

Resolution order should favor explicit commands, deterministic rules/plans and known capability composition before model escalation. Tiny models handle ambiguity/argument extraction/context selection; stronger models handle genuinely novel synthesis/research/planning.

All model output remains proposed evidence/action, never authority or proof.

## Q32 — Does the finished system maximize capability while remaining fast, understandable and removable by layer?

Preserve instant everyday value:

shell, natural-language command translation, search/find, files, open/launch, projects/resume, system doctor, Git, process/disk/network inspection, archives, monitoring/jobs, checkpoints and updates.

Heavy assurance engines remain lazy/on-demand.

Final release evidence must report separately—not hide in one composite score:

- terminal/session reliability;
- deterministic-plan coverage;
- AI-off useful-task success;
- failure reproduction/minimization;
- root-cause localization quality;
- verified repair rate;
- false-fix and defect-escape rates;
- authorized attack-test discovery rate;
- rollback/recovery success;
- unsafe-action prevention;
- approval-request rate by autonomy tier;
- secret/egress violations;
- concurrency/crash recovery;
- automatic update success/rollback;
- frontend/backend/security/system coverage;
- model usage/escalation rate;
- startup/latency/CPU/RAM overhead;
- real Windows/WSL/container behavior.

**PASS only when:** no discovered alternative or reasonable hybrid can obviously increase verified capability/autonomy without disproportionate permanent complexity, and removing any core component measurably harms capability, authority, proof, recovery or daily usefulness.

---

# Q∞ — What can still get through?

> **What defect, unsafe action, environmental change, attack condition, false repair, stale assumption, evidence failure, or undiscovered mechanism could still pass every detector and safeguard we have—and what fundamentally different evidence source or design would expose it?**

Every escaped defect, failed repair or important late discovery triggers:

```text
escape/discovery
→ determine why existing search + architecture + detectors missed it
→ reopen Q−1 if it represents a new mechanism class
→ fix the immediate problem
→ add the smallest reusable detector/invariant/recipe that catches the class
→ attack the new mechanism
→ rerun the relevant tournament
→ retain only if capability gain exceeds complexity
```

Q∞ is never retired.

## Final architectural law

> **Search wider than familiarity. Keep unconventional hypotheses alive until falsified. Choose the smallest architecture capable of the largest verified autonomy. The system understands known state deterministically, plans known work deterministically, executes only exact authorized Actions, contains side effects, reproduces and attacks failures, repairs through competing candidates, independently proves outcomes, rolls back failed changes, learns only from verified evidence, and invokes AI only where AI demonstrably adds capability.**
