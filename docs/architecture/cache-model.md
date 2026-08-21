# Hybrid Cache Architecture

This document defines the architectural contract for PDK hybrid caching.

## Boundaries

`Pdk.Cache` coordinates three independent services:

```text
IPdkHybridCache
|-- IMemoryCache             L1, process-local
|-- IDistributedCache        L2, provider-selected
`-- ICacheInvalidationBus    notification fast path
```

Storage provider selection and invalidation transport are independent. The core
runtime depends only on Microsoft caching abstractions and PDK contracts.

## Cache key identity

A logical key contains service, region, value, and optional tenant. The runtime adds
the configured prefix and environment and escapes every component. This separation
is a correctness and security boundary, not a naming convenience. Maximum normalized
key length is validated before accessing either cache.

## Read algorithm

For `GetOrCreateAsync<T>`:

1. Return a valid L1 entry when distributed validation is not yet due.
2. Acquire the process-local keyed gate.
3. Recheck L1 after acquiring the gate.
4. Load and validate the versioned L2 envelope.
5. Reject incompatible schema, type, global version, or tag versions.
6. Populate L1 from a valid envelope.
7. Otherwise execute one factory, write L2, publish invalidation, and populate L1.

The keyed gate prevents process-local cache stampedes and is reference-counted so
unbounded keys do not leave permanent lock objects. It is not a distributed lock.

## Write ordering

`SetAsync` uses this order:

```text
read current global/tag versions
-> write new versioned envelope to L2
-> publish key invalidation
-> populate local L1
```

Publishing before the L2 write could cause another host to reload an old value.
Publishing after local population could cause the origin to evict its newly written
value when it receives its own notification.

## Invalidation and bounded staleness

Key notifications physically evict L1 entries reached by the bus. Tag and global
invalidation also update durable version tokens in L2. Envelopes record the token
versions that were current when written.

The bus is a low-latency optimization and may be provider-specific. Correctness does
not assume reliable delivery: after `DistributedValidationInterval`, an L1 hit checks
the L2 envelope again. A missed notification therefore has bounded effect. Setting
the interval to zero validates every hit but largely removes the latency advantage
of L1.

Tag/global invalidation is logical. Old envelopes remain in L2 until their normal
expiration but cannot become valid again while their version tokens remain. Version
tokens must have a lifetime at least as long as any envelope; the built-in runtime
stores them without an explicit expiration.

## Provider rules

- `CacheProvider.Memory` registers memory-backed `IDistributedCache` and the
  in-process bus. It is suitable only for development, tests, or one process.
- `CacheProvider.Redis` uses memory in Development and Redis in other environments.
- Any `IDistributedCache` implementation may back L2.
- Applications may replace `ICacheInvalidationBus` with Redis Pub/Sub, a message
  broker, or another transport before registering the hybrid cache.
- `CacheProvider.None` registers no PDK cache runtime.

PDK does not attempt to observe arbitrary native mutations made directly against a
provider. External writers must use the PDK envelope/version protocol or invalidate
through `IPdkHybridCache`.

## Failure behavior

Missing, expired, malformed, or logically invalid cache values become misses and
never become application data. A type/schema collision is a programming error and
throws. Provider and invalidation publication failures currently propagate; PDK does
not silently downgrade to stale L1 data. A future resilience policy may make this
configurable, but it must state its consistency consequences explicitly.

## Extensibility

Stable extension points are:

- `IDistributedCache` for L2 storage;
- `ICacheInvalidationBus` for notification transport;
- `IPdkCacheSerializer` for envelope serialization; and
- `PdkCacheOptions` and per-entry options for expiration and validation cadence.

Provider-specific packages must not change key identity, envelope validation,
write ordering, tenant isolation, or fail-closed type/schema behavior.
