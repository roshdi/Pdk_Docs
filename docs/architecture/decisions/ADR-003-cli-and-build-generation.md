# ADR-003: Support CLI and Build-Time Generation

- Status: Accepted
- Date: 2026-07-17

## Context

Developers need explicit generation for scaffolding, troubleshooting, and
automation. They also need generated artifacts to remain synchronized during
ordinary builds without relying on a remembered manual step.

Independent implementations for CLI and build generation could produce
different output or validation behavior.

## Decision

PDK supports both explicit CLI generation and build-integrated generation.

Both invocation paths must call the same discovery, validation, metadata, and
generation pipeline. Given the same definitions, PDK version, and options, both
paths must produce equivalent output and diagnostics.

The current implementation has two build-triggered commands:

- `pdk generate platform` consumes the compiled Platform assembly and creates or
  updates platform and service structure.
- `pdk generate api` consumes a compiled service Core assembly and generates that
  service's API features and module registration.

Both commands are also available for explicit invocation.

## Consequences

- Generation logic belongs in reusable services rather than command handlers or MSBuild tasks.
- Invocation-specific code is limited to input collection, environment setup, reporting, and exit behavior.
- Build integration must avoid recursive builds and unnecessary regeneration.
- CLI commands remain useful for diagnostics and CI workflows.
- Equivalence should be covered by automated tests.

## Alternatives Considered

### CLI-only generation

Rejected because generated output can become stale when developers forget to run
the command.

### Build-only generation

Rejected because explicit generation is valuable for scaffolding, debugging,
automation, and inspecting failures independently of a full build.

### Separate CLI and build pipelines

Rejected because duplicated logic would drift and undermine deterministic output.
