# Security and Authorization Developer Guide

This guide explains how application developers declare security capabilities and
how administrators' assignments become runtime authorization decisions. The
[security architecture](architecture/security-model.md) is the authoritative
contract; this guide focuses on normal development workflows.

## Mental model

PDK separates five concerns:

1. A **permission** says what operation may be performed, such as
   `Product.Read` or `Product.Update`.
2. A **scope** says which records that permission can reach, such as `Owner`,
   `Unit`, or `All`.
3. **Scope data** supplies values needed by a filtered scope, such as allowed unit
   IDs. Scope data belongs to a user, never to a role.
4. An **allow root** is a permission-and-scope pair granted by a role or directly
   to a user. Permission implication can derive more permissions from that root.
5. A **deny** is a user-only instruction that blocks an entire permission and any
   allow root that depends on it.

Roles contain permission-and-scope pairs. Assigning a role automatically supplies
both parts of each grant. A user's direct grants are unioned with grants from every
assigned role.

## Securing an entity

An entity may implement zero, one, or multiple security interfaces:

```csharp
// No security interfaces: generated features are anonymous.
x.Entity("ProductImage");

// Permission checks, with None and All scopes.
x.Entity("Category")
    .HasSecurity<IBasicSecurity>();

// Row access by owner or unit. The entity's scopes are the union:
// None, Owner, Unit, All.
x.Entity("Product")
    .HasSecurity<IOwnedSecurity>()
    .HasSecurity<IUnitSecurity>();
```

The CLI validates the complete definition before generating code. Generation stops
if composed security interfaces declare the same scope, conflicting properties,
invalid filters, invalid permission implications, or another ambiguous contract.
Do not interpret “no security interface” as “authenticated but unrestricted”; it is
explicitly anonymous.

## Defining a security interface

Every security interface inherits `ISecurity` and declares permissions and scopes:

```csharp
public interface IProjectSecurity : ISecurity
{
    Guid ProjectId { get; set; }

    static void ISecurity.ConfigurePermissions(PermissionBuilder permissions)
    {
        permissions.ConfigurePermissionsFrom<IBasicSecurity>();
    }

    static SecurityScopeDefinition[] ISecurity.GetAvailableScopes() =>
        [SecurityScopes.None, SecurityScopes.Project, SecurityScopes.All];

    [SecurityFilter]
    static Expression<Func<TEntity, bool>>? ProjectFilter<TEntity>(
        IRequestContext context,
        SecurityGrant grant)
        where TEntity : IProjectSecurity
    {
        return grant
            .GetSecurityConstraints<Guid>()
            .CreateFilter()
            .WithParameter<TEntity>(entity => entity.ProjectId);
    }
}
```

Every interface supports `None` and `All`. Every additional scope requires a filter
by convention. The method must:

- be static and marked `[SecurityFilter]`;
- be named `<ScopeName>Filter`;
- accept `IRequestContext context`, followed by `SecurityGrant grant`;
- return an expression compatible with the secured entity; and
- constrain its entity type to the capability that declares the required fields.

`None` contributes no access. It is not a deny. `All` makes the permission
unrestricted and overrides filtered scopes for that permission.

## Built-in capabilities

| Interface | Additional scope | Required entity field | Data source |
| --- | --- | --- | --- |
| `IBasicSecurity` | None | None | None |
| `IOwnedSecurity` | `Owner` | `OwnerId` | Authenticated user from `IAuthService` |
| `IUnitSecurity` | `Unit` | `UnitId` | Unit values resolved into `SecurityGrant` |

`IOwnedSecurity` also initializes `OwnerId` through the `ICreated` capability. It
uses `context.Auth.GetRequiredUserId()`, which rejects anonymous requests and
converts the authenticated transport ID into the platform's configured `AuthId`.

## Correlated multidimensional scope: warehouse example

Some access rules correlate two or more fields. For example, granting document types
1 and 2 in warehouse 1 must not accidentally grant those document types in every
warehouse. Add a stable platform scope definition:

```csharp
public static class SecurityScopes
{
    public const string WarehouseId = "Warehouse";
    public static SecurityScopeDefinition Warehouse =
        new(WarehouseId, "Warehouse");
}
```

Then declare both dimensions on one security capability and build its predicate from
`GetSecurityConstraints<long, int>()`:

```csharp
public interface IWarehouseSecurity : ISecurity
{
    long WarehouseId { get; set; }
    int DocumentType { get; set; }

    static void ISecurity.ConfigurePermissions(PermissionBuilder permissions)
    {
        permissions.ConfigurePermissionsFrom<IBasicSecurity>();
    }

    static SecurityScopeDefinition[] ISecurity.GetAvailableScopes() =>
        [SecurityScopes.None, SecurityScopes.Warehouse, SecurityScopes.All];

    [SecurityFilter]
    static Expression<Func<TEntity, bool>>? WarehouseFilter<TEntity>(
        IRequestContext context,
        SecurityGrant grant)
        where TEntity : IWarehouseSecurity
    {
        return grant
            .GetSecurityConstraints<long, int>()
            .CreateFilter()
            .WithParameters<TEntity>(
                entity => entity.WarehouseId,
                entity => entity.DocumentType);
    }
}
```

Scope data preserves the correlation by storing an array of rules:

```json
[
  { "values1": [1], "values2": [1, 2] },
  { "values1": [2], "values2": [2, 3] }
]
```

The generated predicate is equivalent to:

```text
(WarehouseId IN [1] AND DocumentType IN [1, 2])
OR
(WarehouseId IN [2] AND DocumentType IN [2, 3])
```

Values within a rule are AND-correlated across dimensions. Rules are OR-composed.
An empty value array inside a multidimensional rule leaves that dimension
unrestricted, while an empty rule array produces a deny-all predicate.

## Combining scopes

All effective scopes for one permission are OR-composed. If `Product` supports
`Owner` and `Unit`:

| Effective scopes | Result |
| --- | --- |
| `Owner` + `All` | Every product |
| `None` + `All` | Every product |
| `Owner` + `Unit` | Products owned by the user **or** in an allowed unit |
| `None` only | No products |

An empty or invalid filtered grant contributes no records and must never widen
access. Constraint values inside one filter rule are AND-composed; independent
rules and independent scope grants are OR-composed.

## Overall and permission-specific scope data

Scope data is stored with the user as versioned JSON. Administrators can assign it
at two levels:

- **Overall:** `Unit = [Unit1]` supplies Unit1 to every effective permission using
  `Unit`.
- **Permission-specific:** `Customer.Read + Unit = [Unit2]` replaces the overall
  Unit data for that permission and scope.

Precedence is:

```text
permission-specific record -> overall record -> no data -> no access
```

An explicit empty permission-specific record suppresses overall fallback. Removing
a permission or role makes its scope data inactive but does not delete it. If the
grant returns later, the preserved data becomes effective again. Scope JSON must be
validated against its registered scope schema and version before use.

## Permission implication

Use `Implies()` (or conveniences such as `ImpliesRead()`) to describe both derived
access and its dependency:

```csharp
permissions.Read();
permissions.Update().ImpliesRead();
```

An `Update + Unit` root contributes `Update + Unit` and implied `Read + Unit`, with
the same scope data and provenance. The implication graph must be acyclic, every
target must exist, and duplicate permission IDs are invalid.

PDK retains each root instead of flattening permissions. Consequently:

- An independent `Read` grant survives when a separate `Update` root is denied.
- If `Update` is the only root and `Update` is denied, its implied `Read` disappears.
- If `Read` is denied, an `Update` root that depends on `Read` is invalidated too.

There is no separate `Requires()` relation; implication carries the dependency.

## User-specific denies

Denies are permitted only on direct user authorization state, never on roles. A deny:

- contains a permission ID, with no scope or scope data;
- overrides role, direct, and implied allows for that permission; and
- invalidates any allow root whose transitive implication closure contains the
  denied permission.

For example, `Product.Read + Deny` removes all access to `Product.Read` and also
invalidates `Product.Update` when Update implies Read. Denying one root does not erase
an independent root for another permission.

## Runtime authorization

Call the decision-based service once for the required permission:

```csharp
PermissionAuthorizationDecision decision = await authorization.AuthorizeAsync(
    "Product.Read",
    cancellationToken);
```

The decision reports whether access is allowed, whether it is unrestricted, and all
filtered grants required to build row predicates. Do not split this into a Boolean
check followed by a second scope lookup: the two reads could observe different state
and a single grant cannot represent composed scopes.

At login, Identity can emit a signed, versioned `pdk:authorization` snapshot into
cookie and JWT claims. It contains effective allowed permissions and complete
decision scopes, not raw roots, denies, or inactive scope data. Consumers fail closed
for malformed data, unsupported versions, duplicate permission IDs, or inconsistent
scope shapes. When authorization data must take effect immediately, use an online
resolver or session invalidation rather than relying on a long-lived snapshot.

## Identity persistence

The built-in Identity model uses five authorization entities:

| Entity | Purpose |
| --- | --- |
| `RolePermissionGrant` | Permission-and-scope pairs granted by roles |
| `UserPermissionGrant` | Direct user permission-and-scope pairs |
| `UserPermissionDeny` | Direct user permission denies |
| `UserScopeData` | Overall user scope data |
| `UserPermissionScopeData` | Permission-specific user scope data |

Use the authorization administration service to mutate this state so validation,
idempotency, audit actor, timestamp, and correlation information are applied
consistently. Revoking a grant must not delete preserved scope data.

Authorization entities intentionally do not force auditing or concurrency fields.
Platform developers opt into capabilities as required:

```csharp
x.UserScopeData
    .Implements<ICreatedEntity>()
    .Implements<IModifiedEntity>();
```

Generated create/update handlers invoke `ICreated` and `IModified` through
`CapabilityInvoker`, so custom capability implementations remain effective.

## Request context and capability identity

`IRequestContext` carries a stable correlation ID, resolves request-scoped services,
and caches derived services. Prefer the type-safe cache overload in platform
extensions:

```csharp
public IAuthService Auth => context.GetOrAdd(
    () => context.GetRequiredService<IAuthService>());
```

The platform Core `IAuthService` is implemented by a scoped Web adapter over PDK's
validated authentication service. Never fabricate a user ID and never read HTTP
state directly from an entity capability. Creation, modification, deletion, and
ownership capabilities call `GetRequiredUserId()` and therefore fail closed for an
anonymous or incompatible actor ID.

## Seeding and legacy migration

Role and user seeds must name a scope for every permission grant. There is no implicit
default scope. User seeds may also declare denies and both forms of scope data;
validation rejects unknown IDs and duplicates.

Legacy `permissions` claims meant unrestricted access. They can therefore become
structured `All` grants only through the explicit legacy migration service and an
operator-selected policy. Migration is never automatic at startup, never converts
filtered grants or denies, and only accepts permissions in the validated registry.

## Developer checklist

- Treat entities without security interfaces as deliberately anonymous.
- Keep security interfaces small and independently composable.
- Use stable permission and scope IDs; display names are not identifiers.
- Give every custom scope a correctly named, translatable filter.
- Treat missing, empty, malformed, or incompatible scope data as no access.
- Preserve grant provenance through implication and authorization decisions.
- Put denies only on users and never attach scope data to a deny.
- Validate the complete definition graph before generation.
- Use `IRequestContext` services in capabilities instead of ambient HTTP or static
  system state.
- Use administration APIs rather than editing authorization tables directly.

For storage, resolver, validation, auditing, claim-format, and migration invariants,
read the [authoritative security model](architecture/security-model.md). For host and
login configuration, read the
[Authentication and Authorization Guide](authentication-guide.md).
