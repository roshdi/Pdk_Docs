# PDK Entity Feature and Capability Guide

PDK entities are structural metadata declared inside a `ServiceDefinition`.
Profiles and capability interfaces describe reusable behavior that generators
can apply consistently across entities.

> [!NOTE]
> Definitions, service/entity generation, and API generation are implemented in
> preview. Contracts and provider coverage are still evolving; see
> [Current implementation status](implementation-status.md).

## Entity Model

The declaration hierarchy is:

```text
PlatformDefinition
`-- ServiceDefinition
    |-- EntityDefinition
    |   |-- Table mapping
    |   |-- Entity profile
    |   |-- Explicit interfaces
    |   `-- Security and permissions
    |-- Service-level overrides
    `-- Dependencies
`-- FeatureProfile per entity
    |-- Commands (Create, Update, Delete)
    |-- Queries (GetItem/GetById, GetList, Lookup)
    `-- Navigation configuration
```

## Capability Interfaces

`Pdk.Core.Abstractions` provides capability contracts for creation,
modification, soft deletion, and optimistic concurrency:

- `ICreated`
- `IModified`
- `ISoftDelete`
- `IOptimisticConcurrency`

Applications define the properties and implementation used by generated entity
types. For example:

```csharp
using Pdk.Core.Abstractions;

public interface ICreatedEntity : ICreated
{
    DateTime CreatedOn { get; set; }

    void ICreated.SetCreated(IRequestContext context)
    {
        CreatedOn = DateTime.UtcNow;
    }
}

public interface ISoftDeleteEntity : ISoftDelete
{
    bool IsDeleted { get; set; }

    void ISoftDelete.SetDeleted(IRequestContext context)
    {
        IsDeleted = true;
    }

    void ISoftDelete.SetUndeleted(IRequestContext context)
    {
        IsDeleted = false;
    }
}
```

Production applications should obtain time, identity, and tenant information
from `IRequestContext` services rather than directly using static system APIs.

## Reusable Profiles

An `EntityProfileDefinition` groups interfaces that are commonly applied
together:

```csharp
using Pdk.Definitions.Entity;

public static class EntityProfiles
{
    public static readonly EntityProfileDefinition Audited =
        new EntityProfileDefinition()
            .Implements<ICreatedEntity>();

    public static readonly EntityProfileDefinition Aggregate =
        new EntityProfileDefinition()
            .Implements<
                ICreatedEntity,
                ISoftDeleteEntity>();
}
```

Profiles are application-defined shared metadata. Configure static profiles once
and treat them as immutable after startup.

## Optimistic Concurrency

PDK supports optimistic concurrency through the `IOptimisticConcurrency`
capability. Any interface that extends `IOptimisticConcurrency` is detected
automatically by the code generator — regardless of its name or the name of
the concurrency property.

```csharp
using Pdk.Core.Abstractions;

public interface IConcurrentEntity : IOptimisticConcurrency
{
    string Version { get; set; }

    void IOptimisticConcurrency.SetConcurrency(IRequestContext context)
    {
        // Version is managed by the database provider, not set manually.
    }
}
```

`IConcurrentEntity` is the default generated example. Developers can define
their own interfaces with any name and any property:

```csharp
public interface IOrderConcurrency : IOptimisticConcurrency
{
    string RowVersion { get; set; }
}
```

The generator detects any interface implementing `IOptimisticConcurrency`,
finds the first settable property on it, and emits:

```csharp
builder.HasRowVersion(e => e.PropertyName);
```

### Provider Mapping

Each provider maps the native concurrency type to the property type
(typically `string`) automatically:

| Database | Native Type | Mechanism |
| --- | --- | --- |
| SQL Server | `byte[]` | `rowversion` column via `IsRowVersion()` |
| PostgreSQL | `uint` | `xmin` system column via `IsRowVersion()` |
| Oracle | `long` | Trigger-incremented `NUMBER` via `IsRowVersion()` on `long` |

### Generated Code

When an entity implements any `IOptimisticConcurrency`-derived interface, the
generated entity type configuration automatically emits:

```csharp
builder.HasRowVersion(e => e.PropertyName);
```

where `PropertyName` is the first settable property declared on the interface.

Each `Pdk.Data.*` project provides:

- A `ValueConverter` that converts between the property type and the
  provider's native concurrency type.
- A `HasRowVersion<TEntity>()` extension method on `EntityTypeBuilder<TEntity>`
  that applies the converter automatically.

Services reference the appropriate `Pdk.Data.*` package for their database
provider. The generated code calls `HasRowVersion` with the same API regardless
of provider; the correct extension method resolves based on the referenced
package.

### Example

Declare a custom concurrency interface:

```csharp
public interface IOrderConcurrency : IOptimisticConcurrency
{
    string RowVersion { get; set; }
}
```

Register an entity with this interface:

```csharp
entities.Entity("Order")
    .Table("Orders")
    .Implements<IOrderConcurrency>();
