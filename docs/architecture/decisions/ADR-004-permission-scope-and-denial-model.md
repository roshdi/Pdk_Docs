# ADR-004: Permission, Scope, Implication, and Denial Model

- Status: Accepted
- Date: 2026-08-01

## Context

PDK entities may implement reusable security interfaces. These interfaces define
permissions, available scopes, entity requirements, and row-level filters. An entity
may implement multiple security interfaces, so a user can receive several scopes for
the same permission through roles and direct assignments.

PDK must also support user-specific scope data, permission implication, and explicit
denial without losing independently granted access. A flattened set of ASP.NET
Identity claims cannot represent the origin of an effective permission and therefore
cannot correctly remove access derived through a denied permission.

## Decision

An entity with no security interface is anonymous. An entity with one or more
security interfaces requires permission. Its available scopes are the union of the
non-conflicting scopes declared by those interfaces.

Every security interface supports the framework scopes `None` and `All`. Each custom
scope has exactly one convention-based filter named `<ScopeName>Filter`, marked with
`[SecurityFilter]`, with this signature:

```csharp
static Expression<Func<TEntity, bool>> ScopeFilter<TEntity>(
    IRequestContext context,
    SecurityGrant grant);
```

Roles and direct user assignments create root permission grants. Each root contains
a permission and scope. Role grants never contain user-specific scope data. Effective
grants obtain their data from preserved user scope-data records, preferring
permission-specific data over overall data for the same scope.

Multiple effective scopes for one permission are combined with logical OR. `All`
dominates other scopes. `None` contributes no accessible records and does not
override an allowing scope. A data-dependent scope with no valid effective data also
contributes no accessible records.

Permission implication is transitive. A root grant contributes its scope and scope
data to the root permission and every permission in its implication closure. The
implication is also a dependency: a user-level deny for any permission in that
closure invalidates the entire root. Independent roots remain effective.

Denies:

- are permitted only as direct user authorization state;
- apply to a complete permission, not to one scope;
- override role, direct, and implied allows for that permission;
- invalidate roots that depend transitively on the denied permission; and
- are stored only as explicit denies and are not expanded into derived deny rows.

The built-in Identity service stores structured role grants, direct user grants,
user denies, overall scope data, and permission-specific scope data. Scope data is
preserved when the permission or role that consumes it becomes inactive.

PDK validates the complete security metadata graph before generation. Conflicting
interfaces, duplicate scopes or properties, invalid filters, missing implication
targets, implication cycles, and incompatible implied scopes are errors and stop all
generation.

## Consequences

- Authorization evaluation must retain root-grant provenance rather than flattening
  assignments immediately into permission claims.
- The same deterministic resolver must be used for database-backed and claims-backed
  authorization.
- Identity requires dedicated authorization entities in addition to standard ASP.NET
  Identity claims and role membership.
- A user deny can make permissions that imply the denied permission ineffective.
- Administration surfaces must explain inactive grants, preserved scope data, and
  roots invalidated by denial.
- Existing permission claims require an explicit migration policy and cannot remain
  the authoritative persistent representation.

## Alternatives Considered

### A separate `Requires` relationship

Rejected because the accepted meaning of `Implies` includes both grant propagation
and dependency. Separating the relationships would not match PDK's intended behavior.

### Propagate and persist derived denies

Rejected because derived deny rows lose their origin and are difficult to retract
when definitions or direct denies change. Root invalidation derives the same result
while retaining the administrator's original intent.

### Allow denies on roles

Rejected because role-deny precedence makes the union of roles difficult to reason
about. Roles are additive. Exceptional restriction is user-specific and explicit.

### Store permissions only as ASP.NET Identity claims

Rejected because claims do not provide the structured scope, scope-data lifecycle,
grant origin, and implication provenance required by the model.
