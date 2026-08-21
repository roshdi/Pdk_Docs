# PDK Security Model

This document defines the authoritative architectural model for permissions, scopes,
scope data, implication, denial, Identity persistence, validation, and runtime
authorization. Implementation-oriented guides may describe the current API surface,
but implementations must converge on the semantics defined here.

## Security capabilities and entity access

Platform security capabilities are C# interfaces that inherit from `ISecurity`.
Applications may add capabilities in the platform Core project. A capability:

- declares permissions with `ISecurity.ConfigurePermissions(PermissionBuilder)`;
- declares supported scopes with `ISecurity.GetAvailableScopes()`;
- may require implementing entities to expose properties used by its filters; and
- provides one convention-based filter for every custom scope.

An entity may implement zero, one, or multiple security interfaces.

- Zero security interfaces means the generated entity features are anonymous.
- One or more security interfaces means the generated entity features require the
  declared permission.
- The entity's available scopes are the union of the scopes declared by its security
  interfaces after validation proves that the interfaces do not conflict.

For example, an entity implementing `IOwnedSecurity` and `IUnitSecurity` supports
`None`, `Owner`, `Unit`, and `All`.

## Permissions and implication

A permission identifies an operation such as `Product.Read`, `Product.Update`, or
`Product.Approve`. Permission IDs are stable structural identifiers produced from
the definition metadata and stored by Identity.

Permissions may imply other permissions:

```csharp
permissions.Update().ImpliesRead();
```

Implication is transitive and has two connected meanings:

1. A grant of the source permission derives grants for every implied permission.
2. The source permission depends on every implied permission remaining allowed.

The implication graph must be acyclic. Every target must exist and must accept every
scope that can propagate from the source permission.

## Scopes

A scope determines which records a permission grant can access. Every security
interface supports two framework scopes:

- `None` contributes no accessible records.
- `All` grants unrestricted record access and dominates every other allowing scope.

Every additional scope requires one filter. Common examples are `Owner`, which uses
the current authenticated user, and `Unit`, which uses unit identifiers resolved into
`SecurityGrant`.

`None` is not a permission deny. It is a non-granting scope and does not override an
allowing `Owner`, `Unit`, or `All` grant for the same permission.

## Security filter convention

Every custom scope named `<ScopeName>` has exactly one method named
`<ScopeName>Filter`. The method is static, is marked with `[SecurityFilter]`, accepts
`IRequestContext` followed by `SecurityGrant`, and returns an entity-compatible
expression tree:

```csharp
[SecurityFilter]
static Expression<Func<TEntity, bool>> UnitFilter<TEntity>(
    IRequestContext context,
    SecurityGrant grant)
    where TEntity : IUnitSecurity
{
    // Return an EF Core-translatable expression.
}
```

Both parameters are always present. A contextual scope such as `Owner` may ignore
the grant. A data-dependent scope such as `Unit` reads validated values from it.

The framework invokes `Owner` with an empty valid grant. A data-dependent scope with
missing, empty, malformed, unknown, or incompatible data contributes no records.
Only the explicit `All` scope represents unrestricted access.

## Root grants and grant sources

A root grant is an explicit authorization assignment before implication is applied:

```text
permission ID + scope ID + source
```

There are two allowing sources: a role permission grant and a direct user permission
grant. The user's roots are the union of grants from every assigned role and every
direct user grant. Roles automatically grant every permission-and-scope pair they
contain and contain no user-specific scope data.

The same permission can have multiple roots and multiple scopes. Root identity and
source are retained during evaluation so denial removes only dependent access.

## User scope data

Data-dependent scopes obtain their values from user-owned persisted state. Scope data
is separate from role membership and direct permission grants so it survives
temporary changes in authorization.

### Overall scope data

Overall data supplies the default for every effective permission using a scope. If
User 42 has overall `Unit = [Unit1]` and their roles grant `Product.Read + Unit` and
`Customer.Read + Unit`, both permissions use Unit1 unless overridden.

### Permission-specific scope data

Permission-specific data replaces overall data for one permission and scope:

```text
User 42 + Product.Read + Unit  = [Unit1]
User 42 + Customer.Read + Unit = [Unit2]
```

Resolution is:

```text
permission-specific record exists -> use it
otherwise                         -> use overall scope data
```

Permission-specific data is an override, not a union. This prevents overall data
from unintentionally widening a permission-specific assignment.

The persistence model distinguishes absence from an explicit empty value:

- no permission-specific record means fall back to overall data;
- an explicit empty permission-specific record suppresses fallback and grants no
  records through that scope.

Removing a role, role grant, or direct grant does not delete scope data. The data is
preserved but inactive. If the permission and scope become effective again, the
preserved data is reused. Administrative tools must show this inactive state and
allow deliberate deletion.

Scope data uses a versioned JSON envelope:

```json
{
  "schema": "pdk.scope.unit",
  "version": 1,
  "data": {
    "businessUnitIds": [1, 2]
  }
}
```

PDK validates the envelope before storage where possible and again when resolving
authorization. Validation failure never widens access.

## Multiple-scope composition

Every effective scope grant for one permission contributes an access predicate.
Predicates are combined with logical OR.

| Effective scopes | Result |
| --- | --- |
| `Owner`, `All` | unrestricted because `All` dominates |
| `None`, `All` | unrestricted because `None` contributes nothing |
| `Owner`, `Unit` | owned records OR records in effective units |
| `None` only | no access |
| `Unit` with no effective unit data | no access |

Multiple roots for the same custom scope may be normalized when their data schema
supports a safe union. Otherwise PDK evaluates each grant separately and OR-composes
the filters. Normalization must not erase diagnostic provenance.

## User-specific permission denial

PDK permits explicit denial only for a user. Roles are additive and cannot deny.

A deny contains a permission ID only. It has no scope and no scope data. It blocks
the complete permission regardless of whether access would otherwise come from a
role, a direct grant, an implied grant, or the `All` scope.

Denies are persisted only as administrator-authored facts. PDK does not write
transitively derived denies. Instead it invalidates dependent root grants during
evaluation.

## Implication and denial evaluation

For permission `P`, `Closure(P)` is `P` plus every permission transitively implied by
`P`. A root grant is viable when its closure contains no explicit user deny. A viable
root contributes its scope and resolved scope data to every permission in its
closure. A non-viable root contributes nothing. Independent roots are evaluated
independently.

### Independent permission survives a source deny

```text
Roots: Product.Read, Product.Update
Update implies Read
Deny:  Product.Update
```

The Update root and its derived Read are removed. The independent Read root remains,
so Read is allowed and Update is denied.

### Denied source removes its implied access

```text
Root:  Product.Update
Update implies Read
Deny:  Product.Update
```

The only root is removed. Neither Update nor Read is available.

### Denied dependency invalidates its source

```text
Root:  Product.Update
Update implies Read
Deny:  Product.Read
```

The Update root is invalid because its closure contains denied Read. Neither
permission is available.

Given `Approve -> Update -> Read`, denying Update invalidates both an Update root and
an Approve root. Denying Read invalidates roots for Read, Update, and Approve. An
independent grant not depending on the denied permission remains effective.

If a root implies several branches, denial of any node in its closure invalidates the
whole root. PDK does not retain unrelated implied branches from a root whose source
operation can no longer be authorized correctly.

## Effective authorization algorithm

For a user and requested permission, the canonical resolver:

1. Loads assigned roles, role roots, direct user roots, explicit user denies, overall
   scope data, and permission-specific scope data.
2. Computes each root's transitive closure from generated permission metadata.
3. Discards every root whose closure contains an explicitly denied permission.
4. Propagates each remaining root's scope to every permission in its closure.
5. Selects the grants contributing to the requested permission.
6. Returns unrestricted access if any contributing scope is `All`.
7. Discards `None` scopes.
8. Resolves permission-specific scope data, otherwise overall scope data, for every
   data-dependent scope.
9. Discards scopes whose required data is absent, empty, or invalid.
10. Resolves each remaining custom scope to its validated security filter.
11. OR-composes those filters. If none remain, access is denied.

Database-backed, claims-backed, and remote authorization must conform to this same
algorithm. Claims may contain a versioned effective snapshot, but flattened
permission claims are not the persistent source of truth.

## Identity persistence model

The built-in Identity service owns structured authorization entities in addition to
the standard ASP.NET Identity entities and sessions.

| Entity | Fields | Unique key |
| --- | --- | --- |
| `RolePermission` | `Id`, `RoleId`, `PermissionId`, `ScopeId` | `(RoleId, PermissionId, ScopeId)` |
| `UserPermission` | `Id`, `UserId`, `PermissionId`, `ScopeId` | `(UserId, PermissionId, ScopeId)` |
| `UserPermissionDeny` | `Id`, `UserId`, `PermissionId` | `(UserId, PermissionId)` |
| `UserScopeData` | user, scope, schema/version, JSON | `(UserId, ScopeId)` |
| `UserPermissionScopeData` | user, permission, scope, schema/version, JSON | `(UserId, PermissionId, ScopeId)` |

