# Changelog

All completed features and meaningful project changes are recorded here. Planned work
remains in [ROADMAP.md](ROADMAP.md) until it is completed.

## Unreleased

### Added

- Durable job contracts with explicit stable type names, payload versions, aliases,
  sequential upcasters, and a provider-neutral configurable payload serializer.
- Schedule-level queue, priority, deduplication, and correlation options propagated to
  every materialized job.
- Worker identity and durable per-attempt history covering claims, lease renewals,
  duration, outcome, failure classification, and retry timing.

- Operational job metadata for queues, priorities, and correlation identifiers;
  bounded queues provide explicit backpressure and queue-aware claiming supports
  per-queue worker concurrency.
- Structured execution logs plus OpenTelemetry-compatible activities and metrics for
  queue lag, throughput, duration, retries, and dead letters.
- Worker readiness health checks and graceful draining that stops claims while
  allowing active handlers to finish.
- Provider-neutral `IJobAdministration` inspection, filtering, cancellation, and
  dead-letter replay API, implemented by both in-memory and PostgreSQL stores.

- Durable one-off and recurring schedules with five-field cron expressions, explicit
  time zones, deterministic daylight-saving behavior, and skip, coalesce, or bounded
  catch-up misfire policies.
- Schedule pause, resume, update, and delete operations plus a hosted materializer;
  PostgreSQL row locking and occurrence deduplication prevent duplicate jobs across
  scheduler instances.

- Durable PostgreSQL storage with serialized versioned migrations, indexed polling,
  atomic multi-worker claims, lease recovery, and guarded lifecycle updates.
- PostgreSQL integration coverage for migration and claim contention, crash recovery,
  stale lease rejection, restart, retry, dead-letter, and replay behavior.
- Hybrid PostgreSQL persistence: EF Core owns the model, migrations, and ordinary
  lifecycle CRUD while focused Npgsql commands retain atomic claim and lease paths.

- In-memory Milestone 1 API for enqueueing, scheduling, leasing, completing, failing,
  and canceling jobs with concurrency-safe lifecycle transitions.
- Typed JSON job handlers, dependency-injection registration, and an in-process worker
  with bounded concurrency, graceful shutdown, delayed execution, and lease recovery.
- Deterministic clock-based unit tests and an end-to-end worker integration test.
- Repository agentic workflow with concise `AGENTS.md` guidance and repo-scoped
  `implement`, `review`, and `release-docs` skills.
- Cross-platform .NET harness shared by local development and CI for formatting,
  Release builds, unit and integration tests, a 70% line-coverage gate, and dependency
  auditing.
- .NET 10 solution with separate core library, worker host, and xUnit test project.
- Initial job model with pending, processing, succeeded, failed, and dead-lettered
  lifecycle states.
- Unit tests for immediate job creation and job type validation.
- GitHub Actions CI for dependency restore, formatting verification, Release builds,
  tests, and dependency vulnerability auditing.
- Repository-wide nullable reference types, recommended analyzers, warnings as errors,
  and NuGet auditing.
- Pinned .NET SDK configuration with compatible patch roll-forward behavior.
