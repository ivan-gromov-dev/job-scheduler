# Job Scheduler

A lightweight, reliable job queue and scheduler for .NET.

The worker supports in-memory and durable PostgreSQL storage. The current goal is
operations and observability. See
[ROADMAP.md](ROADMAP.md) for scope and milestones.
Completed work is recorded in [CHANGELOG.MD](CHANGELOG.MD).

## Repository layout

- `src/JobScheduler.Core` — domain model and queue/scheduling abstractions.
- `src/JobScheduler.Worker` — generic-host worker process.
- `src/JobScheduler.PostgreSql` — durable multi-worker storage and migrations.
- `tests/JobScheduler.Core.Tests` — fast unit tests for core behavior.

## Prerequisites

- .NET SDK 10.0.302 or a compatible 10.0 patch release.

## Build and test

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

## Agentic development workflow

Repository instructions live in `AGENTS.md`; reusable workflows live under
`.agents/skills`. Invoke `$implement`, `$review`, or `$release-docs` in Codex for the
corresponding task. Local and CI quality gates share one command:

```powershell
dotnet run --project tools/JobScheduler.Harness -- implement
```

The cross-platform .NET harness verifies formatting, performs a Release build, runs all
discovered unit and integration tests, enforces at least 70% line coverage, and audits
NuGet packages.

PostgreSQL integration tests use `JOB_SCHEDULER_POSTGRES_TEST_CONNECTION_STRING` and
require a disposable database because the suite resets its scheduler tables.
The PostgreSQL provider uses EF Core for schema migrations and ordinary CRUD, with
focused Npgsql SQL for atomic `SKIP LOCKED` claims and lease-token transitions.

## Current guarantees

The in-process worker provides **at-least-once execution**. Claiming moves a due
pending job to `Processing` and assigns a time-limited lease. Successful dispatch
moves it to `Succeeded`. Transient failures and timeouts are retried with configurable
exponential backoff and jitter; permanent failures, cancellations, and exhausted
retries move to `DeadLettered`. Dead letters can be inspected, replayed, and purged
according to retention rules. A pending job may instead be moved to `Canceled`.
Long-running handlers renew their lease. If a process stops after claiming but before
recording an outcome, the expired lease makes the job claimable again and increments
its attempt. Handlers must therefore be idempotent.

Delayed jobs remain `Pending` until their UTC `ScheduledAt` value is reached. The
store and worker receive a `TimeProvider`, so clock-dependent behavior can be tested
deterministically. Graceful shutdown stops new claims and lets the host cancel active
handlers; canceled executions retain their lease for later recovery.

Recurring schedules use standard five-field cron syntax (`minute hour day-of-month
month day-of-week`) and an explicit `TimeZoneInfo` identifier. Occurrences are found
on the UTC timeline: nonexistent local times during a spring-forward transition are
skipped, while both instances of a repeated fall-back local time run. Day-of-month
and day-of-week use cron OR semantics when both are restricted. A missed occurrence
can be skipped, coalesced into one job at materialization time, or caught up in order;
the per-poll catch-up limit prevents an unbounded burst without discarding backlog.

## In-process usage

Register the worker and each typed handler with dependency injection:

```csharp
builder.Services.AddJobHandler<SendEmail, SendEmailHandler>();
builder.Services.AddJobWorker(options => options.MaxConcurrency = 4);
builder.Services.AddScheduleMaterializer();
```

Resolve `IJobClient` to call `EnqueueAsync` for immediate work or `ScheduleAsync` with
a UTC `DateTimeOffset` for delayed work. Pass `JobEnqueueOptions.DeduplicationKey` to
coalesce active or already successful work for the same job type and application key.
Resolve `IScheduleClient` for one-off or recurring typed jobs, and `IScheduleStore` to
inspect, pause, resume, update, or delete schedules. PostgreSQL materialization locks
due schedule rows and writes jobs plus the next occurrence in one transaction, so
multiple materializers can safely share the database.
Throw `JobExecutionException` with a permanent or cancellation classification when a
failure must not be retried. The public API is not yet declared stable.
