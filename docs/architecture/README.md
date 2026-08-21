# PDK Architecture

This directory describes the current architectural intent of Prime Development
Kit (PDK). It complements the implementation-oriented guides in `docs/` and the
decision history in `decisions/`.

## Documents

- [Vision](vision.md) defines the product purpose and boundaries.
- [Principles](principles.md) records durable rules for design and review.
- [Security model](security-model.md) defines permissions, scopes, grants,
  implication, denial, Identity persistence, and authorization evaluation.
- [Hybrid cache model](cache-model.md) defines L1/L2 behavior, provider boundaries,
  invalidation, version validation, and consistency guarantees.
- [Open questions](open-questions.md) lists unresolved architectural work.
- [Architecture decisions](decisions/README.md) indexes accepted and superseded
  Architecture Decision Records (ADRs).

## System model

PDK consists of three cooperating layers:

1. **Definitions** describe platform, service, entity, feature, security, seed,
   and infrastructure structure in strongly typed C#.
2. **Generation** validates definitions, derives metadata, and creates projects
   and source files.
3. **Runtime libraries** provide the abstractions used by generated applications,
   including persistence, command/query handling, authentication, authorization,
   sessions, seeding, and provider integrations.

Definitions own structure; handwritten application code owns domain behavior.
Environment-specific values such as connection strings, signing keys, and session
lifetimes remain runtime configuration rather than structural definitions.

## Generated platform layout

`pdk new platform` creates the platform projects. Registered services are then
generated under `src/`:

```text
src/
|-- <PlatformName>.sln
|-- Platform/
|   |-- <RootNamespace>.Core/
|   |-- <RootNamespace>.Platform/
|   |-- <RootNamespace>.Shared/
|   |-- <RootNamespace>.Contracts/
|   `-- <RootNamespace>.Web/
|-- <ServiceName>/
    |-- <RootNamespace>.<ServiceName>.Core/
    |-- <RootNamespace>.<ServiceName>.Api/
    |-- <RootNamespace>.<ServiceName>.Web/
    |-- <RootNamespace>.<ServiceName>.UnitTests/
    `-- <RootNamespace>.<ServiceName>.IntegrationTests/
`-- <ApplicationName>/
    |-- <RootNamespace>.<ApplicationName>/
    `-- <application-name>/
```

The platform Web project hosts all registered modules as one application. Each
service Web project hosts one module independently. This supports incremental
migration between a modular monolith and independently deployed services without
changing service Core or API projects.

Each registered UI application has a .NET definition project that references the
API projects of its selected services. Developer-owned application configuration,
such as menus and component definitions, belongs in this project. Target-specific
UI projects are generated alongside it; Angular Web applications use a kebab-case
workspace directory and an Angular 22 standalone application structure.

### Platform projects

| Project | Responsibility |
| --- | --- |
| `*.Platform` | `PlatformDefinition`, service definitions, entity profiles, and structural configuration |
| `*.Core` | Shared capability interfaces, security types, seed profiles, and core abstractions |
| `*.Shared` | Shared implementation utilities used across services |
| `*.Contracts` | Cross-service contracts and messages |
| `*.Web` | Combined host for all registered service modules |

### Service projects

| Project | Responsibility |
| --- | --- |
| `*.Core` | Generated entities, EF Core configuration, DbContext, feature profiles, seeds, and developer-owned domain code |
| `*.Api` | Generated commands, queries, handlers, endpoints, module registration, and developer-owned API extensions |
| `*.Web` | Standalone service host |
| `*.UnitTests` | Service unit tests |
| `*.IntegrationTests` | Service integration tests |

Every service owns its DbContext, dependency registrations, and typed seed data.
The combined host composes these modules without merging their persistence models.

## Definition and metadata flow

`PlatformDefinition` is the structural entry point. It records:

- platform name, root namespace, and display name;
- infrastructure defaults;
- built-in and business service registrations.

Each `ServiceDefinition` declares its entities, features, dependencies, schema,
security, provider override, and seeds. `DefaultSchema(...)` is the single
service-wide schema setting. Built-in services use specialized builders only for
their additional structure; for example, `IdentityServiceBuilder` customizes
Identity entities, tables, sessions, and DbContext naming.

Initialization produces one metadata graph. Diagnostics are collected before
generation, and generation stops when errors exist. All generators must consume
this metadata rather than reinterpreting definition source independently.

## Generation lifecycle

Generation has two stages:

