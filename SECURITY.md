# Security policy and threat model

Do not open public issues for suspected vulnerabilities. Use GitHub's private security
advisory reporting for this repository. Include the affected version, configuration,
reproduction, and impact. Maintainers will acknowledge a report, assess severity, and
coordinate disclosure and remediation.

## Trust boundaries

Job payloads, job metadata, cron expressions, administrative input, connection strings,
and database contents are untrusted. Application handlers and the host's DI container
are trusted code. PostgreSQL and the network between workers and PostgreSQL must be
operated inside the application's trusted infrastructure.

## Threats and controls

- Payloads are data, not executable type names. Only explicitly registered job types
  can be dispatched. Applications must validate payload fields and avoid serializers
  that enable arbitrary polymorphic type construction.
- Administrative APIs can reveal payloads and can cancel, replay, trigger, or drain
  work. The library deliberately supplies no transport; adapters must authenticate,
  authorize, audit, rate-limit, and redact these operations.
- Connection strings are secrets. Supply them through a secret provider, require TLS
  where the network is not trusted, restrict the database role to the scheduler schema,
  and never emit credentials in logs.
- Deduplication is not an authorization or exactly-once boundary. Handlers must be
  idempotent and must authorize external side effects using application identity.
- Queue limits, catch-up limits, timeouts, concurrency limits, and dead-letter retention
  mitigate resource exhaustion. Operators must choose bounded values for their load.
- Leases prevent stale owners from recording success, but cannot roll back external
  side effects. Lost leases and process crashes can cause repeated invocation.
- Package publication uses repository tags, a protected environment, OIDC trusted
  publishing with hour-lived NuGet credentials, symbol packages, and Source Link.
  Maintainers must protect release tags and review dependency-audit results.

## Review scope and residual risks

The 1.0 review covers public inputs, persistence, worker ownership, administration,
secrets, denial of service, and the package supply chain. The library does not encrypt
payloads at rest, sandbox handlers, authenticate users, or provide tenant isolation.
Those controls belong to the host and database deployment. Unknown or malformed
persisted payload behavior is scheduled for contract hardening after 1.0; until then,
producers and schema access must be restricted to trusted applications.
