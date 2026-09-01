# Terminal V3 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the smallest production-grade V3 foundation that can prove immutable Action identity, exact-action authorization, deterministic policy, durable transaction states, versioned cross-boundary messages, and CI on Windows/Ubuntu before any autonomous execution is added.

**Architecture:** Greenfield .NET 10 LTS solution. `Terminal.Core` owns deterministic contracts and authority logic. `Terminal.Protocol` owns transport-neutral message envelopes. Platform executors are not trusted to invent or mutate Actions; they will consume already-authorized immutable contracts in later plans.

**Tech Stack:** .NET 10 LTS / C# 14, xUnit, System.Text.Json, SHA-256, Google.Protobuf only when protobuf wire schemas are introduced, GitHub Actions Windows + Ubuntu.

**Spec:** `docs/superpowers/specs/2026-09-01-terminal-v3-greenfield-architecture.md`

## Global Constraints

- Target `net10.0`.
- Windows 11 Home is the authoritative host target; Ubuntu/WSL2 is the Linux target.
- No model dependency in the foundation.
- No production side effect may bypass the future broker boundary.
- Canonical Action identity must be deterministic across repeated serialization.
- Authorization is bound to exactly one Action hash and is non-reusable.
- Unknown or unverified states must never be promoted to success.
- Keep dependencies minimal; do not add DI frameworks, databases, RPC servers, or logging stacks before a task needs them.
- Existing Python V1/V2 code is not an API compatibility constraint.

---

## File structure for this plan

```text
Terminal.slnx
Directory.Build.props
src/
  Terminal.Core/
    Terminal.Core.csproj
    Actions/Action.cs
    Actions/ActionCanonicalizer.cs
    Actions/ActionHash.cs
    Authority/ApprovalTicket.cs
    Authority/PolicyDecision.cs
    Authority/PolicyEngine.cs
    Transactions/TransactionState.cs
    Transactions/TransactionStateMachine.cs
    Verification/VerificationOutcome.cs
  Terminal.Protocol/
    Terminal.Protocol.csproj
    ProtocolVersion.cs
    FrameHeader.cs
    FrameCodec.cs
tests/
  Terminal.Core.Tests/
    Terminal.Core.Tests.csproj
    Actions/ActionIdentityTests.cs
    Authority/ApprovalTicketTests.cs
    Authority/PolicyEngineTests.cs
    Transactions/TransactionStateMachineTests.cs
    Verification/VerificationOutcomeTests.cs
  Terminal.Protocol.Tests/
    Terminal.Protocol.Tests.csproj
    FrameCodecTests.cs
.github/workflows/v3-foundation.yml
```

---

### Task 1: Greenfield .NET solution and immutable Action identity

**Files:**
- Create: `Directory.Build.props`
- Create: `Terminal.slnx`
- Create: `src/Terminal.Core/Terminal.Core.csproj`
- Create: `tests/Terminal.Core.Tests/Terminal.Core.Tests.csproj`
- Test: `tests/Terminal.Core.Tests/Actions/ActionIdentityTests.cs`
- Create after RED: `src/Terminal.Core/Actions/Action.cs`
- Create after RED: `src/Terminal.Core/Actions/ActionCanonicalizer.cs`
- Create after RED: `src/Terminal.Core/Actions/ActionHash.cs`

**Interfaces:**
- Produces: immutable `TerminalAction`, `ActionBackend`, `MutationClass`, `RecoveryClass`, `ActionCanonicalizer.Canonicalize(TerminalAction)`, and `ActionHash.Compute(TerminalAction)`.

- [ ] **Step 1: Create solution/project metadata and write failing Action identity tests**

Tests must assert:

```csharp
[Fact]
public void Same_material_action_has_same_hash()
{
    var first = Fixtures.ReadOnly("git", ["status"]);
    var second = Fixtures.ReadOnly("git", ["status"]);
    Assert.Equal(ActionHash.Compute(first), ActionHash.Compute(second));
}

[Theory]
[InlineData("arguments")]
[InlineData("workingDirectory")]
[InlineData("backend")]
[InlineData("capability")]
[InlineData("environment")]
[InlineData("scope")]
public void Material_change_changes_hash(string field)
{
    var baseline = Fixtures.ReadOnly("git", ["status"]);
    var changed = Fixtures.Change(baseline, field);
    Assert.NotEqual(ActionHash.Compute(baseline), ActionHash.Compute(changed));
}
```

Canonicalization tests must also prove dictionaries are sorted ordinally and action IDs/timestamps are excluded from semantic identity unless explicitly designated material.

