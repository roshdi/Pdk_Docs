# ADR-001: Strongly Typed Definitions Are the Structural Source of Truth

- Status: Accepted
- Date: 2026-07-17

## Context

PDK needs one authoritative representation of application structure. Possible
sources include C# definitions, external configuration files, domain-model
reflection, and generated output. Multiple authoritative sources would create
conflicts and make generation difficult to validate or reproduce.

Enterprise .NET teams benefit from compile-time feedback, refactoring support,
code review, and discoverable APIs.

## Decision

Strongly typed C# definitions are the source of truth for platform, service,
entity, and capability structure.

PDK discovers these definitions, validates them, and builds a unified metadata
model. Generators and runtime integration consume that metadata. External
configuration may supply environment-specific values but does not redefine the
structural model.

Examples of runtime configuration include connection strings, credentials, signing
keys, and session lifetimes. Examples of structural definitions include services,
entities, schemas, features, permissions, and seeds.

Property-set DSL methods accept either one direct property (`x => x.Title`) or an
anonymous projection (`x => new { x.Title, x.Code }`). A projection is only valid
where the operation applies independently to every selected property. Navigation,
field-configuration, and other single-target methods continue to require one direct
property. Nested paths and computed expressions are rejected with a source-located
definition diagnostic.

## Consequences

- Definition APIs require careful compatibility management.
- Invalid structure can be diagnosed before generation.
- IDE navigation, refactoring, and type checking improve the authoring experience.
- Generators must not infer competing structural truth from generated files.
- External formats may import or export metadata but remain derived representations.

## Alternatives Considered

### YAML or JSON as the primary source

Rejected because it provides weaker typing and refactoring support and creates a
separate schema and tooling burden.

### Domain classes as the primary source

Rejected because persistence-oriented domain types do not express the complete
platform, service, security, generation, and UI model.

### Hybrid authority

Rejected because conflicting sources require precedence rules and reduce
determinism.
