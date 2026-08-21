# Architecture Decision Records

Architecture Decision Records (ADRs) preserve the context and consequences of
significant PDK decisions.

## Rules

1. Use the next available three-digit number.
2. Use a short lowercase filename: `ADR-NNN-description.md`.
3. Start new records as **Proposed**.
4. Change the status to **Accepted** or **Rejected** after a decision.
5. Do not rewrite an accepted decision to change history.
6. Replace an accepted decision by adding a new ADR and marking the old one **Superseded by ADR-NNN**.

## Template

```markdown
# ADR-NNN: Decision Title

- Status: Proposed
- Date: YYYY-MM-DD

## Context

What problem or constraint requires a decision?

## Decision

What approach will PDK adopt?

## Consequences

What becomes easier, harder, required, or prohibited?

## Alternatives Considered

Which credible alternatives were rejected, and why?
```

## Decision Index

| ADR | Status | Decision |
| --- | --- | --- |
| [ADR-001](ADR-001-strongly-typed-definitions.md) | Accepted | Strongly typed definitions are the structural source of truth |
| [ADR-002](ADR-002-disposable-generated-code.md) | Accepted | Replaceable output and hash-baselined companions have explicit ownership rules |
| [ADR-003](ADR-003-cli-and-build-generation.md) | Accepted | CLI and build-time generation are both supported and equivalent |
| [ADR-004](ADR-004-permission-scope-and-denial-model.md) | Accepted | Permissions use composable scopes, provenance-preserving implication, and user-only denial |
| [ADR-005](ADR-005-generated-angular-artifacts.md) | Accepted | Angular generated artifacts are isolated, marked, and manifest-owned |