- [ ] **Step 2: Run RED**

Run:

```bash
dotnet test tests/Terminal.Core.Tests/Terminal.Core.Tests.csproj --filter ActionIdentityTests
```

Expected: compilation/test failure because V3 Action types do not exist.

- [ ] **Step 3: Implement minimal immutable Action contract**

Use a sealed record with immutable collections or copied read-only values. Canonicalization writes fields in a fixed versioned order and sorts maps with `StringComparer.Ordinal`. Hash is lower-case SHA-256 hex over UTF-8 canonical bytes.

- [ ] **Step 4: Run GREEN**

Run the same filtered test; expected PASS.

- [ ] **Step 5: Run full Core tests and commit**

```bash
dotnet test tests/Terminal.Core.Tests/Terminal.Core.Tests.csproj
git add Terminal.slnx Directory.Build.props src/Terminal.Core tests/Terminal.Core.Tests
git commit -m "feat(v3): add immutable action identity"
```

---

### Task 2: Single-use exact-action approval tickets

**Files:**
- Test: `tests/Terminal.Core.Tests/Authority/ApprovalTicketTests.cs`
- Create after RED: `src/Terminal.Core/Authority/ApprovalTicket.cs`

**Interfaces:**
- Consumes: `ActionHash.Compute(TerminalAction)`.
- Produces: `ApprovalTicket.Issue(string actionHash, DateTimeOffset now, TimeSpan ttl, ReadOnlySpan<byte> nonce)`, `ApprovalTicket.Validate(string actionHash, DateTimeOffset now)`, and `ApprovalTicket.Consume(...)` returning a new consumed ticket/result without mutable shared state.

- [ ] **Step 1: Write failing tests**

Prove:
- ticket accepts only the bound hash;
- changed hash is rejected;
- expired ticket is rejected;
- consumed ticket is rejected;
- one ticket cannot authorize two executions;
- nonce/ticket ID differ between issues for the same action.

- [ ] **Step 2: Run RED**

```bash
dotnet test tests/Terminal.Core.Tests/Terminal.Core.Tests.csproj --filter ApprovalTicketTests
```

Expected: missing `ApprovalTicket`.

- [ ] **Step 3: Implement minimal ticket**

Ticket fields: `TicketId`, `ActionHash`, `IssuedAt`, `ExpiresAt`, `NonceHash`, `ConsumedAt`. Validation returns a typed result (`Valid`, `WrongAction`, `Expired`, `Consumed`). Never accept user prose or re-route on consume.

- [ ] **Step 4: Run GREEN and full tests**

- [ ] **Step 5: Commit**

```bash
git add src/Terminal.Core/Authority tests/Terminal.Core.Tests/Authority
git commit -m "feat(v3): bind approval to exact action"
```

---

### Task 3: Deterministic policy and autonomy tiers

**Files:**
- Test: `tests/Terminal.Core.Tests/Authority/PolicyEngineTests.cs`
- Create after RED: `src/Terminal.Core/Authority/PolicyDecision.cs`
- Create after RED: `src/Terminal.Core/Authority/PolicyEngine.cs`

**Interfaces:**
- Consumes: immutable `TerminalAction`.
- Produces: `PolicyDecision { AllowAuto, RequireApproval, Deny }` plus reason code and required revalidation flags.

- [ ] **Step 1: Write failing tests**

Minimum cases:
- T0 observation + no dangerous scope => `AllowAuto`;
- T1 ephemeral => `AllowAuto`;
- T2 verified/checkpointable local mutation with declared verifier and recovery => `AllowAuto`;
- T2 missing verifier or recovery => `RequireApproval`;
- privileged/system-wide/remote/root-of-trust => `RequireApproval`;
- catastrophic deny signature => `Deny` regardless of approval intent;
- model confidence/risk fields, if ever present as provenance, cannot change decision.

- [ ] **Step 2: Run RED**
- [ ] **Step 3: Implement the smallest rule engine that passes**
- [ ] **Step 4: Run GREEN + full tests**
- [ ] **Step 5: Commit**

---

### Task 4: Transaction state machine and honest recovery classes

**Files:**
- Test: `tests/Terminal.Core.Tests/Transactions/TransactionStateMachineTests.cs`
- Create after RED: `src/Terminal.Core/Transactions/TransactionState.cs`
- Create after RED: `src/Terminal.Core/Transactions/TransactionStateMachine.cs`

**Interfaces:**
- Produces pure deterministic `CanTransition(from, to)` and `Transition(from, to)`.

- [ ] **Step 1: Write failing transition tests**