```

The generated entity implements `IOrderConcurrency` with a `string RowVersion`
property. The generated entity type configuration includes:

```csharp
public override void Configure(EntityTypeBuilder<OrderEntity> builder)
{
    builder.ToTable("Orders", schemaName);
    ConfigureEntity(builder);
    builder.HasRowVersion(e => e.RowVersion);
}
```

When saving changes, EF Core throws `DbUpdateConcurrencyException` if the
`RowVersion` value has changed since the entity was loaded.

The default `IConcurrentEntity` with `string Version` works the same way:

```csharp
entities.Entity("Customer")
    .Table("Customers")
    .Implements<IConcurrentEntity>();

// Generated: builder.HasRowVersion(e => e.Version);
```

## Capability Configuration

Capability interfaces can define EF Core property configuration through an
optional static `Configure` method. The code generator detects this method and
emits a call in the generated entity type configuration.

### Convention

```csharp
static void Configure<TEntity>(EntityTypeBuilder<TEntity> builder)
    where TEntity : class, IMyCapability
```

- **Name**: `Configure`
- **Signature**: generic, takes `EntityTypeBuilder<TEntity>`, constrained to
  `where TEntity : class, I{CapabilityName}`
- **Optional**: only interfaces that need custom configuration implement it

### Example

The default `ICreatedEntity` declares properties but does not configure string
lengths. A developer can add configuration:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pdk.Core.Abstractions;

public interface ICreatedEntity : ICreated
{
    DateTime CreatedDate { get; set; }
    [AuthId] AuthId CreatedBy { get; set; }
    string CreatedByUserName { get; set; }

    void ICreated.SetCreated(IRequestContext context)
    {
        CreatedDate = context.Clock.Now;
        CreatedBy = context.Auth.GetRequiredUserId();
        CreatedByUserName = context.Auth.UserName
            ?? throw new UnauthorizedAccessException();
    }

    static void Configure<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ICreatedEntity
    {
        builder.Property(e => e.CreatedByUserName).HasMaxLength(50);
    }
}
```

The generated entity type configuration calls this method automatically:

```csharp
public override void Configure(EntityTypeBuilder<OrderEntity> builder)
{
    builder.ToTable(tableName, schemaName);
    ConfigureEntity(builder);
    ICreatedEntity.Configure(builder);
}
```

No manual partial class configuration is needed for standard capabilities.

### Automatic vs Manual Configuration

| Behavior | Trigger | Developer Action |
| --- | --- | --- |
| `HasRowVersion` | Any `IOptimisticConcurrency` interface | None — just implement the interface |
| `Configure` method call | Static `Configure` method on interface | Write the method if property config is needed |
| Manual `ConfigureEntity` | Non-generated partial class | Hand-write in `EntityTypeConfiguration.cs` |

`HasRowVersion` is **automatic** — the generator always emits it for concurrency
interfaces. The `Configure` method is **optional** — only written when the
capability needs property-level configuration like max length, precision, or
default values.

### Multiple Capabilities

An entity can implement multiple capabilities, each with its own `Configure`
method. All are called in the generated configuration:

```csharp
public override void Configure(EntityTypeBuilder<OrderEntity> builder)
{
    builder.ToTable(tableName, schemaName);
    ConfigureEntity(builder);
    builder.HasRowVersion(e => e.RowVersion);  // automatic
    IOrderConcurrency.Configure(builder);       // from Configure method
    ICreatedEntity.Configure(builder);          // from Configure method
}
```

## Register Entities

Set a default profile for a service and override it where needed:

```csharp
using Pdk.Definitions.Service;

public sealed class CatalogService : ServiceDefinition
{
    protected override void Configure()
    {
        Entities(entities =>
        {
            // Default profile for all entities in this service
            entities.DefaultProfile(EntityProfiles.Audited);

            // Simple entity — uses default Audited profile
            entities.Entity("ProductCategory")
                .Table("ProductCategories");

            // Entity with concurrency — override profile
            entities.Entity("Product")
                .Table("Products")
                .Profile(EntityProfiles.ConcurrentHistory);

            entities.Entity("ProductVariant")
                .Table("ProductVariants")
                .Profile(EntityProfiles.ConcurrentHistory);

            // Simple entity — uses default Audited profile
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
            // Default profile for all entities — full audit + concurrency
            entities.DefaultProfile(EntityProfiles.ConcurrentHistory);

            entities.Entity("Customer")
                .Table("Customers");

            entities.Entity("Order")
                .Table("Orders");

            entities.Entity("OrderItem")
                .Table("OrderItems");

            // Child entity — only audit, no concurrency
            entities.Entity("OrderItemDetail")
                .Table("OrderItemDetails")
                .Profile(EntityProfiles.Audited);
        });
    }
}
```

Use:

- `DefaultProfile(...)` for entities subsequently added by the builder.
- `Profile(...)` to select a profile for one entity.
- `Implements<T>()` to add explicit interfaces.
- `OverrideImplements<T>()` to replace explicitly configured interfaces.
- `Table(...)` to set the persistence table name.

Duplicate entity names are compared case-insensitively and produce diagnostic
`PDK201`.

### Service-Level DSL

A `ServiceDefinition` provides additional DSL methods for service-wide
configuration:

```csharp
public sealed class OrderService : ServiceDefinition
{
    protected override void Configure()
    {
        // Override the platform's default database provider for this service
        Database(DatabaseProvider.PostgreSql);

        // Declare dependencies on other services
        Dependencies(d =>
        {
            d.Add<IdentityService>();
            d.Add<CatalogService>();
        });

        // Configure service-level security
        Security(s =>
        {
            s.RequireAuthentication();
            s.EnableRowLevelSecurity();
            s.UseIdentityService<IdentityService>();
        });

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

| DSL Method | Purpose |
| --- | --- |
| `Database(provider)` | Override the platform default database provider for this service |
| `Dependencies(d => ...)` | Declare service-to-service dependencies |
| `Security(s => ...)` | Configure authentication, row-level security, and identity service |

## DbContext Generation

PDK generates a `{ServiceName}DbContext` in the service's Core project. The
context extends `DbContextBase` from `Pdk.Core.Data` and includes `DbSet` properties
for each entity.

### Generated Files

| File | Purpose |
| --- | --- |
| `{ServiceName}DbContext.g.cs` | Auto-generated: constructor, `OnModelCreating`, `DbSet` properties |
| `{ServiceName}DbContext.cs` | Hash-baselined customization scaffold (created once, never overwritten) |

The scaffold's first-line PDK marker records a SHA-256 baseline. Editing its body
does not cause regeneration to overwrite it and identifies it as developer-modified.

### Generated DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using Pdk.Core.Data;
using Pdk.Sample.Order.Core.Entities;
using Pdk.Sample.Order.Core.EntityConfigurations;

namespace Pdk.Sample.Order.Core;

public partial class OrderDbContext : DbContextBase
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options)
        : base(options)
    {
        DefaultSchema = "Order";
        MoreOptions(options);
    }

    partial void MoreOptions(DbContextOptions options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new CustomerEntityTypeConfiguration(DefaultSchema));
        modelBuilder.ApplyConfiguration(new OrderEntityTypeConfiguration(DefaultSchema));
        modelBuilder.ApplyConfiguration(new OrderItemEntityTypeConfiguration(DefaultSchema));
        modelBuilder.ApplyConfiguration(new OrderItemDetailEntityTypeConfiguration(DefaultSchema));
        MoreOnModelCreating(modelBuilder);
    }

    partial void MoreOnModelCreating(ModelBuilder modelBuilder);

    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItemEntity> OrderItems => Set<OrderItemEntity>();
    public DbSet<OrderItemDetailEntity> OrderItemDetails => Set<OrderItemDetailEntity>();
}
```

### Extension Points

| Partial Method | Purpose |
| --- | --- |
| `MoreOptions(DbContextOptions options)` | Configure provider-specific options or change `DefaultSchema` |
| `MoreOnModelCreating(ModelBuilder modelBuilder)` | Add indexes, constraints, seed data, or additional configuration |

The `DefaultSchema` property defaults to the service name. Change it in
`MoreOptions` if needed:

```csharp
public partial class OrderDbContext
{
    partial void MoreOptions(DbContextOptions options)
    {
        DefaultSchema = "sales_order";
    }
}
```

### DbSet Naming