1. **Platform generation**

   Building `*.Platform.csproj` invokes:

   ```text
   pdk generate platform <platform-assembly> <platform-project-directory>
   ```

   The command loads and initializes the compiled `PlatformDefinition`, reports
   diagnostics, and creates or updates platform Web, service Core/API/Web/test
   projects, registered UI application directories, solution membership, entities,
   DbContexts, permissions, and seeds.

2. **Service API generation**

   Building a generated service Core project invokes:

   ```text
   pdk generate api <core-assembly> <core-project-directory>
   ```

   The command discovers compiled entity and feature metadata and generates the
   service API features and module registration.

3. **UI definition generation**

   Building a generated UI application definition project invokes:

   ```text
   pdk generate ui <application-assembly> <application-project-directory>
   ```

   The command loads the compiled `WebApplicationDefinition` and recursively
   initializes its generated service modules and developer-defined submodules.

The CLI commands can also be run explicitly. Explicit and build-integrated
invocation use the same handlers and templates.

Fully replaceable C# files use the `// <pdk-auto-generated />` marker and `.g.cs`
suffix. Companion customization scaffolds use a single marker containing the SHA-256
baseline of their generated body:
`// <pdk-auto-generated algorithm="sha256" value="..." />`. Writes are content-aware,
so unchanged output is not rewritten and existing companions are never overwritten.

Before API generation, PDK inventories marked files under `Features/` plus the
service module and reconciles them with every output expected by the completed run.
Stale `.g.cs` files are removed. A stale companion is removed only when its current
body matches its embedded baseline; modified and legacy unhashed companions are
preserved. Cleanup runs only after successful generation, removes empty feature
directories, and removes the obsolete API manifest. Developers must not edit `.g.cs`
files.

Angular grid generation consumes validated grid and endpoint metadata through a
deterministic CLI emitter. It writes definition, controller, page, and HTML artifacts
below `src/app/generated/` using the marker and manifest ownership rules in ADR-005.
Endpoint discovery supplies explicit query, request, service, and method names; the
emitter does not rediscover them from generated TypeScript.

## Runtime composition

Generated API modules derive from `ModuleBase`. A module:

- registers its DbContext and service-specific dependencies;
- registers typed data and identity seed contributors;
- initializes its own database and runs its own typed seeds.

The platform host registers every module. A standalone service host registers only
its module. When Identity is present in the combined host, its database-backed
authentication, authorization, seed store, and session services replace development
in-memory services.

Runtime request handling uses generated FastEndpoints endpoints and PDK command/query
handlers. Authentication validates the cookie or JWT identity and optional persisted
session before authorization evaluates the endpoint permission. See the
[Authentication, Authorization, and Sessions guide](../authentication-guide.md) and
[Security and Authorization Developer Guide](../security-capability-guide.md).

## Persistence and migration

Each service has a separate generated DbContext and migrations history table. Typed
`ISeedData<TDbContext>` registrations prevent seeds from crossing service boundaries
when multiple modules run in one process. Provider-specific behavior is supplied by
the selected `Pdk.Data.*` package.

During migration from a monolith, the existing database model can remain stable while
service boundaries, handlers, and workflows evolve around it. Schema redesign is an
application migration decision, not an automatic consequence of adopting PDK.

## Built-in Identity

Identity is represented by `Pdk.Definitions.BuiltInServices.IdentityService` and
customized through `IdentityServiceBuilder`. Generation creates ASP.NET Core Identity
entities, structured permission and scope assignments, preserved user scope data,
user-specific permission denies, and, by default, a persistent session entity.
The generated DbContext derives from `PdkIdentityDbContext`. Runtime configuration
controls session lifetime and whether effective authorization is included in
cookie/JWT claims. See the [security model](security-model.md) for the authoritative
authorization semantics and the focused authentication guide for configuration and
endpoint behavior.

## Current boundaries

- PDK generates infrastructure and extension points, not domain rules or workflows.
- Built-in Identity is implemented; other built-in services may have less complete
  runtime support.
- Infrastructure selections describe intent, but completeness varies by provider and
  subsystem.
- Generated implementation contracts may still change before PDK 1.0; definition
  compatibility remains the primary stability goal.

## Changing the architecture

1. Update current-state documentation when implementation behavior changes.
2. Update `vision.md` or `principles.md` only for durable product or design changes.
3. Record unresolved decisions in `open-questions.md`.
4. Add an ADR when a decision significantly constrains future implementation.
5. Supersede accepted ADRs with a new ADR instead of rewriting their history.

ADR statuses are **Proposed**, **Accepted**, **Rejected**, or **Superseded**.
