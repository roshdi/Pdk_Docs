# PDK CLI Guide

The PDK CLI scaffolds and generates Prime Development Kit applications. This
guide documents the currently supported workflow for creating a platform.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- The [`Pdk.Cli` NuGet package](https://www.nuget.org/packages/Pdk.Cli) installed as a global .NET tool

Install the latest version from NuGet:

```shell
dotnet tool install --global Pdk.Cli
```

Update an existing installation:

```shell
dotnet tool update --global Pdk.Cli
```

Verify the installation:

```shell
pdk --version
```

Display the available commands:

```shell
pdk --help
```

## Create a Platform

Use `pdk new platform` to create a solution containing the platform definition,
shared contracts, core abstractions, and selected services.

```shell
pdk new platform [name] [root-namespace]
```

Both arguments are optional. When supplied, they are used as the default values
in the interactive prompts.

| Argument | Description | Example |
| --- | --- | --- |
| `name` | Platform and output directory name | `Sample` |
| `root-namespace` | Root namespace for generated projects | `Pdk.Sample` |

Show command-specific help:

```shell
pdk new platform --help
```

### Examples

Start with empty platform and namespace prompts:

```shell
pdk new platform
```

Provide the platform name as the initial prompt value:

```shell
pdk new platform Sample
```

Provide both initial values:

```shell
pdk new platform Sample Pdk.Sample
```

### Interactive Configuration

The command collects the following configuration:

1. **Platform Name**: Used for the output directory, solution name, and platform metadata. The value is converted to PascalCase.
2. **Root Namespace**: Used as the namespace and project-name prefix.
3. **Database**: Select SQL Server, PostgreSQL, Oracle, or SQLite.
4. **Built-in Services**: Optionally select Identity, Notification, and File services. Use Space to toggle a service and Enter to continue.
5. **Business Services**: Enter one service name at a time. Submit an empty value to finish. Names are converted to PascalCase, and duplicate names are rejected case-insensitively.

The command creates the platform in the current working directory. It stops if
a directory with the generated platform name already exists.

## Generated Structure

For this command:

```shell
pdk new platform Sample Pdk.Sample
```

the generated structure is similar to:

```text
Sample/
|-- Readme.md
`-- src/
    |-- Sample.sln
    |-- Platform/
    |   |-- Pdk.Sample.Contracts/
    |   |   `-- Pdk.Sample.Contracts.csproj
    |   |-- Pdk.Sample.Core/
    |   |   |-- Entities/
    |   |   |-- Infrastructure/
    |   |   |-- Security/
    |   |   |-- Time/
    |   |   |-- GlobalUsings.cs
    |   |   `-- Pdk.Sample.Core.csproj
    |   |-- Pdk.Sample.Platform/
    |   |   |-- EntityProfiles.cs
    |   |   |-- PlatformDefinition.cs
    |   |   |-- CatalogService.cs
    |   |   |-- OrderService.cs
    |   |   `-- Pdk.Sample.Platform.csproj
    |   `-- Pdk.Sample.Shared/
    |       `-- Pdk.Sample.Shared.csproj
    |-- Identity/
    |   |-- Pdk.Sample.Identity.Core/
    |   |-- Pdk.Sample.Identity.Api/
    |   |-- Pdk.Sample.Identity.Web/
    |   |-- Pdk.Sample.Identity.UnitTests/
    |   `-- Pdk.Sample.Identity.IntegrationTests/
    |-- Catalog/
    |   |-- Pdk.Sample.Catalog.Core/
    |   |   |-- Entities/
    |   |   |-- EntityConfigurations/
    |   |   |-- Features/
    |   |   `-- Pdk.Sample.Catalog.Core.csproj
    |   |-- Pdk.Sample.Catalog.Api/
    |   |-- Pdk.Sample.Catalog.Web/
    |   |-- Pdk.Sample.Catalog.UnitTests/
    |   `-- Pdk.Sample.Catalog.IntegrationTests/
    `-- Order/
        |-- Pdk.Sample.Order.Core/
        |   |-- Entities/
        |   |-- EntityConfigurations/
        |   |-- Features/
        |   `-- Pdk.Sample.Order.Core.csproj
        |-- Pdk.Sample.Order.Api/
        |-- Pdk.Sample.Order.Web/
        |-- Pdk.Sample.Order.UnitTests/
        `-- Pdk.Sample.Order.IntegrationTests/
```

### Platform projects

The `Platform/` folder contains the four platform-level projects:

| Project | Purpose |
| --- | --- |
| `*.Core` | Entity capability interfaces, security abstractions, infrastructure extensions, and shared types. References `Pdk.Core`. |
| `*.Platform` | Contains `PlatformDefinition`, service definitions, and `EntityProfiles`. References `Pdk.Definitions` and `*.Core`. |
| `*.Shared` | Shared utilities and DTOs available to all projects. References `Pdk.Core`. |
| `*.Contracts` | Shared contract types and message definitions. References `Pdk.Core`. |

### Service projects

Each service gets its own folder under `src/` with five projects following
the naming convention `{RootNamespace}.{ServiceName}.{ProjectType}`:

| Project | Purpose |
| --- | --- |
| `*.Core` | Service-specific core abstractions, entity models, and business logic interfaces. |
| `*.Api` | Backend API features — commands, queries, handlers, and controllers. |
| `*.Web` | ASP.NET Core host with `Program.cs` for configuring and running the application. |
| `*.UnitTests` | Unit tests for the service. |
| `*.IntegrationTests` | Integration tests for the service. |

Only selected built-in services are generated. Each service produces a
`<Name>Service.cs` definition registered in `PlatformDefinition.cs`.

## PlatformDefinition

The generated `PlatformDefinition.cs` contains three configuration sections.

### Platform

Defines the identity metadata:

```csharp
Platform(platform =>
{
    platform.Name("Sample")
            .RootNamespace("Pdk.Sample")
            .DisplayName("Sample Platform");
});
```

| Property | Description |
| --- | --- |
| `Name` | Platform name, used for directory and solution naming |
| `RootNamespace` | Root namespace prefix for all generated projects |
| `DisplayName` | Human-readable display name |

### Infrastructure

Records the technology choices:

```csharp
Infrastructure(infra =>
{
    infra.DefaultDatabase(DatabaseProvider.SqlServer)
         .MessageBroker(MessageBroker.RabbitMq)
         .Cache(CacheProvider.Redis)
         .OpenTelemetry();
});
```

| Property | Options | Description |
| --- | --- | --- |
| `DatabaseProvider` | `SqlServer`, `PostgreSql`, `Oracle`, `Sqlite` | Primary database provider |
| `MessageBroker` | `RabbitMq`, `AzureServiceBus`, `AmazonSqs`, `None` | Message broker selection |
| `CacheProvider` | `Redis`, `Memory`, `None` | Distributed cache provider |
| `OpenTelemetry` | (flag) | Enables OpenTelemetry instrumentation |

### Services

Registers built-in and business services:

```csharp
Services(services =>
{
    services.Add<IdentityService>();
    services.Add<NotificationService>();
    services.Add<CatalogService>();
    services.Add<OrderService>();
});
```

The developer can add:

- **Built-in services** — `IdentityService`, `NotificationService`, `FileService`.
  They are selected during scaffolding via the interactive prompt. Identity generates
  ASP.NET Core Identity persistence, configurable login sessions, seed discovery, and
  authentication adapters. See the
  [Authentication, Authorization, and Sessions guide](authentication-guide.md).
- **Business services** — any `ServiceDefinition` subclass that the developer
  defines for their business domain. Each service gets its own
  `{ServiceName}Service.cs` file and five generated projects.

Use `x.Add<TService>()` in the `Services(...)` section to register a service.

Each service can override platform defaults:

```csharp
public sealed class OrderService : ServiceDefinition
{
    protected override void Configure()
    {
        Entities(entities =>
        {
            entities.DefaultProfile(EntityProfiles.ConcurrentHistory);

            entities.Entity("Customer")
                .Table("Customers");

            entities.Entity("Order")
                .Table("Orders");

            entities.Entity("OrderItem")
                .Table("OrderItems");

            entities.Entity("OrderItemDetail")
                .Table("OrderItemDetails")
                .Profile(EntityProfiles.Audited);
        });
    }
}
```

## Feature Profiles

Each entity gets a `FeatureProfile<TEntity>` in the Core project's `Features/`
folder. These profiles define which commands and queries are generated and
which fields are included:

```csharp
public class ProductCategoryEntityFeatureProfile
    : FeatureProfile<ProductCategoryEntity>
{
    public override void Configure()
    {
        HasCreateCommand();
        HasUpdateCommand();
        HasDeleteCommand();
        HasGetItemQuery();
        HasGetListQuery();
        HasLookupQuery();
    }
}
```

Commands and queries apply default field rules based on the entity's
capability properties (audit fields, soft-delete flags). Use `IncludeAll()`
to override defaults.

The `Has*` methods return a `CommandBuilder<T>` or `QueryBuilder<T>` with
fluent methods: `Exclude`, `Include`, `IncludeAll`, `IncludeOnly`,
`IncludeNavigation`, `IncludeDetail`, `ExcludeNavigation`, and
`WithoutEndpoint`. Item queries, update commands, and upsert commands also support
`MatchBy`.

`HasGetItemQuery()` defaults to the entity primary key and continues to generate
the conventional `GetById...` operation. A custom item lookup declares its unique
match fields explicitly:

```csharp
HasGetItemQuery("GetByCode")
    .MatchBy(product => product.Code);

HasUpdateCommand("UpdateByCode")
    .MatchBy(product => product.Code);

HasUpsertCommand("Synchronize")
    .MatchBy(product => product.Code)
    .WithoutEndpoint();
```

An update command without `MatchBy` continues to locate its row by primary key.
When `MatchBy` is present, PDK includes the match fields, omits unrelated primary-key
fields from the command, and generates an `AND` predicate for composite matches.
Back every update or upsert match with a database unique key or index; execution
fails explicitly if multiple visible rows match.

`WithoutEndpoint()` leaves the generated contracts, validator, handler, and mapper
available for internal use. For list queries it suppresses both GET and search
endpoints. Regeneration removes stale generated endpoints while preserving modified
companion scaffolds for review.

Multi-level master-detail is supported via nested `IncludeDetail` calls:

```csharp
public class OrderEntityFeatureProfile
    : FeatureProfile<OrderEntity>
{
    public override void Configure()
    {
        HasCreateCommand()
            .IncludeNavigation(o => o.Customer)
            .IncludeDetail(o => o.OrderItems, b =>
                b.Exclude(nameof(OrderItemEntity.Sku)));

        HasGetItemQuery()
            .IncludeNavigation(o => o.Customer)
            .IncludeDetail(o => o.OrderItems, b =>
                b.IncludeDetail(oi => oi.OrderItemDetails));
    }
}
```

See the [Entity Feature and Capability Guide](entity-capability-guide.md)
for the complete DSL reference.

## Entity Capability Interfaces

The Core project contains an `Entities/` folder with default interfaces that
add capabilities to entities. Implementing these interfaces on an entity class
enables specific behaviors:

| Interface | Capability | Properties |
| --- | --- | --- |
| `ICreatedEntity` | Creation audit tracking | `CreatedOn`, `CreatedBy` — auto-set via `IRequestContext` |
| `IModifiedEntity` | Modification audit tracking | `ModifiedOn`, `ModifiedBy` — auto-set when entity changes |
| `IDeletedEntity` | Soft delete with full audit | `IsDeleted`, `DeletedOn`, `DeletedBy` — set/unset methods with timestamp |
| `ISoftDeleteEntity` | Soft delete (simplified) | `IsDeleted` only |
| `IConcurrentEntity` | Optimistic concurrency | `Version` string for row-level conflict detection |

These are default generated code. Developers can customize them or add any other
interfaces as needed. `IConcurrentEntity` is the default concurrency interface;
any interface implementing `IOptimisticConcurrency` is detected automatically
and mapped to the database's native concurrency mechanism via provider-specific
converters in `Pdk.Data.*` packages.

The actual capability contracts live in `Pdk.Core.Abstractions`:

| Capability | Interface | Method |
| --- | --- | --- |
| Creation audit | `ICreated` | `SetCreated(IRequestContext)` |
| Modification audit | `IModified` | `SetModified(IRequestContext)` |
| Soft delete | `ISoftDelete` | `SetDeleted(IRequestContext)`, `SetUndeleted(IRequestContext)` |
| Optimistic concurrency | `IOptimisticConcurrency` | `SetConcurrency(IRequestContext)` |

Any interface extending these contracts is detected automatically by the code
generator — regardless of its name or the name of the properties it declares.
The Core project also provides security interfaces
(`IBasicSecurity`, `IOwnedSecurity`, `IUnitSecurity`), authentication
(`IAuthService`), and time (`IClockService`) abstractions.

## Build-Time Service Generation

The generated Platform project contains an `AfterBuild` target that runs:

```shell
pdk generate platform "$(TargetPath)" "$(ProjectDir)"
```

The command reflects over the compiled `PlatformDefinition`, validates the
metadata, and creates or updates all five projects (Core, Api, Web, UnitTests,
IntegrationTests) for every registered built-in or business service. Projects
are added to the platform solution and to a service-specific solution.
Fully generated files are marked and rewritten only when their content changes.
Existing companion scaffolds are never overwritten.

You can run the command directly when diagnosing generation or in CI:

```shell
pdk generate platform <assembly-path> <platform-project-directory>
```

### Generate API for a single service

The `pdk generate api` command generates commands, queries, handlers,
endpoints, validators, and groups for a single service's API project:

```shell
pdk generate api <core-assembly-path> <core-project-directory>
```

| Argument | Description |
| --- | --- |
| `core-assembly-path` | Path to the compiled Core assembly (`*.Core.dll`) |
| `core-project-directory` | Path to the service's Core project directory |

Before generation, the command inventories marked C# files in API-owned locations.
After a successful run, files not registered as expected output are stale. For
example, removing `HasCreateCommand()` makes its create-command files stale on the
next Core build.

Stale `.g.cs` files are safe to delete because they are fully replaceable. New
companion `.cs` files store a SHA-256 hash of their original body in a single marker:
`// <pdk-auto-generated algorithm="sha256" value="..." />`. A stale companion is
deleted only when its current body still matches that hash. Modified companions and
legacy companions without a hash are preserved. Cleanup is limited to `Features/`
and the generated service module, runs only after successful generation, and removes
empty directories when possible.

The hash covers the file body without the marker. PDK normalizes the UTF-8 BOM and
line endings before hashing, but treats formatting and whitespace edits as developer
changes. The preceding two-line marker/hash format remains readable for compatibility.
The retired `.pdk/generate-api.manifest.json` is removed after successful cleanup;
Angular generation continues to use its separate UI manifest policy.

### Export metadata

The `pdk metadata export` command exports the application metadata to
`application.metadata.json`:

```shell
pdk metadata export <assembly-path>
```

The assembly path can be either the Platform or Core assembly. The command
discovers all entities, their capabilities, and feature profiles, then
writes a JSON metadata file.

### Add a service

The `pdk add service` command adds a new service to an existing platform:

```shell
pdk add service <name>
```

This creates a new `ServiceDefinition` class in the Platform project and
registers it in `PlatformDefinition.cs`.

### Direct API command

`pdk generate api` generates the API project for a single service. It accepts
the compiled Core assembly path and the Core project directory:

```shell
pdk generate api <core-assembly-path> <core-project-directory>
```

This command is invoked automatically when a generated service Core project builds. Use it
directly when troubleshooting API generation or generating a single service
in CI.

### Database migrations

Each service's Core project contains a `psCommands/` folder with three
PowerShell scripts for managing Entity Framework Core migrations. The scripts
resolve project paths automatically using `$PSScriptRoot` and can be run from
any directory.

**Add a migration:**

```shell
.\psCommands\add-migration.ps1
```

Prompts for the migration name. Pass the name directly to skip the prompt:

```shell
.\psCommands\add-migration.ps1 "AddCustomerTable"
```

If no `Migrations/` folder exists yet, the first migration is automatically
named `InitialCreation`.

**Apply migrations:**

```shell
.\psCommands\update-database.ps1
```

Prompts for the migration name. Pass the name directly to skip the prompt:

```shell
.\psCommands\update-database.ps1 "AddCustomerTable"
```

**Remove the last migration:**

```shell
.\psCommands\remove-migration.ps1
```

All scripts are auto-generated per service with the correct `DbContext` name
and project paths.

## Completion and Errors

Before reporting success, the CLI:

1. Creates the solution.
2. Generates all projects and source files.
3. Adds each project to the solution.
4. Builds the generated platform project.

The command returns exit code `0` when all steps succeed. It returns `1` and
prints an error when scaffolding or the final build fails.

After successful generation, open the solution from the generated directory:

```shell
cd Sample/src
dotnet build Sample.sln
```
