# Job Scheduler agent instructions

## Route work

- `JobScheduler.Core`: provider-neutral contracts, models, and in-memory behavior.
- `JobScheduler.Worker`: execution, retry, and schedule materialization.
- `JobScheduler.PostgreSql`: PostgreSQL persistence and migrations.
- Start with the owning module, its directly used contracts, and focused tests. Expand
  only when dependencies or failures require it.
- Read `ROADMAP.md` for planned work and `CHANGELOG.MD` for completed work.

## Workflow

- Use `$implement` for changes, `$review` for review-only work, and `$release-docs` for
  releases.
- Add focused tests for changed behavior. Name integration projects
  `*.IntegrationTests` so the harness discovers them separately.
- Before handing off changes, run
  `dotnet run --project tools/JobScheduler.Harness -- implement`.

## Constraints

- Keep project-root C# files limited to `Program` and DI/`ServiceCollection` entry
  points.
- Put models in `Models`, interfaces in `Interfaces`, and logic in focused directories
  such as `Services`, `Stores`, or `Persistence`; split categories by feature as needed.
- Preserve provider-neutral core contracts and at-least-once execution semantics.
- Use UTC `DateTimeOffset`, inject `TimeProvider`, and propagate `CancellationToken`.
- Never suppress analyzers, skip integration tests, or lower the 70% coverage gate.
- Keep this file concise; encode repeatable checks in `JobScheduler.Harness`.
