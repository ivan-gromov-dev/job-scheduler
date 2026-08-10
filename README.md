# Job Scheduler

A lightweight, reliable job queue and scheduler for .NET.

The foundation milestone is complete. The current goal is a small in-process API with
clear delivery semantics, followed by durable storage and horizontally scalable
workers. See [ROADMAP.md](ROADMAP.md) for scope and milestones.
Completed work is recorded in [CHANGELOG.MD](CHANGELOG.MD).

## Repository layout

- `src/JobScheduler.Core` — domain model and queue/scheduling abstractions.
- `src/JobScheduler.Worker` — generic-host worker process.
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

## Current guarantees

No stable public API or delivery guarantee is promised yet. The intended baseline is
at-least-once execution; handlers must therefore be idempotent.
