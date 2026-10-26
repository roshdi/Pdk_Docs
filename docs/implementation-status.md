# Current implementation status

This page summarizes the repository implementation, checked on 2026-10-26.
Implemented means code exists for the workflow; it does not imply production
readiness, complete provider coverage, or a stable 1.0 contract. Installed NuGet
versions may differ from this checkout.

| Area | Current implementation | Limits |
| --- | --- | --- |
| Platform scaffolding | `pdk new platform` creates definitions and platform projects | Interactive; requires the .NET SDK and available PDK packages/tool |
| Platform generation | `pdk generate platform` creates service Core/API/Web/test projects, the combined Web host, and registered UI application projects | Domain behavior and deployment configuration remain developer-owned |
| API generation | `pdk generate api` emits contracts, validators, mappers, handlers, endpoints, and module registration | Preview contracts; custom workflows use companion partials |
| Features | CRUD, business-key update/upsert, item/list/lookup queries, bulk operations, Excel imports, and projection-backed lists | Each feature has constraints documented in its focused guide |
| Identity | ASP.NET Core Identity integration, structured grants/denies, authorization administration, signed authorization snapshots, and sessions | Remote authorization and immediate cross-service invalidation remain open architecture topics |
| Persistence | Per-service EF Core contexts and migration scripts; SQL Server, PostgreSQL, and Oracle concurrency mappings | Provider support is not uniform; provider metadata alone is not a completeness guarantee |
| Cache | Provider-neutral L1/L2 cache, memory/Redis host registration, version validation, and invalidation bus contract | Default bus is in-process; multi-host immediate invalidation requires a transport implementation |
| Angular | `pdk generate ui` emits workspaces, API clients, modules, layouts, pages, grids, forms, lookups, enums, and resources | Preview; API client/component generation errors can be reported as warnings while the command succeeds; inspect output and build the generated workspace |
| Metadata export | `pdk metadata export` writes `application.metadata.json` | Experimental discovery expects an assembly containing a discoverable DbContext; it is not a general Platform-definition export |
| Add-service command | `pdk add service` prints guidance | Does not create or register a service; edit the Platform definition and regenerate |
| Entity command | `pdk entity` prompts for style/capabilities and prints selections | Does not generate an entity; declare it in the service definition |
| Messaging and observability | Infrastructure selections express intent | Complete broker and telemetry integration is not generated |
| Deployment | Architectural direction only | Deployment generation remains planned |

## Documentation authority

Use the [architecture reference](architecture/README.md) and its focused security
and cache models for architectural contracts. Accepted ADRs retain historical
decisions; an implementation difference is not automatically an accepted design
change. Use focused developer guides for API usage, and check source and tests when
examples or status descriptions disagree.

Current Angular output and ownership differ from the original layout in ADR-005:
normal feature output lives under `src/app/modules/`, and cleanup uses file markers
and content hashes without a persisted manifest. The current behavior is described
in the architecture reference. A future ADR should reconcile the accepted decision
with this implementation before treating the changed layout as a durable contract.

When changing behavior, update this status page and the affected guide in the same
change. Keep unresolved decisions in [open questions](architecture/open-questions.md).
