# Upgrade guide

## Upgrading to 1.0

Use stable job type names when registering handlers. The implicit CLR-derived name is
supported, but explicitly naming persisted jobs prevents namespace or class renames
from invalidating queued data.

```csharp
services.AddJobHandler<SendEmail, SendEmailHandler>(
    "email.send",
    payloadVersion: 2,
    registration => registration
        .AddAlias("Legacy.SendEmail")
        .AddUpcaster(1, payload => UpgradeEmailPayload(payload)));
```

Before deploying PostgreSQL changes, back up the database and run migrations once.
Instances that must never mutate schema should set `AutoMigrate = false` and
`ValidateSchemaOnStartup = true`; they fail readiness/startup when migrations are
missing. Roll out handlers capable of reading both old and new payload versions before
producers begin writing the new version.

For a rolling deployment, drain old workers, wait for active work to finish, migrate,
deploy compatible consumers, then deploy producers. At-least-once semantics do not
change across upgrades: handlers must remain idempotent.

Future major-version guides will document removed APIs, schema ordering, and payload
migration requirements. Minor releases will not intentionally break the 1.x public API.
