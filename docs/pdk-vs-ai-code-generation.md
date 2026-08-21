# PDK and AI Code Generation

PDK and AI coding tools solve different problems and can be used together.

## PDK Provides Deterministic Architecture

PDK starts from strongly typed definitions and applies versioned generators and
conventions. Given the same definitions, PDK version, and options, generation is
intended to produce the same infrastructure every time.

This is useful for concerns that must remain consistent across many services and
teams:

- Project structure
- Entity and persistence metadata
- Contracts and API infrastructure
- Permissions and security metadata
- Naming and architectural conventions
- Regeneration and upgrade workflows

Generated PDK files are disposable and are not manually edited.

## AI Provides Contextual Assistance

AI tools are useful for tasks that require interpretation, exploration, or
developer judgment:

- Drafting domain behavior
- Explaining unfamiliar code
- Suggesting tests and refactorings
- Creating documentation
- Exploring design alternatives
- Assisting with migrations and integrations

AI output can vary between runs and should be reviewed like other contributed
code. It should not silently become the architectural source of truth.

## Recommended Responsibility Boundary

| Concern | PDK | AI tool | Developer |
| --- | --- | --- | --- |
| Structural metadata | Defines and validates the model | Helps author definitions | Approves intent |
| Repeatable infrastructure | Generates deterministically | Helps inspect or troubleshoot | Reviews generated contracts |
| Business rules | Provides extension points | Can draft implementations | Owns and validates behavior |
| Architecture decisions | Enforces accepted conventions | Explores alternatives | Accepts and records decisions |
| Tests | Can generate conventional infrastructure tests | Can propose scenarios | Owns correctness and coverage |

## Working Together

A productive workflow is:

1. Developers record architectural intent in PDK definitions and ADRs.
2. PDK validates definitions and generates conventional infrastructure.
3. AI assists with developer-owned code, tests, documentation, and analysis.
4. Developers review all behavior and update definitions when structure changes.
5. PDK regenerates infrastructure from the updated source of truth.

AI should not edit `.g.cs` files because those changes will be lost during
regeneration. It should modify definitions, generator templates, or companion
partials intended for customization. Editing a hash-baselined companion changes its
body hash, so API cleanup preserves it as developer-owned code.

## Why Not Use AI Alone?

AI can produce boilerplate quickly, but independently generated implementations
may drift across services and over time. PDK provides a stable metadata model,
repeatable conventions, diagnostics, and a controlled regeneration lifecycle.

## Why Not Use PDK Alone?

PDK intentionally does not generate business logic. Domain behavior requires
product context and human accountability. AI can accelerate that work, while
developers remain responsible for correctness.

See the [PDK Architecture Principles](architecture/principles.md) for the source
of truth and generated-code policies.
