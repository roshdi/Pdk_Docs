# PDK Vision

## Mission

Prime Development Kit (PDK) is an opinionated .NET development platform for
long-lived business applications. Teams describe application structure with strongly
typed C# definitions; PDK validates those definitions and generates consistent
application infrastructure.

PDK reduces repeated work around project structure, APIs, persistence, permissions,
mapping, seeding, and service hosting so developers can focus on domain behavior and
business workflows.

## Audience

PDK primarily serves enterprise .NET teams that need:

- consistent service boundaries and implementation conventions;
- compile-time feedback and refactoring support;
- safe regeneration as applications evolve;
- support for both a combined application and independently hosted services;
- a clear boundary between generated infrastructure and handwritten behavior.

## Product scope

PDK combines:

- definition libraries and a derived metadata model;
- a CLI and build-integrated generators;
- generated platform and service projects;
- runtime libraries used by generated applications;
- conventions for persistence, APIs, security, seeding, and hosting.

These parts form one workflow rather than independent products.

## Goals

PDK should:

1. Remove repetitive application infrastructure.
2. Enforce understandable conventions across services and contributors.
3. Keep structure strongly typed, reviewable, and diagnosable.
4. Make generated artifacts safe to replace.
5. Keep domain rules in developer-owned code.
6. Support modular-monolith and independent-service hosting from the same modules.
7. Produce equivalent results from explicit and build-integrated generation.
8. Preserve the meaning of existing definitions as PDK evolves.

## Non-goals

PDK does not invent domain rules, workflows, or migration policy. It provides
infrastructure and extension points through which applications implement them.

PDK does not aim to support every .NET architecture. Predictable conventions and a
coherent migration path are more important than unrestricted structural choice.

## Success criteria

PDK succeeds when teams can describe a service with substantially less infrastructure
code, regenerate it without losing handwritten behavior, run it alone or in the
combined platform host, and understand failures from actionable diagnostics.
