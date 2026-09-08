# Support and versioning policy

The project follows Semantic Versioning. The public 1.x API consists of public types
in the three published `JobScheduler.*` packages. Minor releases may add APIs and
optional schema migrations; patch releases contain compatible fixes. Breaking public
API or persisted-contract changes require a major release and an upgrade guide.

The latest minor release receives fixes. Security fixes may be backported to the
previous minor when impact and feasibility justify it. Supported runtimes follow the
.NET support lifecycle; 1.0 targets .NET 10. PostgreSQL provider support is tested
against PostgreSQL 17, and newer server versions are supported when PostgreSQL retains
protocol and SQL compatibility.

Report reproducible bugs through GitHub issues. Report vulnerabilities privately as
described in [SECURITY.md](../SECURITY.md). Releases include `.nupkg` and `.snupkg`
artifacts; consumers should pin an exact or bounded version and verify package source.
Publishing uses NuGet Trusted Publishing; the `nuget` GitHub environment must define
the non-secret `NUGET_USER` variable with the owning NuGet.org profile name.
