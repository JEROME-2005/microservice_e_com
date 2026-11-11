# ADR-0007: Object storage choice deferred; MinIO excluded
Date: 2026-11-11 · Status: Accepted
## Context
The MinIO community project was archived (read-only) in April 2026. Phase 1 product images are plain URLs.
## Decision
No object storage in Phase 1. In Phase 3 pick an S3-compatible store (SeaweedFS or Garage) and code against the S3 API.
## Alternatives
- MinIO (no more images/security fixes).
## Consequences
+ Nothing to run now. − KYC documents and uploads wait for Phase 3.