`DbSet` property names are pluralized from the entity name:
`Customer` → `Customers`, `Order` → `Orders`, `OrderItem` → `OrderItems`,
`OrderItemDetail` → `OrderItemDetails`.

### Multiple Entities

Each entity registered in the service definition gets:
- A `DbSet<TEntity>` property on the DbContext
- An `ApplyConfiguration` call in `OnModelCreating`
- Its own `EntityTypeConfiguration` class

## Entity Security

Security capabilities implement `ISecurity` and declare supported permissions and
scopes. An entity may compose multiple capabilities; an entity with none is
anonymous. Every capability includes `None` and `All`, while each additional scope
must provide a convention-based `[SecurityFilter]`:

```csharp
using Pdk.Core.Security;

public interface IBasicSecurity : ISecurity
{
    static void ISecurity.ConfigurePermissions(PermissionBuilder permissions)
    {
        permissions.Read();
        permissions.Create().ImpliesRead();
        permissions.Update().ImpliesRead();
        permissions.Delete().ImpliesRead();
    }

    static SecurityScopeDefinition[] ISecurity.GetAvailableScopes() =>
    [
        SecurityScopes.None,
        SecurityScopes.All
    ];
}
```

Associate the capability and configure entity-specific permission groups:

```csharp
entities.Entity("Customer")
    .HasSecurity<IBasicSecurity>(permissions =>
    {
        var standard = permissions.Default();
        standard.Read();
        standard.Create().ImpliesRead();
        standard.Update().ImpliesRead();
        standard.Delete().ImpliesRead();

        permissions.Group("Workflow")
            .Add("Approve")
            .ImpliesRead();
    });
```

Capability permissions describe what a security model supports. Entity permission
groups describe the permissions generated for a specific entity. Implication carries
both derived access and dependency: if Update implies Read, denying Read invalidates
an Update root, but an independently granted Read root survives a deny of Update.
Roles grant permission-and-scope pairs; scope data and denies belong to users.
Multiple effective scopes for one permission are OR-composed.

See the [Security and Authorization Developer Guide](security-capability-guide.md)
for development workflows and the
[authoritative security model](architecture/security-model.md) for invariants.

## Feature Profile DSL

A `FeatureProfile<TEntity>` defines which commands and queries are generated
for an entity and controls which fields are included in each operation.

Each entity gets a generated feature profile in the Core project's `Features/`
folder (named `FeatureProfiles/`). Developers customize these profiles to control
the generated API surface.

### Profile Structure

```csharp
using Pdk.Definitions.Features;

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

Each `Has*` method returns a builder for fluent configuration.

### Available Commands and Queries

| Method | Return Type | Description |
| --- | --- | --- |
| `HasCreateCommand(name?)` | `CommandBuilder<TEntity>` | Generates a create command |
| `HasUpdateCommand(name?)` | `CommandBuilder<TEntity>` | Generates an update command; uses the primary key by default and supports `MatchBy` |
| `HasDeleteCommand(name?)` | `CommandBuilder<TEntity>` | Generates a delete command |
| `HasUpsertCommand(name?)` | `CommandBuilder<TEntity>` | Generates a create-or-update command using explicit match fields |
| `HasBulkUpdateCommand(name)` | `CommandBuilder<TEntity>` | Generates a transactional bulk update using the primary key or `MatchBy` fields |
| `HasBulkDeleteCommand(name?)` | `CommandBuilder<TEntity>` | Generates a transactional bulk delete using the primary key or `MatchBy` fields |
| `HasBulkInsertCommand(name?)` | `CommandBuilder<TEntity>` | Generates a transactional bulk insert with per-item validation |
| `HasBulkUpsertCommand(name?)` | `CommandBuilder<TEntity>` | Generates a transactional bulk upsert using required `MatchBy` fields |
| `HasImportFromExcelCommand(name?)` | `CommandBuilder<TEntity>` | Generates an `.xlsx` import endpoint and editable mapping profile |
| `HasGetItemQuery(name?)` | `QueryBuilder<TEntity>` | Generates a single-item query; defaults to the primary key |
| `HasGetByIdQuery(name?)` | `QueryBuilder<TEntity>` | Compatibility alias for the primary-key item query |
| `HasGetListQuery(name?)` | `QueryBuilder<TEntity>` | Generates a list query; `MatchBy` can add required equality filters |
| `HasLookupQuery(name?)` | `QueryBuilder<TEntity>` | Generates a lookup query |

All methods accept an optional `name` parameter to customize the generated
command/query name. By default, the entity name is appended automatically
(e.g., `HasCreateCommand()` for `OrderEntity` produces `CreateOrder`).

### Update Commands and Match Fields

Updates locate the existing row by primary key unless a unique business key is
declared explicitly:

```csharp
HasUpdateCommand("UpdateByCode")
    .MatchBy(product => product.Code);

