---
name: review
description: Review repository changes without modifying them. Use for code review, pull-request review, regression analysis, or requests to assess correctness, concurrency, API compatibility, tests, security, and operability.
---

# Review

1. Read `AGENTS.md`, the requested diff, and enough surrounding code to trace behavior.
2. Identify correctness bugs, races, lease/state-transition violations, cancellation or
   time errors, compatibility breaks, missing tests, security issues, and operational
   regressions. Prefer concrete failure scenarios over stylistic preferences.
3. Run `dotnet run --project tools/JobScheduler.Harness -- review` when execution is
   safe. Report unavailable external integration separately; never describe an unrun
   check as passed.
4. Report findings first, ordered by severity. Include tight file and line references,
   impact, and the smallest credible remediation.
5. If there are no findings, say so and list residual risks or untested boundaries.
6. Do not edit files unless the user explicitly requests fixes after the review.
