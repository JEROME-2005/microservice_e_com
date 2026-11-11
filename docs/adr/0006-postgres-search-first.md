# ADR-0006: Postgres ILIKE + pg_trgm now, Meilisearch in Phase 3
Date: 2026-11-11 · Status: Accepted
## Context
Phase 1 is single-language; multilingual analyzers are a Phase 3 requirement.
## Decision
Catalog search uses `ILIKE` with escaped wildcards and a trigram GIN index.
## Alternatives
- Meilisearch/OpenSearch now — infrastructure we cannot use yet.
## Consequences
+ One less moving part. − Relevance ranking is basic until Phase 3.