HasUpdateCommand("UpdateWarehouseProduct")
    .MatchBy(product => product.WarehouseId, product => product.Code);
```

PDK includes custom match properties in the generated command, omits unrelated
primary-key properties, and generates an `AND` predicate for composite matches.
Concurrency fields remain available for optimistic concurrency. The lookup is
performed against the security-filtered query. No match returns `404`; more than
one match produces an integrity error explaining that the fields require a unique
database key or index.

Update match fields identify the current row and are excluded from generated entity
mapping. To change a unique business key, create an explicit command with separate
lookup and replacement properties such as `ExistingCode` and `NewCode`.

An unmapped, computed, or concurrency update match produces `PDK404` at the DSL
source line. `MatchBy` is rejected for create and delete commands.

### Upsert Commands

An upsert must explicitly identify the mapped field or field combination that
uniquely identifies an existing row:

```csharp
HasUpsertCommand("Synchronize")
    .MatchBy(product => product.Code);

HasUpsertCommand("SynchronizeWarehouseProduct")
    .MatchBy(product => product.WarehouseId, product => product.Code);
```

The generated handler updates the matching row or creates a new row when none is
found. It returns `200 OK` for an update, `201 Created` for an insert, and exposes
`WasCreated` in the result. The lookup and save run in one transaction. The
database must have a unique key or index over the `MatchBy` fields to protect
against competing inserts.

Omitting `MatchBy` produces `PDK400`. An unmapped, computed, or concurrency match
field produces `PDK401`. Diagnostics include the feature-profile source file and
line.

### Bulk Update Commands

For the complete request shapes, validation policies, concurrency behavior, and
result contract, see [Bulk Update and Delete Commands](bulk-commands.md).

A bulk update uses the primary key by default. `SharedUpdate` generates the
shared update fields directly on the command and applies them to every matched
row:

```csharp
HasBulkUpdateCommand("Activate")
    .SharedUpdate(barcode => barcode.IsActive)
    .ItemValidator<ActivateProductBarcodeItemValidator>()
    .OnValidationFailure(BulkFailureBehavior.SkipInvalidItems)
    .MaximumBatchSize(5000);
```

For a single primary key, the generated request contains an ID array followed
by the shared fields. For example, the command above accepts
`{ "productBarcodeIds": [1, 2], "isActive": true }`. Composite business keys
use a `matches` array alongside the same flattened shared fields:

```csharp
HasBulkUpdateCommand("Assign")
    .MatchBy(item => new { item.WarehouseId, item.Code })
    .SharedUpdate(item => new { item.OwnerId, item.IsActive });
```

Add `WithConcurrency()` when the entity implements `IOptimisticConcurrency`:

```csharp
HasBulkUpdateCommand("Activate")
    .WithConcurrency()
    .SharedUpdate(barcode => new { barcode.IsActive, barcode.PartnerId })
    .MaximumBatchSize(5000);
```

Concurrency generates a match object for every row, even when selection uses a
single primary key. The key or `MatchBy` fields identify the row; its concurrency
token verifies the submitted version. A missing or stale row rejects the entire
batch with HTTP 409 before any mutation.

Use `PerItemUpdate` when each row has different update values:

```csharp
HasBulkUpdateCommand("Reclassify")
    .MatchBy(item => new { item.WarehouseId, item.Code })
    .PerItemUpdate(item => new { item.CategoryId, item.IsActive });
