---
name: implement
description: Implement repository code, tests, build, or configuration changes. Use for feature work, bug fixes, refactoring, or any request that modifies runtime behavior. Enforces focused tests, all discovered integration tests, and a 70% line-coverage gate through the repository harness.
---

# Implement

1. Read `AGENTS.md` and inspect only files relevant to the requested behavior.
2. Establish current behavior with focused tests or a minimal reproduction.
3. Implement the smallest coherent change. Preserve public compatibility unless the
   request explicitly changes it.
4. Add or update tests that directly exercise every changed behavior and important
   failure path. Do not treat the global coverage percentage as proof of relevance.
5. Name integration test projects `*.IntegrationTests`; ensure their infrastructure is
   deterministic and their tests pass when discovered by the harness.
6. Run `dotnet run --project tools/JobScheduler.Harness -- implement`. The handoff is
   blocked unless formatting and Release build pass, every unit and integration test
   passes, coverage reports exist, aggregate line coverage is at least 70%, and
   dependency audit passes.
7. Do not lower coverage, exclude changed production code, suppress analyzers, or skip
   integration tests to obtain a green result.
8. Summarize changed behavior, test evidence, coverage, and any remaining risk.
