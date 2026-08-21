# Getting Started with PDK

> [!IMPORTANT]
> PDK is an early preview targeting .NET 10. `pdk new platform` scaffolds a
> platform, and building its Platform project generates the Core and API project
> structure for registered services. Generated APIs include commands, queries,
> handlers, endpoints, validators, and API groups with full master-detail support.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A terminal with the `dotnet` command available

Verify the SDK:

```shell
dotnet --version
```

## Install the CLI

Install [`Pdk.Cli`](https://www.nuget.org/packages/Pdk.Cli) as a global .NET
tool:

```shell
dotnet tool install --global Pdk.Cli
```

Verify the installation:

```shell
pdk --version
pdk --help
```

To update an existing installation:

```shell
dotnet tool update --global Pdk.Cli
```

## Create a Platform

Run the command from the directory that should contain your new platform:

```shell
pdk new platform Sample Pdk.Sample
```

The name and root namespace are optional initial values. The command remains
interactive and asks for:

1. Platform name
2. Root namespace
3. Database provider: SQL Server, PostgreSQL, Oracle, or SQLite
4. Optional Identity, Notification, and File service definitions
5. Zero or more business-service names

Use Space to toggle built-in services and Enter to continue. Submit an empty
business-service name to finish. For this walkthrough, enter `Catalog` and
`Order` as business services.

The command will not overwrite an existing platform directory.

## Generated Structure

The preceding example creates:

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
    |-- Notification/
    |   |-- Pdk.Sample.Notification.Core/
    |   |-- Pdk.Sample.Notification.Api/
    |   |-- Pdk.Sample.Notification.Web/
    |   |-- Pdk.Sample.Notification.UnitTests/
    |   `-- Pdk.Sample.Notification.IntegrationTests/
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
| `*.Core` | Entity models, entity configurations, feature profiles, and DbContext. |
| `*.Api` | Commands, queries, handlers, endpoints, validators, and API groups. |
| `*.Web` | ASP.NET Core host with `Program.cs` for configuring and running the application. |
| `*.UnitTests` | Unit tests for the service. |
| `*.IntegrationTests` | Integration tests for the service. |

The `Core` project's `Features/` folder contains a `FeatureProfile` for each
entity that controls which commands and queries are generated.

## Inspect the Platform Definition

Open `PlatformDefinition.cs`. It contains three configuration sections:

### Platform

Defines the identity metadata for the platform:

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

Records the technology choices for the platform:

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

Cache selection is connected to generated runtime registration. Redis platforms use
memory-backed L2 during Development and require `ConnectionStrings:Cache` in other
environments. `CacheProvider.Memory` is intended for development, tests, or a single
host. See the [Hybrid Cache Developer Guide](cache-guide.md). Other infrastructure
choices remain preview metadata where their provider integration is not yet complete.

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
  They are selected during scaffolding via the interactive prompt. Identity includes
  generated ASP.NET Core Identity persistence, login sessions, seeds, and runtime
  adapters; see the [Authentication guide](authentication-guide.md).
- **Business services** — any `ServiceDefinition` subclass that the developer
  defines for their business domain. Each service gets its own
  `{ServiceName}Service.cs` file and five generated projects.

### Service-Level DSL

Services can override platform defaults, declare dependencies, and configure
entity security:

```csharp
public sealed class CatalogService : ServiceDefinition
{
    protected override void Configure()
    {
        // Default profile applied to all entities unless overridden
        Entities(entities =>
        {
            entities.DefaultProfile(EntityProfiles.Audited);

            entities.Entity("ProductCategory")
                .Table("ProductCategories")
                .HasSecurity<IBasicSecurity>(b =>
                {
                    b.Add("MyPermission");
                });

            entities.Entity("Product")
                .Table("Products")
                .Profile(EntityProfiles.ConcurrentHistory)
                .HasSecurity<IBasicSecurity>();

            entities.Entity("ProductVariant")
                .Table("ProductVariants")
                .Profile(EntityProfiles.ConcurrentHistory);

            entities.Entity("ProductImage")
                .Table("ProductImages");
        });
    }
}

public sealed class OrderService : ServiceDefinition
{
    protected override void Configure()
    {
        Entities(entities =>
        {
            entities.DefaultProfile(EntityProfiles.ConcurrentHistory);

            entities.Entity("Customer")
                .Table("Customers")
                .HasSecurity<IBasicSecurity>();

            entities.Entity("Order")
                .Table("Orders")
                .HasSecurity<IOwnedSecurity>();

            entities.Entity("OrderItem")
                .Table("OrderItems");

            entities.Entity("OrderItemDetail")
                .Table("OrderItemDetails")
                .Profile(EntityProfiles.Audited);
        });
    }
}
```

| DSL Method | Purpose |
| --- | --- |
| `Database(provider)` | Override the platform default database provider for this service |
| `Dependencies(d => ...)` | Declare service-to-service dependencies |
| `Security(s => ...)` | Configure authentication, row-level security, and identity service |
| `HasSecurity<T>()` | Assign a security capability to an entity (see below) |

#### Security DSL

Entities without a security interface are deliberately anonymous. One or more
security interfaces require authorization; their scopes are combined into the
entity's available scope set.

```csharp
// Anonymous — no authorization required
entities.Entity("ProductImage")
    .Table("ProductImages");

// Secured — endpoints require authorization.
// Effective scopes: None, Owner, Unit, All.
entities.Entity("Product")
    .HasSecurity<IOwnedSecurity>()
    .HasSecurity<IUnitSecurity>();
```

Each `HasSecurity<T>()` call adds a reusable capability, its scopes, entity
requirements, filters, and default permissions. The CLI validates the complete
composition and stops before generation on conflicting properties, duplicate scopes,
invalid filters, or an invalid permission implication graph.

```csharp
// IBasicSecurity defaults + custom permission
entities.Entity("ProductCategory")
    .HasSecurity<IBasicSecurity>(b =>
    {
        b.Add("MyPermission");
    });
// Generates: Read, Create, Update, Delete + MyPermission
```

| Method | Description |
| --- | --- |
| `HasSecurity<T>()` | Add a security capability and its default permissions |
| `HasSecurity<T>(b => ...)` | Add a security capability and custom permissions |
| `b.Add("Name")` | Add a custom permission |
| `b.Read()` | Add Read permission explicitly |
| `b.Create()` | Add Create permission explicitly |
| `b.Group("Prefix", g => ...)` | Create a permission group with a custom prefix |

Permissions are always granted with a scope. Roles carry permission-and-scope pairs;
scope data belongs to users and may be overall or permission-specific. Multiple
effective scopes are OR-composed. Direct user denies override allowing roots that
depend on the denied permission. See the
[Security and Authorization Developer Guide](security-capability-guide.md) for the
complete model.

## Define a Feature Profile

Each entity gets a `FeatureProfile` that controls which commands and queries are
generated and which fields are included. The Core project's `Features/` folder
contains a generated profile for each entity.

### Single-Level Commands and Queries

A simple entity like `ProductCategory` uses standard CRUD operations with no
navigation properties:

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

This generates six operations:

| Operation | HTTP | Endpoint | Description |
| --- | --- | --- | --- |
| CreateProductCategory | POST | `/Catalog/ProductCategories` | Create a new category |
| UpdateProductCategory | PUT | `/Catalog/ProductCategories` | Update an existing category |
| DeleteProductCategory | DELETE | `/Catalog/ProductCategories/{id}` | Soft-delete a category |
| GetByIdProductCategory | GET | `/Catalog/ProductCategories/{id}` | Get a single category |
| GetListProductCategory | GET | `/Catalog/ProductCategories` | Search/list categories |
| GetLookupProductCategory | GET | `/Catalog/ProductCategories/Lookup` | Lightweight lookup |

Feature profiles also define the expected API output set. If an operation such as
`HasCreateCommand()` is removed, the next successful API generation deletes its
stale `.g.cs` files and any untouched hash-baselined companion scaffolds. Companion
files containing developer changes are preserved. See
[Generated-output cleanup](pdk-cli-guide.md#generate-api-for-a-single-service) for
the complete safety rules.

`HasGetItemQuery()` uses the primary key by default and therefore still generates
the conventional `GetById...` operation. It can also generate another unique item
lookup:

```csharp
HasGetItemQuery("GetByCode")
    .MatchBy(product => product.Code);
```

This generates a `GetByCode/{code}` route, request validation, and the matching
handler predicate. Keep a unique database key or index on custom match fields.

Update commands use the primary key by default. To update an existing row by a
stable business key instead, configure the same `MatchBy` DSL:

```csharp
HasUpdateCommand("UpdateByCode")
    .MatchBy(product => product.Code);

HasUpdateCommand("UpdateByWarehouseAndCode")
    .MatchBy(product => product.WarehouseId, product => product.Code);
```

With a custom match, the generated command contains the match fields but omits the
unrelated primary key. Composite fields are combined with `AND`. The database must
enforce the same uniqueness; PDK reports an integrity failure if multiple rows
match. Match values identify the existing row, are excluded from generated update
mapping, and are not a mechanism for renaming
that key—use separate existing/new fields in an explicit command for that workflow.
`HasGetByIdQuery()` remains available as a compatibility alias.

Each `Has*` method returns a `CommandBuilder<T>` or `QueryBuilder<T>` for
fluent field selection:

```csharp
public class CustomerEntityFeatureProfile
    : FeatureProfile<CustomerEntity>
{
    public override void Configure()
    {
        // Custom command name — generates "RegisterCustomer" instead of "CreateCustomer"
        HasCreateCommand("Register");

        HasUpdateCommand();
        HasDeleteCommand();
        HasGetItemQuery();
        HasGetListQuery();

        // Lookup includes only Id and Email
        HasLookupQuery()
            .Include(x => x.Email);
    }
}
```

Commands and queries apply default field rules based on the entity's capability
interfaces. For example, `HasCreateCommand()` automatically excludes primary
keys, concurrency tokens, and audit properties. Use `IncludeAll()` to override
these defaults.

A query can keep the owning feature's route and permissions while reading from
another EF entity or database view:

```csharp
HasLookupQuery()
    .From<ProductLookupViewEntity>();
```

The generated DTO and handler source fields come from
`ProductLookupViewEntity`; generated names, folders, and routes continue to use
the feature owner's entity.

Use an upsert for synchronization workflows where a stable business key identifies
the row:

```csharp
HasUpsertCommand("Synchronize")
    .MatchBy(product => product.Code);
```

Update and upsert matches are applied to the security-filtered query. The database
must enforce uniqueness for the selected match fields. Add
`.WithoutEndpoint()` to any command or query that should keep its generated
contract, validator, handler, and mapper but must not expose an HTTP endpoint.

### Multi-Level Master-Detail Commands and Queries

Entities with navigation properties can include related data in commands and
queries. The `Order` entity demonstrates a three-level master-detail hierarchy:

```
Customer (master)
  └── Order (child)
        └── OrderItem (detail)
              └── OrderItemDetail (nested detail)
```

The `OrderFeatureProfile` configures which navigations to include:

```csharp
public class OrderEntityFeatureProfile
    : FeatureProfile<OrderEntity>
{
    public override void Configure()
    {
        HasCreateCommand()
            // Include Customer navigation — read-only reference data
            .IncludeNavigation(o => o.Customer)
            // Include OrderItems as a child collection
            .IncludeDetail(o => o.OrderItems, b =>
            {
                // Exclude computed fields from input
                b.Exclude(
                    nameof(OrderItemEntity.Sku),
                    nameof(OrderItemEntity.TotalPrice));
            });

        HasUpdateCommand()
            // OrderItems are updatable — each item carries EntityState
            .IncludeDetail(o => o.OrderItems);

        HasDeleteCommand();

        HasGetItemQuery()
            // Eager-load the full order hierarchy
            .IncludeNavigation(o => o.Customer)
            .IncludeDetail(o => o.OrderItems, b =>
            {
                // Nested: also load OrderItemDetails within each OrderItem
                b.IncludeDetail(oi => oi.OrderItemDetails);
            });

        HasGetListQuery();
        HasLookupQuery();
    }
}
```

#### CreateOrderCommand (POST)

The create command accepts the order and its child items in a single request:

```json
{
  "customerId": 1,
  "orderNumber": "ORD-001",
  "orderDate": "2026-01-15",
  "status": "Pending",
  "totalAmount": 99.99,
  "customer": { "name": "Alice", "email": "alice@example.com" },
  "orderItems": [
    {
      "productName": "Widget",
      "quantity": 2,
      "unitPrice": 49.99
    }
  ]
}
```

The `Customer` navigation is included as read-only reference data. The
`OrderItems` collection is included as child entities — `Sku` and `TotalPrice`
are excluded because they are computed.

#### UpdateOrderCommand (PUT)

The update command includes OrderItems with entity state tracking. Each item
carries an `EntityState` (Added, Updated, or Deleted) so the handler knows
whether to insert, modify, or remove it:

```json
{
  "orderId": 1,
  "customerId": 1,
  "orderNumber": "ORD-001",
  "status": "Shipped",
  "totalAmount": 149.98,
  "version": "AAAAAAAAB9k=",
  "orderItems": [
    {
      "id": 10,
      "entityState": "Updated",
      "productName": "Widget Pro",
      "quantity": 3,
      "unitPrice": 49.99,
      "version": "AAAAAAAAB9k="
    },
    {
      "entityState": "Added",
      "productName": "Gadget",
      "quantity": 1,
      "unitPrice": 99.99
    }
  ]
}
```

New items have no `id`. Updated items carry their `id` and `version`. Deleted
items use `entityState: "Deleted"`.

#### GetByIdOrderQuery (GET)

The get-by-id query returns the full hierarchy with nested data:

```json
{
  "orderId": 1,
  "customerId": 1,
  "orderNumber": "ORD-001",
  "orderDate": "2026-01-15T00:00:00",
  "status": "Shipped",
  "totalAmount": 149.98,
  "version": "AAAAAAAAB9k=",
  "customer": {
    "id": 1,
    "name": "Alice",
    "email": "alice@example.com",
    "phone": "+1-555-0100"
  },
  "orderItems": [
    {
      "id": 10,
      "orderId": 1,
      "productName": "Widget Pro",
      "quantity": 3,
      "unitPrice": 49.99,
      "totalPrice": 149.97,
      "orderItemDetails": [
        {
          "id": 20,
          "orderItemId": 10,
          "attributeName": "Color",
          "attributeValue": "Blue"
        }
      ]
    }
  ]
}
```

Three levels of data are loaded in a single query: Order → OrderItems →
OrderItemDetails. The `Sku` and `TotalPrice` fields on OrderItems are excluded
from the create command input but included in query results.

### Navigation DSL Reference

| Method | Purpose |
| --- | --- |
| `IncludeNavigation(expr)` | Include a scalar navigation (many-to-one reference) |
| `IncludeDetail(expr, builder?)` | Include a collection navigation (one-to-many children) |
| `ExcludeNavigation(expr)` | Exclude a navigation from the operation |

The `NavigationBuilder<T>` inside the detail callback supports:

| Method | Description |
| --- | --- |
| `IncludeDetail(expr, builder?)` | Nest a child collection within the detail |
| `IncludeNavigation(expr)` | Include a scalar reference within the detail |
| `Exclude(props...)` | Exclude specific fields from the detail |
| `IncludeOnly(props...)` | Include only specific fields |

### Custom Swagger Summary

Override the default Swagger/OpenAPI summary and description for any endpoint:

```csharp
HasCreateCommand()
    .Summary(s =>
    {
        s.Summary = "Creates a new Product record.";
        s.Description = "Creates a new Product record in the catalog.";
    });
```

Without `Summary()`, endpoints use auto-generated text like
`"Creates a new Product record."`. See the
[Entity Feature and Capability Guide](entity-capability-guide.md) for
the complete DSL reference.

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

These are default generated code and serve as a starting point. Developers can
customize them or add any other interfaces as needed. `IConcurrentEntity` is the
default concurrency interface; any interface implementing `IOptimisticConcurrency`
is detected automatically and mapped to the database's native concurrency
mechanism via provider-specific converters in `Pdk.Data.*` packages.

The actual capability contracts live in `Pdk.Core.Abstractions`:

| Capability | Interface | Method |
| --- | --- | --- |
| Creation audit | `ICreated` | `SetCreated(IRequestContext)` |
| Modification audit | `IModified` | `SetModified(IRequestContext)` |
| Soft delete | `ISoftDelete` | `SetDeleted(IRequestContext)`, `SetUndeleted(IRequestContext)` |
| Optimistic concurrency | `IOptimisticConcurrency` | `SetConcurrency(IRequestContext)` |

Any interface extending these contracts is detected automatically by the code
generator — regardless of its name or the name of the properties it declares.

The Core project also provides:

- **Security interfaces** (`IBasicSecurity`, `IOwnedSecurity`, `IUnitSecurity`)
  that add permission and scope constraints to entities.
- **`IAuthService`** and **`IClockService`** abstractions for authentication
  and time.
- **`RequestContextExtensions`** that expose `Clock` and `Auth` from
  `IRequestContext`.

## Build the Solution

```shell
cd Sample/src
dotnet restore
dotnet build Sample.sln
```

Building the solution also builds the Platform project. Its `AfterBuild`
target runs `pdk generate platform`, which reflects over the compiled
`PlatformDefinition` and creates or updates all five projects (Core, Api,
Web, UnitTests, IntegrationTests) for every registered service. The first
build generates all service projects; subsequent builds update them when
definitions change.

Keep the global `pdk` tool available when building generated projects.
Rebuild just the Platform project when you change definitions:

```shell
dotnet build Platform/Pdk.Sample.Platform/Pdk.Sample.Platform.csproj
```

The same operation can be invoked directly for troubleshooting or CI:

```shell
pdk generate platform <path-to-platform.dll> <path-to-platform-project-directory>
```

## Database Migrations

Each service's Core project contains a `psCommands/` folder with PowerShell
scripts for managing Entity Framework Core migrations. These scripts resolve
project paths automatically — run them from any directory.

### Add a migration

```shell
# Prompts for migration name
.\psCommands\add-migration.ps1

# Pass name directly
.\psCommands\add-migration.ps1 "AddCustomerTable"
```

If no `Migrations/` folder exists yet, the first migration is automatically
named `InitialCreation`.

### Apply migrations

```shell
# Prompts for migration name
.\psCommands\update-database.ps1

# Pass name directly
.\psCommands\update-database.ps1 "AddCustomerTable"
```

### Remove the last migration

```shell
.\psCommands\remove-migration.ps1
```

All scripts are auto-generated per service with the correct `DbContext` name
and project paths.

## Current Preview Limitations

- Infrastructure choices (PostgreSQL, RabbitMQ, Redis, OpenTelemetry) are metadata
  only — complete runtime integration is not yet generated.
- Angular and PrimeNG UI generation are planned but not implemented.
- Commands such as `pdk entity` are incomplete.

Use the [CLI Guide](pdk-cli-guide.md) for command details and the
[architecture reference](architecture/README.md) for PDK's intended direction.
