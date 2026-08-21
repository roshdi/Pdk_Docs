# PDK Architecture Open Questions

This file tracks unresolved topics only. A resolution becomes authoritative when it
is reflected in the current architecture and, when significant, an accepted ADR.

## Platform model

- Should a platform support multiple configured instances of the same service type?
- Which definition values are explicit contract, conventional default, or derived
  metadata?

## Generation and compatibility

- How should the API generator's snapshot-and-content-hash cleanup model extend to
  platform/project outputs and non-C# artifacts?
- How should PDK CLI, runtime package, template, and metadata-schema versions be
  coordinated and diagnosed?
- Which definition and generated extension-point compatibility guarantees begin with
  PDK 1.0, and what migration mechanism accompanies a breaking change?

## Extensibility

- Can third parties add definition DSLs, metadata processors, diagnostics, or
  generators?
- If so, how are extension ordering, compatibility, and conflicts handled?

## Runtime providers

- Which capabilities belong in PDK runtime libraries versus generated application
  code?
- What minimum behavior must each advertised database, messaging, caching, and
  observability provider implement?

## Distributed authorization

- When permission claims are disabled, what protocol and caching model should
  independently hosted services use to query Identity?
- How are authorization-data changes invalidated across independently hosted services
  when immediate propagation is required?

The cookie/JWT representation is no longer open: version 1 uses the signed
`pdk:authorization` effective-authorization snapshot defined in the
[security model](security-model.md). A future remote protocol may reuse its semantics
but requires a separate compatibility decision.
