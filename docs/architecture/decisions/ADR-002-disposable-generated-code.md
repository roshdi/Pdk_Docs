# ADR-002: Generated Code Is Disposable

- Status: Accepted
- Date: 2026-07-17
- Updated: 2026-08-05

## Context

PDK must regenerate infrastructure as definitions and generators evolve. If
developers edit generated files, regeneration can overwrite business behavior
or require unreliable source merging.

## Decision

Fully generated artifacts such as `.g.cs` files must never be manually edited. They
are disposable and can be deleted and recreated from definitions. Companion
scaffolds are the explicit exception intended for developer customization.

Developer-owned behavior must live in partial classes, companion files, or explicit
extension points that generation does not overwrite. Generated files must carry a
recognizable marker where the file format permits.

The C# convention places fully replaceable output in `.g.cs` files and creates
companion partial `.cs` files as customization scaffolds. A new scaffold stores a
SHA-256 baseline of its generated body in a single first-line marker:

```csharp
// <pdk-auto-generated algorithm="sha256" value="..." />
```

Generation never overwrites an existing scaffold. Once its body differs from the
baseline, cleanup treats it as developer-owned. An untouched scaffold remains safely
disposable. Legacy scaffolds without a hash are preserved because their modification
state cannot be established. Content-aware writes avoid rewriting unchanged output.

API cleanup inventories marked files before generation and compares them with all
outputs expected by a successfully completed run, including existing scaffolds that
were not written. A stale `.g.cs` file may be deleted when it retains the PDK marker.
A stale scaffold may be deleted only when its body matches its embedded baseline.
Cleanup is constrained to API-owned paths and never runs after failed generation.

## Consequences

- Generators may replace fully generated files without preserving local edits.
- `.g.cs` names identify fully replaceable C# output.
- A scaffold's embedded baseline distinguishes an untouched generated scaffold from
  one containing developer changes.
- New generated behavior must expose stable extension points where customization is expected.
- Cleanup may delete only stale artifacts positively identified as generated and
  safe under the applicable ownership rule.
- Documentation must teach developers where custom behavior belongs.

## Alternatives Considered

### Preserve edits through text merging

Rejected because arbitrary source merging is fragile and makes output dependent
on generation history.

### Generate files only once

Rejected because created-once infrastructure cannot safely evolve with metadata
or generator improvements.

### Treat scaffolds as developer-owned without a baseline

Rejected as a default because ownership becomes ambiguous. PDK instead retains an
immutable generated baseline in companion metadata and preserves the file as soon as
its body differs from that baseline.
