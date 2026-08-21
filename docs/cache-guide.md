# Hybrid Cache Developer Guide

PDK provides `Pdk.Cache`, a provider-neutral two-level cache:

- L1 uses `IMemoryCache` inside each application process.
- L2 uses the configured `IDistributedCache` implementation.
- `ICacheInvalidationBus` provides fast local invalidation notifications.
- Periodic L2 version validation provides bounded recovery when a notification is
  delayed, lost, or unavailable across hosts.

The [cache architecture](architecture/cache-model.md) defines the consistency and
provider contracts behind this API.

## Platform configuration

Select the default distributed provider in the platform definition:

```csharp
Infrastructure(x => x
    .Cache(CacheProvider.Redis));
```

Available providers are:

| Provider | Generated behavior |
| --- | --- |
| `Redis` | Development uses memory-backed L2; other environments use Redis from `ConnectionStrings:Cache` |
| `Memory` | Uses memory-backed `IDistributedCache`; intended for development, tests, or one host |
| `None` | Does not register `Pdk.Cache` |

`AddDistributedMemoryCache()` is not a production distributed system. Separate
processes do not share its values.

Applications can register any `IDistributedCache` before calling
`AddPdkHybridCache()`. The cache runtime does not depend on Redis APIs:

```csharp
services.AddDistributedSqlServerCache(options =>
{
    options.ConnectionString = configuration.GetConnectionString("Cache");
    options.SchemaName = "cache";
    options.TableName = "Entries";
});

services.AddPdkHybridCache(options =>
{
    options.KeyPrefix = "Sales";
    options.EnvironmentName = environment.EnvironmentName;
});
```

For local development or unit tests:

```csharp
services.AddPdkDevelopmentCache(options =>
{
    options.KeyPrefix = "Sales";
    options.EnvironmentName = "Development";
});
```

## Reading and populating values

Inject `IPdkHybridCache` and use `GetOrCreateAsync`:

```csharp
public sealed class ProductReader(IPdkHybridCache cache, ProductDbContext db)
{
    public ValueTask<ProductDto> GetAsync(long productId, CancellationToken cancellationToken)
    {
        var key = new CacheKey(
            service: "Catalog",
            region: "product",
            value: productId.ToString());

        return cache.GetOrCreateAsync(
            key,
            async token => await db.Products
                .Where(product => product.Id == productId)
                .Select(product => new ProductDto(product.Id, product.Name))
                .SingleAsync(token),
            tags: ["products", $"product:{productId}"],
            cancellationToken: cancellationToken);
    }
}
```

PDK checks L1, then L2, then invokes the factory. Concurrent misses for the same
physical key share one factory execution within the process.

## Keys and tenant isolation

Use `CacheKey` rather than constructing physical strings:

```csharp
new CacheKey(
    service: "Catalog",
    region: "product",
    value: productId.ToString(),
    tenantId: tenantId);
```

PDK includes the configured prefix, environment, service, tenant, region, and value
in the normalized key. This prevents values from leaking between environments,
services, or tenants. Do not omit `tenantId` for tenant-specific data.

## Updating and removing values

Write the distributed value before committing the local value:

```csharp
await cache.SetAsync(
    key,
    product,
    tags: ["products", $"product:{product.Id}"],
    cancellationToken: cancellationToken);
```

Remove one key:

```csharp
await cache.RemoveAsync(key, cancellationToken);
```

Invalidate a group:

```csharp
await cache.RemoveByTagAsync($"product:{productId}", cancellationToken);
```

Invalidate every PDK entry in the configured prefix and environment logically:

```csharp
await cache.ClearAsync(cancellationToken);
```

Tag and global invalidation use distributed version tokens. Old L2 entries are not
required to be scanned or physically deleted; they are treated as misses and expire
normally.

## Expiration and consistency

Configure defaults globally:

```csharp
services.AddPdkHybridCache(options =>
{
    options.DefaultLocalExpiration = TimeSpan.FromMinutes(1);
    options.DefaultDistributedExpiration = TimeSpan.FromMinutes(20);
    options.DistributedValidationInterval = TimeSpan.FromSeconds(5);
});
```

Or override them for one entry:

```csharp
var entryOptions = new PdkCacheEntryOptions
{
    LocalExpiration = TimeSpan.FromSeconds(15),
    DistributedExpiration = TimeSpan.FromMinutes(5)
};
```

Local expiration must be positive and cannot exceed distributed expiration.
`DistributedValidationInterval` controls the maximum time an active L1 entry waits
before checking L2 again. Set it to zero for validation on every access, accepting
the additional distributed round trip.

Invalidation works through two complementary mechanisms:

1. `ICacheInvalidationBus` immediately evicts matching L1 entries reached by the
   notification.
2. L2 envelope/version validation detects changes after the configured interval,
   including between hosts that do not share an invalidation bus.

Provider-specific applications can replace the default in-process bus before
calling `AddPdkHybridCache()`:

```csharp
services.AddSingleton<ICacheInvalidationBus, BrokerCacheInvalidationBus>();
services.AddPdkHybridCache();
```

The registration uses `TryAdd`, so an application-provided bus is preserved.

## Serialization and types

L2 values use a versioned envelope and System.Text.Json by default. Replace the
serializer when necessary:

```csharp
services.AddSingleton<IPdkCacheSerializer, MessagePackPdkCacheSerializer>();
```

A physical key must always represent one value type. Requesting the same key with an
incompatible type throws a clear error instead of silently returning corrupted data.
Malformed L2 envelopes are removed and treated as cache misses.

## Operational rules

- Route writes and invalidations through `IPdkHybridCache`.
- Keep cached DTOs immutable; never mutate an object obtained from L1.
- Include tenant identity in every tenant-specific key.
- Keep L1 expiration shorter than L2 expiration.
- Use a cross-host invalidation-bus implementation when near-immediate eviction is
  required; retain version validation as the recovery path.
- Monitor distributed cache and invalidation failures. The current implementation
  propagates provider failures rather than silently serving potentially stale data.
- Do not use the memory distributed provider for multi-instance production hosting.
