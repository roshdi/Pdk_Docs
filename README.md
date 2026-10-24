# Prime Development Kit (PDK)

> Build enterprise applications from metadata, not boilerplate.

[![NuGet Pdk.Cli](https://img.shields.io/nuget/v/Pdk.Cli.svg?label=Pdk.Cli)](https://www.nuget.org/packages/Pdk.Cli)

> [!IMPORTANT]
> PDK is an early preview targeting .NET 10. Platform definitions and platform
> scaffolding are available, while service/API generation and frontend support
> are still under development.

Prime Development Kit is an opinionated .NET development platform for describing
enterprise applications with strongly typed metadata and generating repeatable
infrastructure from those definitions.

PDK separates application structure from application behavior:

- **Definitions own structure.** C# definitions describe platforms, services,
  entities, infrastructure choices, capabilities, permissions, and security.
- **Code owns behavior.** Developers write domain rules and application logic in
  developer-owned files.
- **Generated code is disposable.** `.g.cs` files are not edited manually and can be
  recreated from definitions; companion partials preserve developer customization.

The architectural direction combines microservice boundaries, vertical slices,
metadata-driven generation, and eventually metadata-driven Angular UI support.
See the [architecture reference](docs/architecture/README.md) for accepted
principles and decisions.

## What Works Today

The current preview provides:

- A strongly typed platform, service, and entity definition model
- Platform metadata for database, messaging, caching, and observability choices
- Reusable entity profiles and capability interfaces
- Permission and security metadata abstractions
- Provider-neutral L1/L2 hybrid caching with invalidation and stampede protection
- Structured diagnostics for invalid definitions
- A .NET tool that scaffolds a platform solution with `pdk new platform`
- Runtime abstractions built around Entity Framework Core

The generated platform scaffold contains Core, Platform, Shared, and Contracts
projects. Selected built-in and business services are currently generated as
service definitions, not complete deployable microservices.

Generated service feature profiles use a cardinality-oriented query DSL. A default
`HasGetItemQuery()` resolves by primary key, while `MatchBy(...)` supports unique
business-key item queries, required equality filters on list queries, update
commands, and upsert commands. An update without `MatchBy` retains its primary-key
behavior. Commands and queries can use
`WithoutEndpoint()` when their generated handlers and contracts are needed only by
internal application workflows. See the
[Entity Feature and Capability Guide](docs/entity-capability-guide.md#feature-profile-dsl)
for the complete reference.

List queries can also use `FromProjection<TSource>()` to read an unmapped,
flat LINQ result instead of a mapped table or view. PDK generates the normal
query API; a developer-owned handler partial supplies the server-side query.
See [Projection-backed list queries](docs/projection-queries.md) for examples,
security considerations, and the difference from `From<TSource>()`.

## Quick Start

Install the CLI from [NuGet](https://www.nuget.org/packages/Pdk.Cli):

```shell
dotnet tool install --global Pdk.Cli
```

Create a platform:

```shell
pdk new platform Sales PdkSample.Sales
```

The command asks you to select a database provider, optional built-in service
definitions, and business-service names. It then creates and builds the platform
solution.

```text
Sales/
|-- Readme.md
`-- src/
    |-- Sales.sln
    `-- Platform/
        |-- PdkSample.Sales.Contracts/
        |-- PdkSample.Sales.Core/
        |-- PdkSample.Sales.Platform/
        `-- PdkSample.Sales.Shared/
```

Continue with [Getting Started](docs/getting-started.md) or see the complete
[PDK CLI Guide](docs/pdk-cli-guide.md).

## Definition Model

`PlatformDefinition` is the composition root. It configures platform information,
infrastructure defaults, and registered services.

```csharp
public sealed class SalesPlatform : PlatformDefinition
{
    public override void Configure()
    {
        Platform(platform => platform
            .Name("Sales")
            .RootNamespace("PdkSample.Sales")
            .DisplayName("Sales Platform"));

        Infrastructure(infrastructure => infrastructure
            .DefaultDatabase(DatabaseProvider.PostgreSql)
            .MessageBroker(MessageBroker.RabbitMq)
            .Cache(CacheProvider.Redis)
            .OpenTelemetry());

        Services(services =>
        {
            services.Add<CustomerService>();
        });
    }
}
```

A service defines its entities, dependencies, and security metadata:

```csharp
public sealed class CustomerService : ServiceDefinition
{
    protected override void Configure()
    {
        Entities(entities =>
        {
            entities.DefaultProfile(EntityProfiles.ConcurrentHistory);

            entities.Entity("Customer")
                .Table("Customers");
        });
    }
}
```

The intended metadata flow is:

```text
PlatformDefinition
`-- Platform metadata and infrastructure
    `-- ServiceDefinition
        |-- Entities and profiles
        |-- Dependencies
        `-- Security and permissions
            `-- Validated metadata
                `-- Generators and runtime integration
```

The definition APIs are active but still evolving. Definition compatibility is
the primary stability goal as described in
[ADR-001](docs/architecture/decisions/ADR-001-strongly-typed-definitions.md).

## Security Model

PDK combines operation permissions with composable record scopes. An entity with no
security interface is deliberately anonymous; an entity with one or more security
interfaces requires permission, and its available scopes are the union supplied by
those interfaces. Multiple effective scopes are OR-composed, while `All` provides
unrestricted access and `None` contributes no access.

Roles grant permission-and-scope pairs. Users can also receive direct grants,
overall or permission-specific scope data, and user-only permission denies.
Permission implication preserves the originating grant and scope: for example,
`Update.ImpliesRead()` derives Read access but also makes Update dependent on Read.
A deny invalidates only roots whose implication closure contains the denied
permission, so independent grants survive.

Identity persists structured authorization rather than treating flattened claims as
the source of truth. Runtime authorization returns one decision containing all
effective scopes. Login can serialize that decision into the signed, versioned
`pdk:authorization` claim. Legacy permission claims become `All` grants only through
an explicit operator-selected migration policy.

Start with the [Security and Authorization Developer Guide](docs/security-capability-guide.md).
The [security architecture](docs/architecture/security-model.md) defines the
authoritative semantics.

## Architecture

PDK follows these core principles:

1. Strongly typed definitions are the structural source of truth.
2. Business behavior remains handwritten.
3. Generated artifacts are deterministic and disposable.
4. Hash-baselined companion partials preserve developer changes while allowing
   untouched stale scaffolds to be cleaned safely.
5. Strong conventions provide consistent microservice and vertical-slice structure.
6. CLI and build generation use the same metadata pipeline.
7. Invalid definitions should produce actionable diagnostics before generation.

Read the full [architecture principles](docs/architecture/principles.md) and
[architecture decision records](docs/architecture/decisions/README.md).

## Projects

| Project | Purpose | Status |
| --- | --- | --- |
| `Pdk.Core` | Runtime abstractions, entity capabilities, request context, and security primitives | Preview |
| `Pdk.Cache` | Provider-neutral memory/distributed hybrid cache and invalidation contracts | Preview |
| `Pdk.Definitions` | Strongly typed platform, service, entity, and diagnostic metadata APIs | Preview |
| `Pdk.Cli` | CLI commands, project scaffolding, templates, and discovery infrastructure | Platform scaffolding available |
| `Pdk.Data` | Shared persistence integration | Placeholder |
| `Pdk.Data.SqlServer` | SQL Server persistence provider | Placeholder |
| `Pdk.Data.PostgreSQL` | PostgreSQL persistence provider | Placeholder |
| `Pdk.Data.Oracle` | Oracle persistence provider | Placeholder |

## Libraries

### Used by Current Source

| Library | Usage |
| --- | --- |
| [.NET 10](https://dotnet.microsoft.com/) | Target runtime and SDK |
| [Entity Framework Core](https://github.com/dotnet/efcore) | Runtime data abstractions and metadata discovery |
| [Ardalis.SmartEnum](https://github.com/ardalis/SmartEnum) | Strongly typed provider definitions |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | CLI parsing and invocation |
| [Spectre.Console](https://spectreconsole.net/) | Interactive CLI prompts and output |
| [Scriban](https://github.com/scriban/scriban) | Source and project templates |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | YAML configuration serialization |
| [Microsoft.Build](https://github.com/dotnet/msbuild) | Solution and project integration |
| [Microsoft.Extensions](https://github.com/dotnet/runtime) | Hosting, dependency injection, configuration, and logging |

Tests use xUnit, FluentAssertions, and coverlet.

### Planned Application Stack

The architectural roadmap includes FastEndpoints, FluentValidation,
Riok.Mapperly, QueryKit, MassTransit, OpenTelemetry, Angular, and PrimeNG. These
libraries are not all integrated into active generators yet and may change as
the related architecture decisions are completed.

## Preview Status

| Capability | Status |
| --- | --- |
| Platform scaffolding with `pdk new platform` | Available |
| Platform/service/entity definition APIs | Preview |
| Definition diagnostics | Preview |
| Service project and API generation | In development |
| Metadata export | Experimental |
| Provider-specific persistence generation | Planned |
| Angular and PrimeNG UI generation | Planned |
| Deployment generation | Planned |

Commands other than `pdk new platform` may be incomplete during the preview.
Review the [Getting Started limitations](docs/getting-started.md#current-preview-limitations)
before adopting PDK in a production workflow.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Architecture](docs/architecture/README.md)
- [PDK CLI Guide](docs/pdk-cli-guide.md)
- [Entity Feature and Capability Guide](docs/entity-capability-guide.md)
- [Nested Command Orchestration](docs/nested-command-orchestration.md)
- [Bulk Update and Delete Commands](docs/bulk-commands.md)
- [Security and Authorization Developer Guide](docs/security-capability-guide.md)
- [Authentication and Authorization Guide](docs/authentication-guide.md)
- [Hybrid Cache Developer Guide](docs/cache-guide.md)
- [PDK and AI Code Generation](docs/pdk-vs-ai-code-generation.md)

## Build and Test

```shell
dotnet build PDK.sln
dotnet test tests/Pdk.Core.UnitTests/Pdk.Core.UnitTests.csproj
dotnet test tests/Pdk.Definitions.UnitTests/Pdk.Definitions.UnitTests.csproj
```

## Contributing

PDK is under active development. Contributions are welcome for bug fixes,
tests, documentation, definition APIs, generators, and architecture proposals.

For significant changes, review the
[architecture reference](docs/architecture/README.md) and add or update an ADR
when the change introduces a durable architectural decision.

Repository: [github.com/pdklib/Pdk](https://github.com/pdklib/Pdk)
