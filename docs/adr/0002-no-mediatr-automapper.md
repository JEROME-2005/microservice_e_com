# ADR-0002: No MediatR / AutoMapper — plain feature classes
Date: 2026-11-11 · Status: Accepted
## Context
Both libraries moved to commercial licensing. For a solo learner they add indirection without adding understanding.
## Decision
Use small per-use-case "feature" files (endpoint + handler + DTO + validator) and hand-written mapping. CQRS-lite is a folder convention, not a library.
## Alternatives
- MediatR (commercial), Mediator source generator (extra concept), AutoMapper (commercial, hides mapping bugs).
## Consequences
+ Everything is explicit and greppable. − Slightly more mapping code.
