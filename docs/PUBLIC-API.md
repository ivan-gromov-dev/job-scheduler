# Public API

Version 1.0 stabilizes the public types in the three shipped packages. Applications
normally depend on contracts rather than concrete stores or worker services.

## JobScheduler.Core

- `IJobClient` enqueues immediate and delayed typed jobs.
- `IJobAdministration` lists, cancels, and replays jobs.
- `IJobHandler<TJob>` defines a typed handler; register it with `AddJobHandler` and a
  stable persisted type name.
- `IScheduleClient` creates one-off and recurring schedules; `IScheduleStore` supplies
  administrative operations and materialization.
- `IJobPayloadSerializer` replaces the default JSON serializer without introducing a
  provider dependency.
- `IJobStore` is the provider contract. Its lease token makes completion, retry, and
  dead-letter transitions conditional on current ownership.

`Job`, `Schedule`, their option/query records, failure types, cursor pages, and enums
are durable API data shapes. Timestamps are UTC `DateTimeOffset` values. Cancellation
tokens are propagated, but canceling a caller does not revoke an already accepted job.

## JobScheduler.Worker

`AddJobWorker` registers execution and dead-letter maintenance;
`AddScheduleMaterializer` registers recurring schedule materialization.
`JobWorkerOptions`, `ScheduleMaterializerOptions`, and `RetryPolicy` are configuration
contracts. `IJobWorkerControl` initiates draining and reports `DrainProgress`.

## JobScheduler.PostgreSql

`AddPostgreSqlJobStore` replaces in-memory stores with durable PostgreSQL stores.
`PostgreSqlJobStoreOptions` controls the connection, migration, and schema-validation
behavior. Concrete stores and the migrator are public for advanced hosting and
provider diagnostics, but applications should prefer the provider-neutral contracts.

Compatibility is governed by [SUPPORT.md](SUPPORT.md). The release compatibility suite
compiles the 1.0 entry points and guards representative signatures against accidental
source or binary breaks.