Authorization rows reference users and roles with cascade delete. Removing a grant
does not cascade to scope-data rows. Stable permission and scope IDs connect runtime
state to generated structural metadata.

Provider-neutral identity seed contracts express every role or direct-user grant as
an explicit `(PermissionId, ScopeId)` pair. A scope is therefore mandatory when a
permission is seeded; there is no implicit default scope. Denies contain only a
permission ID, are supported only on user seeds, and cannot carry a scope. Seed
builders reject duplicate permission/scope pairs and duplicate user denies using
case-insensitive ID comparison. Scope-data payloads are intentionally separate from
these grant contracts and remain owned by `UserScopeData` and
`UserPermissionScopeData`.

Authorization entities do not prescribe auditing, modification tracking, optimistic
concurrency, or other application capabilities. Platform developers add these through
the Identity entity builders, for example:

```csharp
identity.UserScopeData
    .Implements<ICreatedEntity>()
    .Implements<IModifiedEntity>();
```

Generated create and update handlers invoke implemented `ICreated` and `IModified`
capabilities through `CapabilityInvoker<TEntity, TCapability>`. Their properties,
mapping, and behavior therefore remain owned by the selected platform capability.

Generated platform capabilities obtain actor identity through the platform Core
`IAuthService`, not from constants or directly from HTTP state. The Web project
registers a scoped `DefaultAuthService` adapter over PDK's validated
`IAuthenticationService`. Core's `GetRequiredUserId()` converts the transport string
user ID to the platform's configured `AuthId` and fails clearly when the value is
incompatible, keeping the Web adapter independent of generated ID aliases. It rejects anonymous
execution, so `ICreatedEntity`, `IModifiedEntity`, `IDeletedEntity`, and
`IOwnedSecurity` never stamp a fabricated actor. Undelete clears deletion actor and
timestamp instead of recording the undelete operation as a deletion. Platforms that
need undelete audit history should add a dedicated capability or history entity.

### Raw authorization store

The Identity runtime exposes a scoped `IRawAuthorizationStore<TKey>`. Its EF Core
implementation reads the generated user-role and authorization entity sets and
returns an immutable `RawAuthorizationState` containing:

- role permission/scope grants, retaining the originating role ID;
- direct user permission/scope grants;
- direct user permission denies;
- overall user scope-data envelopes; and
- permission-specific user scope-data envelopes.

The raw store is intentionally policy-free. It filters records by user, uses
no-tracking queries, and preserves inactive scope data, but it does not expand the
permission implication graph, apply deny precedence, select between overall and
permission-specific data, validate schemas or versions, deserialize JSON, or combine
scopes. Those operations belong to the effective authorization resolver. Generated
Identity modules register the raw store only when all five authorization entities and
the ASP.NET Identity user-role entity are available.

### Pure effective-permission resolver

`EffectivePermissionResolver` consumes validated permission definitions and a
`RawAuthorizationState`. It has no database, claims, request-context, JSON, or filter
dependency. For each explicit root it computes the transitive closure, removes the
entire root when any permission in that closure is explicitly denied, and propagates
the surviving root's scope to every permission in the closure.

The result preserves root permission, grant source, source ID, and scope for every
effective permission. It also reports invalidated roots and unknown persisted roots
for diagnostics. Independent roots remain independent: invalidating an Update root
does not remove an independently granted Read root. Diamond-shaped implication paths
produce one effective grant per root and target permission.

The resolver treats permission and deny IDs case-insensitively, rejects duplicate
permission definitions, missing implication targets, and cycles, and ignores unknown
persisted roots rather than granting them. It deliberately does not inspect scope
data or decide record-level access; that belongs to the authorization-resolution
layer.

## Runtime authorization contract

Authorization returns one composite decision rather than a Boolean permission check
plus one optional grant. A decision distinguishes unrestricted access, filtered
access, and denial and carries every effective scope needed to construct the filter:

```csharp
public sealed record PermissionAuthorizationDecision(
    bool IsAllowed,
    bool IsUnrestricted,
    IReadOnlyList<EffectiveScopeGrant> Scopes,
    AuthorizationDenialReason? DenialReason);
```

Effective grants retain their root permission and source for audit and explanation.
Implementations may deduplicate equivalent predicates without discarding diagnostic
provenance.

