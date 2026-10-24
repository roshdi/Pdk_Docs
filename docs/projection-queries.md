# Projection-backed list queries

`FromProjection<TSource>()` lets a feature's list query read an unmapped LINQ
result instead of a mapped EF entity or database view. It is useful for joined
grids, calculated columns, aggregates, and replacing read-only views without
handwriting an entire query endpoint.

## Choose the source

| Definition | Read source | Developer responsibility |
| --- | --- | --- |
| `HasGetListQuery()` | The owning mapped entity | Optional query customization |
| `.From<TSource>()` | Another mapped EF entity, including a view | Register and configure that source in the EF model |
| `.FromProjection<TSource>()` | A flat, unmapped result type | Implement the generated `CreateProjectionQuery` partial method |

All three keep the owning feature's artifact names, folders, endpoint group,
and permission identity. Changing the read source does not change the write
entity or create a table for the projection.

## Define the read model and feature

Place the read model in a namespace accessible to the feature profile and the
generated handler (normally the service's Core `Entities` namespace):

```csharp
namespace Sample.Catalog.Core.Entities;

public sealed class ProductListItem
{
    public long ProductId { get; set; }
    public string Title { get; set; } = "";
    public string? CategoryTitle { get; set; }
    public decimal Price { get; set; }
}
```

In the existing `ProductEntityFeatureProfile.Configure()`:

```csharp
HasGetListQuery("Browse")
    .FromProjection<ProductListItem>()
    .IncludeAll();
```

Run the usual API generation workflow described in the
[CLI guide](pdk-cli-guide.md). For a Catalog service and Product feature, this
declaration generates `BrowseProductQuery`, its request, validator, mapper,
handler, and a **POST `/api/Catalog/Products/Browse`** endpoint. The response
uses the standard `IPaginationData<BrowseProductQuery>` contract.

Named list queries generate one POST endpoint using the operation name.
The default unnamed list still generates GET on the entity collection and
POST `Search`. `FromProjection` does **not** imply `WithoutEndpoint()`;
append that option only for internal-only queries.

## Implement the query in a companion partial

Generation emits this contract in `BrowseProductQueryHandler.g.cs`:

```csharp
protected override IQueryable<ProductListItem> CreateQuery(BrowseProductQueryRequest request)
    => CreateProjectionQuery(request);

private partial IQueryable<ProductListItem> CreateProjectionQuery(BrowseProductQueryRequest request);
```

Implement it in `BrowseProductQueryHandler.cs`, in the same namespace and
partial class. The example assumes existing mapped `ProductEntity` and
`CategoryEntity` types with the shown properties:

```csharp
using Microsoft.EntityFrameworkCore;
using Sample.Catalog.Core.Entities;

namespace Sample.Catalog.Api.Features.Product;

public partial class BrowseProductQueryHandler
{
    private partial IQueryable<ProductListItem> CreateProjectionQuery(
        BrowseProductQueryRequest request)
    {
        return from product in dbContext.Set<ProductEntity>().AsNoTracking()
               join category in dbContext.Set<CategoryEntity>().AsNoTracking()
                   on product.CategoryId equals category.CategoryId into categories
               from category in categories.DefaultIfEmpty()
               select new ProductListItem {
                   ProductId = product.ProductId,
                   Title = product.Title,
                   CategoryTitle = category == null ? null : category.Title,
                   Price = product.Price
               };
    }
}
```

The explicit private partial method has no default implementation: compilation
fails until you supply it. Do not add another `CreateQuery` override alongside
the generated one, and do not edit the generated file.

Return an EF-backed `IQueryable` without materializing it. Avoid `ToList`,
`AsEnumerable`, or per-row database calls in this method; later filtering,
sorting, counting, and paging should remain SQL-translatable. `AsNoTracking`
belongs on the mapped source queries, not on a fictitious
`dbContext.Set<ProductListItem>()`.

The projection needs no `DbSet`, EF key, table, view, or service entity
declaration. Keep it out of EF's model; use `modelBuilder.Ignore<ProductListItem>()`
if an application convention would otherwise discover it.

## What PDK still handles

The normal list handler pipeline remains in place:

1. Create the source with `CreateProjectionQuery`.
2. Apply request-specific source filters (`ApplyQueryFilter`).
3. Apply authorization/security and preference filters to the source type.
4. Map the source to the generated query DTO (`ApplyProjection` / `ApplyQuery`).
5. Apply DTO filters, contains-text search, and sorting.
6. Apply requested output column selection, then count, aggregate, and page.

Use the generated request's normal `ClientFilters`, `OrderBy`, `Skip`, `Take`,
and `IncludeTotalCount` fields. Add optional business parameters in the
developer-owned request partial and validate them in its validator partial.
Apply business predicates in `CreateProjectionQuery` or `ApplyQueryFilter`;
do not rely on client filters for mandatory restrictions. Supply a stable
default sort, ideally with a unique tie-breaker, for repeatable paging.

## Authorization and scope

Permission identity stays with the owning feature, but source security filters
operate on `TSource`, not on each table used in the join. A flat read model
does not automatically acquire the owning entity's capability interfaces,
warehouse/tenant fields, or row restrictions.

Include the fields needed for authorization in the source model and configure
the applicable security/preference behavior, or implement explicit trusted
scope predicates and the appropriate `GetSecurityFilter` override in the
handler partial. Verify both denied access and restricted-row access. Do not
disable security just to make an unmapped projection run.

For shared lookup metadata with deliberately different scoping, keep an
explicit permission gate. The declaration alone is not a replacement for a
review of that query's authorization policy.

## Current constraints

- Only `HasGetListQuery` supports `FromProjection`; item and lookup declarations
  using it are rejected while building metadata.
- Properties must be flat scalar values: CLR primitives, enums, `string`,
  `decimal`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`,
  `TimeSpan`, and `byte[]`, including nullable forms where applicable.
  Navigation objects, collections, and smart-enum classes are not supported
  as projection properties. Project their scalar IDs/codes and labels instead.
- Public instance properties with getters and setters are inspected; indexers
  and properties lacking either accessor are skipped. Nullability is inferred
  from the CLR type and nullable-reference metadata.
- `IncludeAll`, `IncludeOnly`, `Include`, and `Exclude` select the projection
  properties. Their lambda overloads are still typed to the owning entity;
  use string names for fields that exist only on the projection, for example
  `.IncludeOnly(nameof(ProductListItem.ProductId), nameof(ProductListItem.CategoryTitle))`.
- `MatchBy` validates names against the owning mapped entity. It is not a
  general-purpose filter declaration for projection-only fields; use custom
  request parameters and `ApplyQueryFilter` for those fields.
- An in-memory `AsQueryable()` is not an EF async query provider. Combining
  database options with in-memory values requires deliberate materialization
  and a custom paging implementation (for example, `CreatePagedList`), while
  preserving permission checks, filters, ordering, counts, and cancellation.

## Related list-handler changes

For ordinary mapped queries with smart-enum DTO properties, the list handler
supports title-based filtering using its smart-enum mappings.
This does not relax `FromProjection`'s scalar-only source rule.

Handler field mappings and QueryKit configuration are cached once per concrete
handler type. Register stable mappings in `InitializeQueryFieldMapping`; never
capture a request, handler, DbContext, current user, or culture-dependent
request state in those cached delegates. Request-specific behavior belongs
in query pipeline overrides. If overriding `ConfigHandler`, call its base
implementation.

See also the [feature profile reference](entity-capability-guide.md#feature-profile-dsl)
and [CLI guide](pdk-cli-guide.md).
