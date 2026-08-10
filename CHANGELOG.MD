# Changelog

All completed features and meaningful project changes are recorded here. Planned work
remains in [ROADMAP.md](ROADMAP.md) until it is completed.

## Unreleased

### Added

- .NET 10 solution with separate core library, worker host, and xUnit test project.
- Initial job model with pending, processing, succeeded, failed, and dead-lettered
  lifecycle states.
- Unit tests for immediate job creation and job type validation.
- GitHub Actions CI for dependency restore, formatting verification, Release builds,
  tests, and dependency vulnerability auditing.
- Repository-wide nullable reference types, recommended analyzers, warnings as errors,
  and NuGet auditing.
- Pinned .NET SDK configuration with compatible patch roll-forward behavior.
