# ADR-0008: Explicit orchestrated checkout saga
Date: 2026-11-11 · Status: Accepted
## Context
Checkout spans Catalog (price), Inventory (reserve) and Payments (intent) and must compensate on failure.
## Decision
Orders is the orchestrator: the order row is the saga state, transitions are compare-and-set (`UPDATE … WHERE status = expected`), and sweepers handle expiry.
## Alternatives
- MassTransit state-machine saga (DSL to learn first) · choreography only (hard to follow).
## Consequences
+ Readable, debuggable, unit-testable. − Optional refactor to a MassTransit saga after Phase 2 as an exercise.
