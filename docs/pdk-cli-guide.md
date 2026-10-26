# PDK CLI Guide

The PDK CLI scaffolds and generates Prime Development Kit applications. This
guide documents platform scaffolding and platform, API, and UI generation. See
[Current implementation status](implementation-status.md) for preview boundaries.

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
    |   |   |-- FeatureProfiles/
    |   |   `-- Pdk.Sample.Catalog.Core.csproj
    |   |-- Pdk.Sample.Catalog.Api/
    |   |-- Pdk.Sample.Catalog.Web/
    |   |-- Pdk.Sample.Catalog.UnitTests/
    |   `-- Pdk.Sample.Catalog.IntegrationTests/
    `-- Order/
        |-- Pdk.Sample.Order.Core/
        |   |-- Entities/
        |   |-- EntityConfigurations/
        |   |-- FeatureProfiles/
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
| `*.Api` | Backend API features — commands, queries, handlers, and FastEndpoints endpoints. |
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
folder (named `FeatureProfiles/`). These profiles define which commands and queries are generated and
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
`WithoutEndpoint`. Item and list queries, update commands, and upsert commands
also support `MatchBy`.

`HasGetItemQuery()` defaults to the entity primary key and continues to generate
the conventional `GetById...` operation. A custom item lookup declares its unique
match fields explicitly:

```csharp
HasGetItemQuery("GetByCode")
    .MatchBy(product => product.Code);

HasGetListQuery("ByWarehouse")
    .MatchBy(product => product.WarehouseId);

HasUpdateCommand("UpdateByCode")
    .MatchBy(product => product.Code);

HasUpsertCommand("Synchronize")
    .MatchBy(product => product.Code)
    .WithoutEndpoint();

HasBulkUpdateCommand("Activate")
    .SharedUpdate(product => product.IsActive)
    .ItemValidator<ActivateProductItemValidator>()
    .OnValidationFailure(BulkFailureBehavior.SkipInvalidItems)
    .MaximumBatchSize(5000);

HasBulkUpdateCommand("UpdatePrices")
    .MatchBy(product => new { product.WarehouseId, product.Code })
    .PerItemUpdate(product => product.Price);

HasBulkDeleteCommand()
    .MaximumBatchSize(5000);

HasBulkDeleteCommand("RemoveByWarehouseCode")
    .MatchBy(product => new { product.WarehouseId, product.Code });
```

An update command without `MatchBy` continues to locate its row by primary key.
When `MatchBy` is present, PDK includes the match fields, omits unrelated primary-key
fields from the command, and generates an `AND` predicate for composite matches.
Back every update or upsert match with a database unique key or index; execution
fails explicitly if multiple visible rows match.

Bulk updates use the primary key by default and support composite `MatchBy` keys.
`SharedUpdate` generates its common update fields directly on the command next
to the ID or `matches` collection. `PerItemUpdate` generates an `items` payload
with update values per match. The generated handler applies the security
filter, loads all rows in one query, saves once, and exposes
`CustomizeUpdateAsync` for developer-owned domain changes.

See [Bulk Update and Delete Commands](bulk-commands.md) for JSON request shapes,
per-row FluentValidation, failure policies, concurrency, and generated results.

Bulk update and delete commands can validate every matched row with FluentValidation.
Use `ItemValidator<TValidator>()` to register a validator for
`BulkItemValidationContext<TEntity, TCommand>`. `OnValidationFailure` has two
explicit policies: `RollbackBatch` (the default) saves nothing when any row is
invalid; `SkipInvalidItems` saves valid rows and reports invalid rows as skipped.
The result contains a row key and the complete FluentValidation failures, including
property, message, error code, attempted value, severity, and custom state.

Use `WithConcurrency()` on a bulk update or bulk delete to require one optimistic
concurrency value per row. The entity must implement `IOptimisticConcurrency`
and expose exactly one mapped concurrency token. Selection continues to use the
primary key or `MatchBy`; a missing or stale row rejects the whole batch with
HTTP 409.

Bulk delete generates a POST `{name}Bulk` endpoint with the same primary-key or
composite `MatchBy` selection. It applies row security, uses `ISoftDelete` when
available, otherwise removes matched entities, and exposes `CustomizeDeleteAsync`.

For a list query, generated `MatchBy` request fields are required equality
filters. They are applied before projection and compose with security filters,
QueryKit filters, sorting, and pagination. Use `ClientFilters` instead when a
filter must remain optional. The unnamed list operation generates `GET ""` and
`POST "Search"`; a named list operation generates one POST endpoint using its
name as the route.

`WithoutEndpoint()` leaves the generated contracts, validator, handler, and mapper
available for internal use. For list queries it suppresses both GET and search
endpoints. Regeneration removes stale generated endpoints while preserving modified
companion scaffolds for review.

For a list backed by a custom LINQ projection rather than a mapped table or view,
declare `HasGetListQuery("Browse").FromProjection<ProductListItem>().IncludeAll()`.
Generation keeps the owning feature's routes and permissions and requires a
developer-owned `CreateProjectionQuery` partial implementation. See
[Projection-backed list queries](projection-queries.md) for the full example,
supported scalar types, runtime pipeline, and security requirements. Use
`From<TSource>()` instead when the source is another mapped EF entity.

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
Angular cleanup likewise uses file markers and content hashes, with no persisted
manifest; legacy UI manifests are removed during cleanup. Feature-module
TypeScript and HTML structure is owned by the Scriban templates under
`src/Pdk.Cli/Templates/AngularApp/src/app/modules/`. The C# Angular grid generator
prepares validated template models and applies generated-file ownership policy; it
does not assemble module artifacts with TypeScript string builders.

### Export metadata

The `pdk metadata export` command exports the application metadata to
`application.metadata.json`:

```shell
pdk metadata export <assembly-path>
```

Use a compiled service Core assembly containing a discoverable DbContext. The
experimental discovery path inspects its EF model and feature profiles, then writes
the JSON file in the current working directory. It currently constructs discovery
options with SQL Server; it is not a general export of a `PlatformDefinition`
assembly and does not guarantee compatibility with every provider or constructor.

### Add a service

The `pdk add service` command is currently a placeholder:

```shell
pdk add service <name>
```

It prints guidance and does not create or register a service. To add one, create a
`ServiceDefinition` class in the Platform project, register it with
`Services(services => services.Add<TService>())`, and rebuild the Platform project
or run `pdk generate platform`.

### Generate a UI application

```shell
pdk generate ui <application-assembly-path> <application-project-directory>
```

The compiled assembly must contain exactly one concrete `WebApplicationDefinition`.
The command initializes and validates its module graph, discovers layouts and pages,
and generates an Angular workspace alongside the definition project. Generated UI
definition projects invoke the same command during builds.

The workflow emits API clients, grids, forms, lookups, enums, and resources from the
registered modules. Current API client/component generation errors may be printed
as warnings without making the command fail; inspect command output and build the
Angular workspace before treating generation as verified. See the
[architecture reference](architecture/README.md#generation-lifecycle) for ownership
and customization rules.

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