Prove legal forward paths and rejection of impossible transitions, including:

```text
Prepared -> Authorized -> Started -> SideEffectObserved -> Verifying -> Committed
Started -> Failed
Started -> Cancelled
Started -> Indeterminate
Failed -> RollingBack -> RolledBack
Indeterminate -> RollingBack -> RolledBack
```

`Committed -> Started`, `RolledBack -> Committed`, and direct `Prepared -> Committed` must fail.

- [ ] **Step 2: Run RED**
- [ ] **Step 3: Implement pure state machine**
- [ ] **Step 4: Run GREEN + full tests**
- [ ] **Step 5: Commit**

SQLite durability is intentionally deferred to the state/journal sub-plan; this task first proves transition semantics without storage coupling.

---

### Task 5: Verification outcome truth model

**Files:**
- Test: `tests/Terminal.Core.Tests/Verification/VerificationOutcomeTests.cs`
- Create after RED: `src/Terminal.Core/Verification/VerificationOutcome.cs`

**Interfaces:**
- Produces closed outcome enum/value object and `IsSuccess` semantics where only `Verified` is full success.

- [ ] **Step 1: Write failing tests**

Prove `Verified` is success and `Failed`, `Partial`, `Unverified`, `NotReproduced`, `Flaky`, `EnvironmentFailure`, `OracleFailure`, `Cancelled`, `Indeterminate`, and `RolledBack` are not full success.

- [ ] **Step 2: Run RED**
- [ ] **Step 3: Implement minimal outcome type**
- [ ] **Step 4: Run GREEN + full tests**
- [ ] **Step 5: Commit**

---

### Task 6: Versioned transport-neutral frame codec

**Files:**
- Create: `src/Terminal.Protocol/Terminal.Protocol.csproj`
- Create: `tests/Terminal.Protocol.Tests/Terminal.Protocol.Tests.csproj`
- Test: `tests/Terminal.Protocol.Tests/FrameCodecTests.cs`
- Create after RED: `src/Terminal.Protocol/ProtocolVersion.cs`
- Create after RED: `src/Terminal.Protocol/FrameHeader.cs`
- Create after RED: `src/Terminal.Protocol/FrameCodec.cs`

**Interfaces:**
- Produces deterministic length-delimited framing for opaque payload bytes: magic, protocol major/minor, message type, request ID, payload length.
- Does **not** add TCP, gRPC, or WSL process code yet.

- [ ] **Step 1: Write failing tests**

Prove round trip, truncated frame rejection, oversized frame rejection, unknown incompatible major version rejection, and bounded maximum payload.

- [ ] **Step 2: Run RED**
- [ ] **Step 3: Implement frame codec using `System.Buffers.Binary` and bounded streams/spans**
- [ ] **Step 4: Run GREEN + solution tests**
- [ ] **Step 5: Commit**

---

### Task 7: Foundation CI matrix

**Files:**
- Create: `.github/workflows/v3-foundation.yml`

**Interfaces:**
- Consumes solution/projects from Tasks 1-6.
- Produces Windows + Ubuntu proof for restore/build/test.

- [ ] **Step 1: Create CI workflow**

Use `actions/checkout`, `actions/setup-dotnet` with `.NET 10.x`, then:

```bash
dotnet restore Terminal.slnx
dotnet build Terminal.slnx --configuration Release --no-restore
dotnet test Terminal.slnx --configuration Release --no-build --logger "trx;LogFileName=v3-foundation.trx"
```

Run on `windows-latest` and `ubuntu-latest` for pushes/PRs affecting V3 paths.

- [ ] **Step 2: Push and inspect CI**

Expected: both OS jobs green.

- [ ] **Step 3: If CI exposes platform assumptions, add a failing regression test before fixing them**

- [ ] **Step 4: Commit CI corrections separately**

---

## Self-review

### Spec coverage
This plan intentionally covers only the dependency root required by every later subsystem: immutable Action identity, exact authorization, deterministic policy, transaction semantics, truthful verification outcome, framing, and cross-platform CI. WSL transport/process supervision, SQLite journal, deterministic planning, system graph, privacy gate, assurance engine, updater, and HDCS each require their own follow-on plan because they are independently reviewable subsystems.

### Placeholder scan
No implementation task uses `TBD`, `TODO`, or unspecified error handling. Deferred subsystems are explicitly outside this plan rather than placeholders inside a task.

### Type consistency
All later tasks consume `TerminalAction` and its canonical SHA-256 Action hash. Approval never accepts raw user text. Policy consumes the same immutable Action. Transactions and verification remain storage/transport independent in this foundation.