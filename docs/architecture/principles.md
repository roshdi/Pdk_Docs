# PDK Architecture Principles

These principles guide implementation and review. A deliberate exception should be
discussed explicitly and recorded in an ADR when it creates a durable constraint.

## 1. Definitions own structure; code owns behavior

Strongly typed C# definitions are authoritative for platform, service, entity,
feature, security, seed, and infrastructure structure. Business rules and workflows
belong in developer-owned code. Runtime configuration supplies environment values but
does not redefine structure.

## 2. One metadata model drives generation

Definition discovery and validation produce one metadata graph. Project, persistence,
API, permission, and seed generation derive from that graph rather than maintaining
independent interpretations.

## 3. Generated code is disposable

Generated artifacts may be deleted and recreated. Developers do not edit them.
Custom behavior lives in companion partial files or explicit extension points that
regeneration does not overwrite. An untouched, hash-baselined companion is still a
generated scaffold; a modified companion is preserved as developer-owned code.

## 4. State intent once

Definitions should be concise and strongly typed. PDK derives conventional names,
defaults, and related metadata. Overrides are appropriate only where application
variation justifies them; two settings must not control the same concern.

## 5. Diagnose before writing

Invalid definitions should produce stable, actionable diagnostics before generation.
Expected definition errors should be collected where practical instead of surfacing as
unrelated reflection, template, or runtime failures.

## 6. Generation is deterministic and idempotent

The same definitions, PDK version, and options should produce the same output. Repeated
generation should not rewrite unchanged files or create duplicate declarations.
Cleanup must distinguish replaceable generated artifacts, untouched generated
scaffolds, and developer-modified companions without depending on generation history.

## 7. CLI and build integration are equivalent

Explicit CLI and MSBuild-triggered generation use the same discovery, validation,
metadata, and template pipeline. Invocation method must not change output.

## 8. Definition compatibility has priority

Definition APIs are the primary compatibility boundary. Generated implementation
details may evolve when definitions retain their meaning and regeneration remains
safe. Breaking definition changes require migration guidance and an ADR.

## 9. Authorization fails closed

Missing, empty, malformed, unknown, or incompatible security data must not widen
access. Unrestricted access is represented only by the explicit `All` scope. A
data-dependent scope without valid effective data contributes no accessible records.

## 10. Authorization decisions preserve their origin

Effective permissions may come from roles, direct user grants, or permission
implication. Authorization must preserve enough provenance to remove only the access
derived from an invalidated or denied root grant. Flattened permission sets must not
be treated as the authoritative authorization model.
