# Job Scheduler agent instructions

## Workflow

- Use `$implement` for code or configuration changes.
- Use `$review` for review-only requests; do not edit unless explicitly asked.
- Use `$release-docs` when preparing or documenting a release.
- Run `dotnet run --project tools/JobScheduler.Harness -- implement` before handing off
  implementation.

## Engineering constraints

- Preserve provider-neutral core contracts and at-least-once execution semantics.
- Use UTC `DateTimeOffset` values and inject `TimeProvider` into time-dependent code.
- Propagate `CancellationToken` through asynchronous operations.
- Add focused tests for changed behavior. Name integration test projects
  `*.IntegrationTests` so the harness discovers and reports them separately.
- Do not suppress analyzers or lower the 70% coverage gate to make a change pass.

## Context efficiency

- Read only task-relevant files. Use `ROADMAP.md` for planned work and `CHANGELOG.MD`
  for completed work; do not restate either here.
- Keep instructions concise and move repeatable checks into `JobScheduler.Harness`.
