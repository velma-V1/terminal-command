# Terminal V3 Durable Execution Layer Implementation Plan

**Goal:** Add the smallest crash-safe state/journal and execution-broker substrate required before Terminal may execute consequential Windows or WSL Actions.

**Architecture decision:** Windows remains authority. SQLite is the authoritative local operational journal using WAL + synchronous FULL for consequential state. Windows process trees use Job Objects as the strong cancellation boundary. Linux execution is owned by the Linux agent and uses cgroup v2 when available, with a process-group fallback. WSL control uses the existing bounded framed protocol over one persistent child process; TCP/gRPC is not foundational.

**Research basis:** SQLite documents WAL + FULL as ACID/durable across power loss; SQLite allows one concurrent writer, so state transitions use short write transactions and explicit busy handling. Windows Job Objects manage/terminate process trees as a unit. Windows process creation must use `CREATE_SUSPENDED` so the child can be assigned to Terminal's Job Object before any child code can spawn descendants. `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` restricts inherited handles to the exact redirected standard handles. Microsoft explicitly warns that inherited handles can leak files, sockets, and tokens. Linux cgroup v2 `cgroup.kill` kills the entire descendant tree and handles concurrent forks/migrations.

## Task 1 — SQLite operational store

Create `Terminal.State` project using `Microsoft.Data.Sqlite` 10.0.11.

Schema v1 tables:
- `schema_info`
- `actions`
- `approval_tickets`
- `transactions`
- `transaction_events`
- `executions`
- `verification_results`

Connection initialization:
- `PRAGMA foreign_keys=ON`
- `PRAGMA journal_mode=WAL`
- `PRAGMA synchronous=FULL`
- bounded busy timeout

Tests must prove schema migration idempotence, WAL/FULL configuration, transaction/event foreign keys, concurrent readers, serialized writers, and reopening after process-equivalent connection disposal.

## Task 2 — Persistent single-use approval store

Implement SQLite-backed `IApprovalTicketStore`.

Consumption must be one atomic conditional write inside a short write transaction. Two concurrent consumers for one ticket must yield exactly one `Valid` result; all others return `Consumed`/non-authorized. Wrong Action ID/hash and expiry cannot mark consumed.

## Task 3 — Durable transaction journal

Implement `ITransactionJournal` around the already-proven pure state machine.

Every transition appends an immutable event and updates current state in the same database transaction. Illegal state transitions are rejected before persistence. On startup, `ListIncomplete()` returns all nonterminal transactions for reconciliation.

Tests inject reopen/crash-equivalent boundaries after each state and prove current state + event history remain consistent.

## Task 4 — Execution authorization envelope

Add an immutable `ExecutionAuthorization` contract containing:
- exact Action ID/hash
- PolicyDecision reason
- approval ticket ID when required
- target-revalidation evidence ID/version
- transaction ID
- issuance timestamp

The future broker accepts this envelope plus the original immutable Action. It re-hashes and checks identity immediately before execution. Any mismatch is rejected without starting a process.

## Task 5 — Broker interface and no-side-effect fake

Create `IExecutionBroker` and `IProcessSupervisor` interfaces in a new `Terminal.Execution` project, plus an in-memory fake used only by tests.

No real OS process launch yet. Tests prove:
- action hash mismatch never reaches supervisor;
- missing/invalid approval for `RequireApproval` never reaches supervisor;
- stale target evidence never reaches supervisor;
- `Deny` never reaches supervisor;
- valid `AllowAuto` envelope reaches supervisor exactly once;
- execution start is journaled before side effects are reported.

## Task 6 — Windows Job Object supervisor

Create `Terminal.Windows` project.

Implement noninteractive Windows process supervision first:
- explicit executable + argv; no implicit shell;
- construct a writable Windows command line with correct argv quoting while passing the executable separately as `lpApplicationName`;
- build an explicit Unicode environment block from the parent environment plus the Action's environment delta;
- redirect bounded stdout/stderr and continuously drain both pipes;
- create child pipe/NUL handles as inheritable but use `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` so **only** the explicit standard handles can cross into the child;
- create the process with `CREATE_SUSPENDED | EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT`;
- create/configure a Windows Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` and job-wide memory limit when the Action declares one;
- assign the still-suspended process to the Job Object **before** `ResumeThread`; never use `Process.Start()` followed by best-effort assignment;
- cancel/timeout -> terminate the Job Object -> wait for process boundary completion -> finish draining pipes;
- query Job Object accounting before closing it so peak memory/CPU accounting can enter execution evidence;
- working directory and environment are passed explicitly;
- all native handles/attribute lists/environment allocations have deterministic cleanup on every failure path.

Tests must prove explicit argv/working-directory/environment behavior, bounded output while still draining, timeout/cancel semantics, child-tree termination, and resource-accounting availability on Windows. Pure command-line quoting tests run on all CI operating systems.

ConPTY interactive hosting is a separate follow-on task after noninteractive lifecycle tests are green.

## Task 7 — Linux lifecycle protocol and agent skeleton

Create `Terminal.LinuxAgent` executable.

Agent starts in `--stdio` protocol mode, performs hello/version handshake, and supports health/cancel messages without executing arbitrary Actions yet.

The Windows transport will later launch it using `wsl.exe -d <distro> -- terminal-linux-agent --stdio`.

## Task 8 — WSL transport supervisor

Create `Terminal.Windows/WslTransport`.

Launch one persistent `wsl.exe` child with redirected binary stdin/stdout. Decode only bounded valid protocol frames. STDERR is treated as diagnostic side-channel, not protocol. Heartbeat/handshake failures mark backend unavailable and fail closed.

No localhost port, NAT dependency, mirrored-network dependency, or service daemon.

## Task 9 — Linux execution groups

For Linux child Actions:
1. prefer dedicated cgroup v2 subtree when writable/delegated;
2. use `cgroup.kill` for strong tree cancellation;
3. otherwise launch in a new session/process group and kill the process group;
4. report containment strength in execution evidence so fallback is never mistaken for equivalent isolation.

## Task 10 — CI/fault matrix

Extend V3 CI with:
- State tests on Windows/Ubuntu
- broker tests
- Windows Job Object tests on Windows
- Linux process-group tests on Ubuntu
- protocol malformed-frame tests
- concurrent approval consume stress
- journal reopen/reconciliation tests

WSL end-to-end remains a required real-Windows integration gate because GitHub-hosted Windows runners cannot be assumed to expose the exact user's WSL2 configuration.

## Admission rules
- Do not add a daemon.
- Do not add gRPC/TCP.
- Do not add a DI framework.
- Do not add an ORM; direct parameterized SQL is smaller and more auditable here.
- Do not add interactive ConPTY until noninteractive lifecycle is proven.
- Do not claim Linux cgroup isolation when only process-group fallback is active.
- Do not permit the executor to recalculate policy or invent Actions.
- Do not accept a Windows child process assignment race; Job membership must be established while the primary thread is suspended.
