# ADR-005: Isolate and Mark Generated Angular Artifacts

- Status: Accepted
- Date: 2026-08-04

## Context

PDK now emits Angular page, grid-controller, definition, and HTML artifacts. They
must follow the disposable-generation rule while allowing developer-owned Angular
customization beside generated structure.

## Decision

Generated Angular artifacts live below `src/app/generated/`. TypeScript files use
`.g.ts` and the `// <pdk-auto-generated />` first-line marker. HTML files use
`.g.html` and the `<!-- <pdk-auto-generated /> -->` first-line marker. Hash-baselined
scaffolds (`.ts`, `.html`, `.scss`) carry a content-hash marker; SCSS files use the
`/* pdk-auto-generated algorithm="sha256" value="..." */` block-comment form.

Cleanup identifies artifacts by their first-line marker. Fully generated companions
(`.g.ts`, `.g.html`, `.base.ts`) carry the plain `// <pdk-auto-generated />` marker
and are always replaceable. Scaffold files carry a content-hash marker and are
deleted only when the stored hash still matches the file content, preserving
developer-modified files. On each run the generator removes marked files under
`src/` that were not produced by that run; no manifest is persisted.

Generated form definitions and form page components/templates are also fully
replaceable even though their route-facing filenames do not use a `.g` or `.base`
suffix. The form controller (`*-form.controller.ts`) is their only hash-baselined,
developer-customizable scaffold.

Layout scaffolds are generated under `src/app/layouts/<name>/` from empty or
non-empty templates and follow the same hash-baselined policy: `.ts`, `.html`,
and `.scss` files carry their content-hash marker and are refreshed only while
unmodified.

Developer behavior remains outside the generated directory and integrates through
dependency injection, companion routes/components, commands, formatters, definition
transforms, and presentation renderer registrations. Generators do not merge source.

## Consequences

Generated UI output can be deleted, recreated, and cleaned safely. Developers must
not edit generated files. Additional generated formats require an ownership marker
and cleanup policy before adoption.

## Alternatives Considered

Filename-only ownership was rejected because it is insufficient evidence for
deletion. Source merging and generate-once files were rejected for the reasons in
ADR-002.