```

Per-item requests contain an `items` array with match and update fields. Duplicate
match keys are rejected. The handler loads all security-visible entities in one
query, invokes modification capabilities, and calls `SaveChangesAsync` once. The
result reports requested, matched, updated, and not-found counts. Developers can
override `CustomizeUpdateAsync` in the non-generated partial handler for operation
rules that cannot be expressed as field mapping.

Use a FluentValidation item validator for rules that must be evaluated separately
for every matched entity:

```csharp
public sealed class ActivateProductBarcodeItemValidator
    : AbstractValidator<BulkItemValidationContext<ProductBarcodeEntity,
        ActivateProductBarcodeCommand>>
{
    public ActivateProductBarcodeItemValidator()
    {
        RuleFor(row => row.Entity.IsActive)
            .Equal(false)
            .WithErrorCode("AlreadyActive");
    }
}
```

`RollbackBatch` (the default) marks invalid rows as failed, valid rows as not
processed, and saves nothing. `SkipInvalidItems` marks invalid rows as skipped and
saves valid rows once. Each row result preserves FluentValidation property names,
messages, codes, attempted values, severity, and custom state.

### Bulk Delete Commands

For the complete bulk-delete contract, see
[Bulk Update and Delete Commands](bulk-commands.md#bulk-delete).

Bulk delete uses the primary key by default:

```csharp
HasBulkDeleteCommand()
    .MaximumBatchSize(5000);
```

Use a named operation and `MatchBy` for one or more business-key columns:

```csharp
HasBulkDeleteCommand("RemoveByWarehouseCode")
    .MatchBy(item => new { item.WarehouseId, item.Code })
    .WithConcurrency();
```

`WithConcurrency()` is available for bulk update and bulk delete. Generation
fails at the DSL source line unless the entity implements
`IOptimisticConcurrency` and exposes exactly one mapped concurrency token.

The generated endpoint uses POST (`DeleteBulk` by default) so its JSON body is
portable across clients and proxies. The handler applies permission and row-level
security filters, loads matching entities once, invokes `ISoftDelete` when the
entity supports it, otherwise performs hard deletes, and saves once. Override
`CustomizeDeleteAsync` in the partial handler for additional domain rules.

### Item Queries and Match Fields

`HasGetItemQuery()` describes query cardinality rather than a specific identifier.
Without additional configuration, it uses the entity primary key and preserves the
conventional `GetById...` artifact names and `/{id}` route:

```csharp
HasGetItemQuery();
```

Use `MatchBy` and an operation name for another unique lookup:

```csharp
HasGetItemQuery("GetByCode")
    .MatchBy(product => product.Code);

HasGetItemQuery("GetByWarehouseAndCode")
    .MatchBy(product => product.WarehouseId, product => product.Code);
```

Custom item routes include the operation name to avoid ambiguous routes, for
example `GetByCode/{code}`. PDK generates the matching request properties,
validation rules, OpenAPI parameters, and handler predicate. The selected fields
should have a unique database key or index because an item query expects at most
one logical result.

An entity with neither a primary key nor explicit match fields produces `PDK402`.
An invalid or unmapped item match field produces `PDK403`, including its source
file and line. `HasGetByIdQuery()` remains available as a compatibility alias.

### Required List Filters

Use `MatchBy` on a list query when the operation must always be constrained by
one or more entity fields:

```csharp
HasGetListQuery("ByProduct")
    .MatchBy(barcode => barcode.ProductId);

HasGetListQuery("ByWarehouseAndProduct")
    .MatchBy(barcode => barcode.WarehouseId, barcode => barcode.ProductId);
```

PDK adds strongly typed match properties to the generated request, validates
them, and applies an `AND` equality predicate to the entity query before
security, projection, QueryKit filters, sorting, and pagination. A named list
query generates one POST endpoint whose route is the supplied operation name;
the properties are JSON request fields. Use ordinary `ClientFilters` for
optional filters; list `MatchBy` values are required operation inputs.

The unnamed `HasGetListQuery()` retains the conventional pair of endpoints:
`GET` at the empty group route and `POST` at `Search`.

### Generating Internal Operations Without Endpoints

Commands and queries can generate their contracts, validators, handlers, and
mappers without exposing an HTTP endpoint:

```csharp
HasUpsertCommand("Import")
    .MatchBy(product => product.Code)
    .WithoutEndpoint();

HasGetListQuery("Export")
    .WithoutEndpoint();
