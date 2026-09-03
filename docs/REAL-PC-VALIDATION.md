# Terminal Command V1 — Real-PC Qualification

Hosted CI is necessary but is not release proof. Run this matrix on the target Windows 11 Home + WSL2 machine before claiming local/product completion.

## Gate A — platform and foundation

1. Confirm the pinned .NET SDK installs and `dotnet restore/build/test Terminal.slnx` succeeds from a clean checkout.
2. Run `.\tools\v1-wsl-smoke.ps1 -Distro Ubuntu` and require correlated Hello/Health plus actual WSL transport success.
3. Confirm WSL child termination leaves no orphan Linux agent/process tree.
4. Exercise Windows Job Object timeout, cancellation, descendant termination, bounded output, cwd/environment, and accounting tests on the actual host.
5. Confirm standard-user operation; no persistent elevation is required.

## Gate B — real terminal semantics

Required when implemented:

1. Exactly one Terminal window/session opens through the final launcher.
2. Persistent cwd and environment behave correctly across commands.
3. Windows ConPTY foreground applications are interactive; Ctrl-C, resize, exit, and child cleanup work.
4. WSL PTY foreground applications are interactive with the same lifecycle guarantees.
5. Background jobs cannot escape process ownership or corrupt foreground session state.
6. Closing/crashing Terminal reconciles owned children and incomplete transaction state honestly.

## Gate C — authority and mutation

1. Read-only T0 work executes automatically only within declared scope.
2. A T2 mutation requires a proven checkpoint/recovery method and independent verifier before auto-execution.
3. T4 privileged/system-wide/remote work requires exact explicit approval.
4. Changed Action hash, stale target identity, reused approval ticket, widened scope, symlink/reparse target swap, and protocol mismatch all fail closed.
5. Absolute deny rules cannot be bypassed by approval metadata or model confidence.
6. Crash/restart during each consequential transaction phase reconciles to a truthful terminal state.

## Gate D — containment and hostile inputs

Required as each boundary is implemented:

1. Verify the reported containment class matches reality: Job Object, process group, cgroup, container/sandbox, or VM-class isolation.
2. Attempt child/grandchild process escape, resource exhaustion, output flooding, and cancellation races.
3. Suspicious supported documents must produce only the sanitized derivative; no automatic host export of the original occurs.
4. Unknown hostile binaries execute only in the stronger configured disposable boundary when policy requires it.
5. Verify no unintended host mounts, credentials, environment secrets, clipboard, device, or network paths cross the quarantine boundary.

## Gate E — evidence/privacy

1. Feed diagnostic/scanner output containing synthetic credentials/tokens and confirm durable evidence is redacted.
2. Confirm malformed/oversized evidence is bounded or rejected without destabilizing Terminal.
3. VPN/Tor workflows, when configured, must pass explicit DNS/IP/egress leak tests before being labeled private.
4. Tool/model output cannot alter policy, scope, or authority merely by containing instruction-like text.

## Gate F — engineering and assurance

When implemented, run seeded defects across representative repositories and require:

1. project/build/test/lint/type discovery;
2. failure reproduction and minimization;
3. trace/evidence collection;
4. causal localization;
5. competing repair candidates;
6. isolated candidate execution;
7. independent regression/adversarial verification;
8. promotion only for verified repairs;
9. rollback after a deliberately bad candidate;
10. security scanners/fuzzers remain inside explicit authorized target scope.

## Gate G — updates, settings, jobs, AI

1. Desired-state/settings changes are independently verified and recoverable where auto-applied.
2. Updates reject stale/rollback/freeze-invalid metadata, stage separately, health-test, atomically promote, and restore the previous healthy release on failure.
3. Scheduled jobs execute through the normal Action/policy/evidence path, not an alternate hidden authority.
4. Disable every model and prove normal shell, discovery, known planning, engineering, assurance, maintenance, evidence, and recovery remain materially useful.
5. Enable model escalation and measure whether it improves verified novel-task completion without changing authority.

## Release record

For every qualification run record:

- commit SHA;
- Windows build/version;
- WSL version and distro/kernel;
- CPU/RAM/GPU/storage relevant to resource limits;
- antivirus/security software that can affect process behavior;
- optional adapter versions;
- pass/fail/unknown for every applicable gate;
- raw evidence location with secrets removed;
- unresolved environment-specific defects.

**Unknown is not pass.** A release claim is blocked by any failed applicable hard gate or by missing evidence for a capability being claimed.