`IAuthorizationService` exposes only
`AuthorizeAsync(permission, cancellationToken)`. The former split operations for a
Boolean permission check and a single optional grant are not part of the contract;
they could observe different authorization state and could not represent multiple
OR-composed scopes. Denied decisions contain no scopes. `All` produces an
unrestricted decision, while `None` is removed and cannot grant access by itself.

At login, the Identity host resolves structured authorization once and emits one
signed `pdk:authorization` JSON claim. Version 1 contains only effective allowed
permissions and their complete decision scopes; it does not emit raw roots, raw
denies, or inactive scope data. Claims-based authorization treats the snapshot as
authoritative whenever present. A malformed snapshot, more than one snapshot, an
unsupported version, duplicate permission IDs, or an inconsistent unrestricted or
filtered representation fails closed without consulting legacy flattened claims.

## Generation-time validation

The CLI initializes and validates the complete metadata graph before writing any
generated file. Security errors include:

- security interfaces contributing the same or conflicting property;
- implemented interfaces declaring the same custom scope;
- a custom scope without exactly one matching `<ScopeName>Filter`;
- a filter for an undeclared scope;
- an invalid filter attribute, name, static modifier, return type, generic constraint,
  or parameter signature;
- redefinition of framework `None` or `All` scopes;
- a data-dependent scope without a grant-data schema;
- missing or duplicate permission IDs;
- a missing implication target, implication cycle, or incompatible implied scope;
- a role deny or a deny carrying scope data; and
- seeded scope data referring to an unknown permission, scope, schema, or version.

Expected errors are collected into stable, actionable diagnostics. Any validation
error stops generation before files are written.

## Administration and observability

Administration surfaces must distinguish root grants from implied grants, role
grants from direct grants, active scope data from preserved inactive data, explicit
denies from roots invalidated by them, and invalid scope data from missing grants.

Authorization diagnostics and audit records should include the requested permission,
decision, contributing or invalidated roots, scope IDs, and stable reason codes. API
responses remain limited to `401 Unauthorized` and `403 Forbidden` as appropriate and
must not disclose sensitive grant data.

The Identity package exposes `IAuthorizationAdministrationService<TKey>` for
authorization-management applications. It provides idempotent operations for role
and direct-user grants, user-only denies, overall scope data, and
permission-specific scope data. Revoking a grant does not remove its scope data;
scope data has separate explicit removal operations. Scope-data creates and updates
invoke optional `ICreated` and `IModified` capabilities through `CapabilityInvoker`,
so platform-owned auditing and concurrency customization remains effective.

`GetUserAuditAsync` returns an explanation-oriented view containing role/direct root
provenance, viable and invalidated roots, blocking deny IDs, effective decisions,
unknown persisted roots, and whether each scope-data record is active, an active
override, or preserved inactive. It includes a request correlation ID. The audit
view never returns `DataJson` or serialized constraints; it reports only scope,
schema/version, provenance, and whether data exists. HTTP or UI adapters over this
service must require a dedicated platform administration permission and must not use
ordinary end-user authorization as proof of administrative authority.

### Legacy permission-claim migration

Legacy `permissions` claims represented unrestricted access and therefore may be
converted only to explicit `AllowAll` grants. PDK never performs this conversion at
startup or merely because structured authorization entities exist. An operator must
resolve `ILegacyPermissionClaimMigration` and supply one of the explicit policies:

```csharp
var migration = services.GetRequiredService<ILegacyPermissionClaimMigration>();

var preview = await migration.MigrateAsync(
    LegacyPermissionClaimMigrationPolicy.Preview());

var result = await migration.MigrateAsync(
    LegacyPermissionClaimMigrationPolicy.CopyToAll());
// Or, after review:
// LegacyPermissionClaimMigrationPolicy.MoveToAll()
```

`Preview` reports without writing. `CopyToAll` creates structured grants and retains
the old claims for a staged rollout. `MoveToAll` creates missing grants and removes
migratable legacy claims, including claims whose matching All grant already exists.
The process handles both role and user claims, is idempotent, invokes optional
`ICreated` capabilities, and never converts `pdk:grant`, deny, or authorization
snapshot claims.

Only permission IDs present in the validated `PermissionRegistry` are migratable.
Empty and unknown IDs remain untouched and are returned with stable skip reasons.
An existing Owner, Unit, or other restricted grant does not suppress creation of the
All grant because doing so would silently narrow the legacy unrestricted meaning.
Operators should preview and archive the report before choosing Copy or Move.