```

For list queries, `WithoutEndpoint()` suppresses both the main endpoint and the
generated search endpoint. On regeneration, stale generated endpoints and
untouched hash-baselined endpoint scaffolds are removed. Modified endpoint
scaffolds are preserved and reported for review.

### CommandBuilder and QueryBuilder API

Both builders provide the same fluent API for field selection:

#### Query source entities

Use `From<TSource>()` when an operation belongs to one feature but must read
from another mapped EF entity, such as a database view:

```csharp
public partial class ProductGroupFeatureProfile
{
    public override void Configure()
    {
        HasGetListQuery(); // reads ProductGroupEntity

        HasLookupQuery()
            .From<VProductGroupEntity>(); // reads wms.vProductGroup
    }
}
```

The owning feature remains responsible for artifact names, folders, endpoint
groups, routes, and permissions. In this example PDK still generates
`GetLookupProductGroupQuery` under `Features/ProductGroup` at
`/api/Catalog/ProductGroups/GetLookup`, while the handler and mapper consume
`VProductGroupEntity`. Query fields, default lookup name fields, primary keys,
filtering, sorting, and security filtering are derived from the source entity.

The source type must be part of the EF model. Prefer a separate view entity or
shared interface over EF entity inheritance when a table and view represent
different database objects.

#### Unmapped projection sources (`FromProjection`)

Use `FromProjection<TSource>()` for list queries whose read model is a flat,
unmapped LINQ result, for example a table join or aggregate replacing a view:

```csharp
HasGetListQuery("Browse")
    .FromProjection<ProductListItem>()
    .IncludeAll();
```

PDK generates the query contract, validator, mapper, handler, and endpoint under
the owning feature. Implement the generated `CreateProjectionQuery` partial
method in the developer-owned handler file; do not edit `.g.cs` files. No EF
mapping or primary key is required for `ProductListItem`. This method currently
supports **list queries only**, not item or lookup queries.

See [Projection-backed list queries](projection-queries.md) for an end-to-end
example, field selection, authorization, filtering, paging, and limitations.

#### Exclude

Remove specific fields from the generated command or query:

```csharp
HasCreateCommand()
    .Exclude(e => e.Id, e => e.Version);

HasUpdateCommand()
    .Exclude("Id", "CreatedOn");
```

#### Include

Add fields back that were excluded by default rules:

```csharp
HasGetListQuery()
    .Include(e => e.CreatedOn);
```

#### IncludeAll

Override all default rules and include every entity property:

```csharp
HasGetItemQuery()
    .IncludeAll();
```

#### IncludeOnly

Restrict the output to only the specified fields (replaces all defaults):

```csharp
HasLookupQuery()
    .IncludeOnly(e => e.Id, e => e.Name);
```

#### Custom Command Name

Override the generated command name:

```csharp
HasCreateCommand("Register");
// Generates RegisterCustomer instead of CreateCustomer
```

#### Custom Swagger Summary

Override the default Swagger/OpenAPI summary and description for an endpoint:

```csharp
HasCreateCommand()
    .Summary(s =>
    {
        s.Summary = "Creates a new Product record.";
        s.Description = "Creates a new Product record in the catalog.";
    });
```

Both `Summary` and `Description` default to the auto-generated text if not set.
Queries support the same DSL:

```csharp
HasGetItemQuery()
    .Summary(s =>
    {
        s.Summary = "Fetch a product by its unique identifier.";
        s.Description = "Retrieves a single Product including category information.";
    });
```

### Navigation Properties

Control how navigation properties are included in commands and queries. PDK
supports two types of navigations:

- **Scalar navigations** (many-to-one references) — use `IncludeNavigation`
- **Collection navigations** (one-to-many children) — use `IncludeDetail`

#### Single-Level Navigation

Include a reference entity on a command:

```csharp
HasCreateCommand()
    .IncludeNavigation(o => o.Customer);
```

When the navigation DTO is input for a nested command rather than a new entity graph,
disable generated entity mapping while retaining the DTO in the command contract:

```csharp
HasCreateCommand()
    .IncludeNavigation(
        o => o.Customer,
        customer => customer.IncludeAll(),
        mapToEntity: false);
```

This emits `CustomerDto` but prevents Mapperly from assigning it to
`OrderEntity.Customer`. See [Nested Command Orchestration](nested-command-orchestration.md)
for safe create/update handler execution and transaction ownership.

Include a child collection:

```csharp
HasCreateCommand()
    .IncludeDetail(o => o.OrderItems, b =>
    {
        b.Exclude(nameof(OrderItemEntity.Sku),
                   nameof(OrderItemEntity.TotalPrice));
    });
```

Exclude a navigation entirely:

```csharp
HasGetItemQuery()
    .ExcludeNavigation(o => o.Customer);
```

#### Multi-Level Master-Detail

Nest details within details using the `NavigationBuilder<T>` callback:

```csharp
HasGetItemQuery()
    .IncludeNavigation(o => o.Customer)
    .IncludeDetail(o => o.OrderItems, b =>
    {
        // Second level: load OrderItemDetails within each OrderItem
        b.IncludeDetail(oi => oi.OrderItemDetails);
    });
