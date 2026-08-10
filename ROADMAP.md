# Roadmap

This roadmap favors correctness and explicit delivery semantics over feature count.
The target baseline is **at-least-once execution** with idempotent handlers.

## Guiding decisions

- Keep the domain and public contracts independent from storage and hosting.
- Use UTC timestamps through `DateTimeOffset` and inject time in runtime components.
- Claim jobs with a time-limited lease so abandoned work can be recovered.
- Treat retries, cancellation, deduplication, and observability as core behavior.
- Start with one process, but avoid designs that prevent multiple workers later.

## Milestone 1 — In-memory MVP (current)

- [ ] Define enqueue, schedule, claim, complete, fail, and cancel contracts.
- [ ] Implement a concurrency-safe in-memory store.
- [ ] Add typed job handlers and dependency-injection registration.
- [ ] Implement a worker loop with bounded concurrency and graceful shutdown.
- [ ] Support delayed jobs and deterministic tests through `TimeProvider`.
- [ ] Document lifecycle transitions and at-least-once semantics.

Exit criterion: an application can enqueue immediate or delayed jobs and process them
reliably within one process, with complete unit and integration coverage.

## Milestone 2 — Failure handling

- [ ] Add configurable retry policies with exponential backoff and jitter.
- [ ] Distinguish transient, permanent, timeout, and cancellation failures.
- [ ] Add execution timeouts and lease renewal for long-running handlers.
- [ ] Add dead-letter storage, inspection, replay, and retention rules.
- [ ] Define idempotency and optional deduplication keys.

Exit criterion: failed and abandoned jobs have deterministic, testable outcomes.

## Milestone 3 — Durable PostgreSQL storage

- [ ] Design versioned schema and migrations.
- [ ] Implement atomic multi-worker claiming with leases.
- [ ] Add optimistic concurrency and recovery of expired leases.
- [ ] Add indexes and polling behavior for immediate and scheduled workloads.
- [ ] Run crash, restart, contention, and migration integration tests.

Exit criterion: multiple worker processes can safely share a durable queue without
losing accepted jobs.

## Milestone 4 — Scheduling

- [ ] Add one-off scheduling and recurring schedules.
- [ ] Define cron syntax, time-zone handling, and daylight-saving behavior.
- [ ] Define misfire policy: skip, coalesce, or catch up.
- [ ] Prevent duplicate materialization across scheduler instances.
- [ ] Add pause, resume, update, and delete operations for schedules.

Exit criterion: recurring jobs behave predictably across restarts and clock changes.

## Milestone 5 — Operations and observability

- [ ] Add structured logs with job, attempt, queue, and correlation identifiers.
- [ ] Add OpenTelemetry traces and metrics for latency, throughput, retries, and lag.
- [ ] Add health/readiness checks and graceful draining.
- [ ] Add queue limits, backpressure, priorities, and per-queue concurrency controls.
- [ ] Provide an administrative API/CLI for inspection, cancellation, and replay.

Exit criterion: operators can diagnose behavior and safely control a running system.

## Milestone 6 — Packaging and release

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
