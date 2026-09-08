# Roadmap

This roadmap favors correctness and explicit delivery semantics over feature count.
The target baseline is **at-least-once execution** with idempotent handlers.

## Post-1.0 Milestone 10 — Contract hardening

- [ ] Define permanent-failure behavior for unknown job types, unsupported payload
      versions, missing upcasters, malformed payloads, and serializer failures.
- [ ] Specify replay semantics for attempts, history, deduplication, queue capacity,
      and optional payload or routing changes.
- [ ] Complete and document the lifecycle transition matrix, including idempotency and
      conflict outcomes for concurrent cancel, replay, claim, and completion operations.
- [ ] Add provider contract tests that run the same lifecycle, scheduling,
      administration, and pagination scenarios against in-memory and PostgreSQL stores.
- [ ] Define validation and storage limits for payloads, identifiers, priorities,
      queue counts, durations, retry policies, and schedule catch-up.
- [ ] Define optimistic-concurrency or explicit last-write-wins behavior for schedule
      updates racing with pause, delete, trigger, and materialization operations.
- [ ] Persist and expose schedule-materialization failures without silently disabling
      a schedule or reporting the materializer as healthy.

Exit criterion: invalid data, incompatible jobs, and concurrent control operations have
provider-consistent, observable, and explicitly documented outcomes.

## Post-1.0 Milestone 11 — Sustained operations

- [ ] Add configurable retention and batched cleanup for succeeded, canceled, and
      terminal jobs, attempt history, and schedule materialization history.
- [ ] Define queue and priority fairness, and prevent or explicitly expose starvation
      of low-priority work.
- [ ] Harden recovery from retry and lease-reclamation storms with bounded backoff,
      jitter, and predictable behavior after storage outages or clock changes.
- [ ] Add long-running storage-growth, backlog, contention, and recovery tests for the
      retention, fairness, and retry guarantees.

Exit criterion: scheduler throughput and storage remain bounded and predictable under
long-running workloads, large backlogs, and recovery bursts.

## Post-1.0 Milestone 12 — Delivery integration and execution control

- [ ] Provide transactional outbox and inbox integration patterns for coordinating job
      publication and idempotent consumption with application data.
- [ ] Add optional typed job results with explicit retention and retrieval semantics.
- [ ] Add rate limits independent of worker concurrency, with queue, job-type, and
      external-service scopes.
- [ ] Support distributed cancellation signals for running jobs while preserving clear
      behavior for handlers that do not cooperate with cancellation.

Exit criterion: applications can coordinate delivery with business transactions,
control execution rates, and observe or cancel active work across worker processes.

## Post-1.0 Milestone 13 — Operator experience

- [ ] Provide supported administrative HTTP and CLI adapters for job and schedule
      inspection, cancellation, replay, triggering, and draining.
- [ ] Define authentication, authorization, and audit requirements for destructive or
      sensitive administrative operations.
- [ ] Add an optional dashboard for queue health, active work, failures, schedules,
      materialization history, and drain progress.

Exit criterion: operators can securely inspect and control the scheduler without
building a custom adapter or accessing the database directly.

## Post-1.0 Milestone 14 — Composition and provider ecosystem

- [ ] Add batches and continuation jobs with explicit partial-failure and cancellation
      behavior.
- [ ] Evaluate chains or DAG orchestration as a separate layer without weakening the
      core queue's delivery semantics.
- [ ] Define a storage-provider conformance suite and use it to qualify additional
      durable providers.

Exit criterion: higher-level composition and additional providers extend the scheduler
through explicit contracts rather than provider-specific behavior.

## Explicitly deferred

- Exactly-once execution claims remain out of scope even after transactional outbox
  and inbox support; external side effects still require application-level idempotency.