```

This produces a three-level hierarchy:

```
Customer (scalar navigation)
Order → OrderItems (collection) → OrderItemDetails (nested collection)
```

The same pattern supports deeper nesting:

```csharp
HasGetItemQuery()
    .IncludeDetail(o => o.OrderItems, b =>
    {
        b.IncludeDetail(oi => oi.OrderItemDetails, d =>
        {
            // Third level nesting
            d.IncludeDetail(detail => detail.SubItems);
        });
    });
```

#### NavigationBuilder API

| Method | Purpose |
| --- | --- |
| `IncludeNavigation(expr, builder?, mapToEntity = true)` | Include a scalar navigation; set `mapToEntity: false` for nested-command input |
| `IncludeDetail(expr, builder?)` | Include a collection navigation (one-to-many children) |
| `ExcludeNavigation(expr)` | Exclude a navigation from the operation |

Inside the `NavigationBuilder<T>` callback:

| Method | Description |
| --- | --- |
| `IncludeDetail(expr, builder?)` | Nest a child collection within the detail |
| `IncludeNavigation(expr)` | Include a scalar reference within the detail |
| `Exclude(props...)` | Exclude specific fields from the detail |
| `IncludeOnly(props...)` | Include only specific fields |

### Default Field Rules

Commands and queries apply automatic field filtering based on the entity's
capability properties:

| Operation | Auto-Excluded | Auto-Included |
| --- | --- | --- |
| `HasCreateCommand()` | Primary key, concurrency token, `IsCreatedProperty`, `IsModifiedProperty`, `IsSoftDeleteProperty` | All remaining |
| `HasUpdateCommand()` | `IsCreatedProperty`, `IsModifiedProperty`, `IsSoftDeleteProperty`; also primary keys when custom `MatchBy` fields are configured | All remaining, including match and concurrency properties |
| `HasDeleteCommand()` | Everything except primary key and concurrency token | — |
| `HasUpsertCommand()` | Primary key, concurrency token, audit, soft-delete, and security properties unless selected for matching | All remaining |
| `HasGetItemQuery()` | `IsCreatedProperty`, `IsModifiedProperty`, `IsSoftDeleteProperty`, security properties | All remaining |
| `HasGetListQuery()` | `IsCreatedProperty`, `IsModifiedProperty`, `IsSoftDeleteProperty` | All remaining |
| `HasLookupQuery()` | `IsCreatedProperty`, `IsModifiedProperty`, `IsSoftDeleteProperty` | All remaining |

The `IsCreatedProperty`, `IsModifiedProperty`, and `IsSoftDeleteProperty`
flags are detected automatically based on the entity's capability interface
implementation and property naming conventions (`CreatedOn`, `ModifiedOn`,
`DeletedOn`, `IsDeleted`, etc.).

Use `IncludeAll()` to override these defaults and include every field.

### Execution Order

Field selection is applied in this order:

1. Default rules (type-based) are applied
2. `IncludeAll()` overrides all defaults if called
3. `Exclude(...)` removes fields from the result
4. `Include(...)` adds fields back that were excluded
5. `IncludeOnly(...)` replaces the entire result with only the specified fields

## Register the Service

Services belong to a platform and can override platform defaults:

```csharp
public sealed class SamplePlatform : PlatformDefinition
{
    public override void Configure()
    {
        Platform(platform => platform
            .Name("Sample")
            .RootNamespace("Pdk.Sample")
            .DisplayName("Sample Platform"));

        Infrastructure(infra => infra
            .DefaultDatabase(DatabaseProvider.SqlServer)
            .MessageBroker(MessageBroker.RabbitMq)
            .Cache(CacheProvider.Redis)
            .OpenTelemetry());

        Services(services =>
        {
            services.Add<IdentityService>();
            services.Add<NotificationService>();
            services.Add<CatalogService>();
            services.Add<OrderService>();
        });
    }
}
```

A service definition can override the platform's database provider, declare
dependencies, and configure security:

```csharp
public sealed class OrderService : ServiceDefinition
{
    protected override void Configure()
    {
        // Override the platform's default database
        Database(DatabaseProvider.PostgreSql);

        // Declare dependencies on other services
        Dependencies(d =>
        {
            d.Add<IdentityService>();
        });

        // Configure service-level security
        Security(s =>
        {
            s.RequireAuthentication();
            s.EnableRowLevelSecurity();
            s.UseIdentityService<IdentityService>();
        });

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

Registering the same service type twice produces diagnostic `PDK101`.
