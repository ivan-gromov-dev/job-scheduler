# Roadmap

This roadmap favors correctness and explicit delivery semantics over feature count.
The target baseline is **at-least-once execution** with idempotent handlers.

## Guiding decisions

- Keep the domain and public contracts independent from storage and hosting.
- Use UTC timestamps through `DateTimeOffset` and inject time in runtime components.
- Claim jobs with a time-limited lease so abandoned work can be recovered.
- Treat retries, cancellation, deduplication, and observability as core behavior.
- Start with one process, but avoid designs that prevent multiple workers later.

## Milestone 1 — In-memory MVP

- [x] Define enqueue, schedule, claim, complete, fail, and cancel contracts.
- [x] Implement a concurrency-safe in-memory store.
- [x] Add typed job handlers and dependency-injection registration.
- [x] Implement a worker loop with bounded concurrency and graceful shutdown.
- [x] Support delayed jobs and deterministic tests through `TimeProvider`.
- [x] Document lifecycle transitions and at-least-once semantics.

Exit criterion: an application can enqueue immediate or delayed jobs and process them
reliably within one process, with complete unit and integration coverage.

## Milestone 2 — Failure handling

- [x] Add configurable retry policies with exponential backoff and jitter.
- [x] Distinguish transient, permanent, timeout, and cancellation failures.
- [x] Add execution timeouts and lease renewal for long-running handlers.
- [x] Add dead-letter storage, inspection, replay, and retention rules.
- [x] Define idempotency and optional deduplication keys.

Exit criterion: failed and abandoned jobs have deterministic, testable outcomes.

## Milestone 3 — Durable PostgreSQL storage

- [x] Design versioned schema and migrations.
- [x] Implement atomic multi-worker claiming with leases.
- [x] Add optimistic concurrency and recovery of expired leases.
- [x] Add indexes and polling behavior for immediate and scheduled workloads.
- [x] Run crash, restart, contention, and migration integration tests.

Exit criterion: multiple worker processes can safely share a durable queue without
losing accepted jobs.

## Milestone 4 — Scheduling

- [x] Add one-off scheduling and recurring schedules.
- [x] Define cron syntax, time-zone handling, and daylight-saving behavior.
- [x] Define misfire policy: skip, coalesce, or catch up.
- [x] Prevent duplicate materialization across scheduler instances.
- [x] Add pause, resume, update, and delete operations for schedules.

Exit criterion: recurring jobs behave predictably across restarts and clock changes.

## Milestone 5 — Operations and observability

- [x] Add structured logs with job, attempt, queue, and correlation identifiers.
- [x] Add OpenTelemetry traces and metrics for latency, throughput, retries, and lag.
- [x] Add health/readiness checks and graceful draining.
- [x] Add queue limits, backpressure, priorities, and per-queue concurrency controls.
- [x] Provide an administrative API/CLI for inspection, cancellation, and replay.

Exit criterion: operators can diagnose behavior and safely control a running system.

## Milestone 6 — Execution correctness (current)

- [ ] Treat lost leases as an explicit execution outcome and do not report stale
  complete, retry, or dead-letter transitions as successful.
- [ ] Cancel local execution when lease renewal fails and prevent further lifecycle
  transitions from the former owner.
- [ ] Define timeout behavior for handlers that do not cooperate with cancellation and
  prevent a retry from overlapping the timed-out invocation within the same process.
- [ ] Make draining configurable and coordinate worker shutdown with schedule
  materialization and active handler completion.
- [ ] Move dead-letter retention out of claim loops into a periodic, single-owner,
  batched maintenance service.

Exit criterion: every invocation has an authoritative lease-aware outcome, and
timeouts, maintenance, and shutdown cannot silently create conflicting execution.

## Milestone 7 — Durable job and schedule contracts

- [ ] Add stable explicit job type names independent of CLR namespaces and type
  renames.
- [ ] Version serialized payloads and support aliases or upcasters for jobs persisted
  by older application versions.
- [ ] Make serializer behavior configurable without coupling core contracts to a
  storage provider.
- [ ] Carry queue, priority, deduplication, and correlation options through one-off and
  recurring schedules.
- [ ] Record worker identity and durable attempt history, including claim, renewal,
  duration, outcome, failure, and retry timing.

Exit criterion: persisted jobs and schedules remain executable and diagnosable across
application upgrades and worker instances.

## Milestone 8 — Operational control

- [ ] Add cursor-based job and schedule listing with filters for time, type, status,
  queue, and correlation identifier.
- [ ] Add bulk cancellation and replay plus manual triggering of schedules and
  inspection of their next occurrence and materialization history.
- [ ] Add storage and schema readiness checks, schedule-materializer health, and
  propagation of fatal background-service failures.
- [ ] Support an explicit schema-validation mode that refuses to start against a
  missing or incompatible database without applying migrations.
- [ ] Expose administrative draining and report drain progress and active work.

Exit criterion: operators can determine whether the complete scheduler is ready,
inspect durable work, and control it without direct database access.

## Milestone 9 — Packaging and release

- [ ] Stabilize and document the public API.
- [ ] Add compatibility, performance, and soak-test suites.
- [ ] Publish versioned NuGet packages with symbols and source links.
- [ ] Add upgrade guides, examples, and a support/versioning policy.
- [ ] Perform a security and threat-model review.

Exit criterion: a reproducible release is ready for use outside this repository.

## Explicitly deferred

- Exactly-once execution claims; external side effects make this misleading without
  application-level idempotency or transactional integration.
- A web dashboard before the administrative contracts and telemetry are stable.
- Additional storage providers before the PostgreSQL contract is proven.
- Distributed workflow/DAG orchestration; this project begins as a job queue and
  scheduler, not a general workflow engine.
