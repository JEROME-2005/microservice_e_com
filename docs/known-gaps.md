# Known gaps

Deliberate shortcuts, each with the phase that fixes it. An honest gap list is a sign of maturity, not weakness.
Started from the Phase 1 register (build guide §1.22); updated as Phase 2 closes items (build guide Phase 2 §I.3).

| # | Gap | Risk today | Status / fixed in |
|---|---|---|---|
| 1 | Publish-after-commit (dual write) everywhere | lost event after a crash at the wrong millisecond | ✅ **closed** in Phase 2 — transactional outbox + inbox in every service ([ADR-0020](adr/0020-outbox-inbox.md)) |
| 2 | OTPs are logged, not sent | dev-only | open → Phase 2 Sprint 3 (Notification service) |
| 3 | Shared internal API key | one leaked key opens all internal APIs | open → Phase 6 (mTLS / mesh) |
| 4 | Refresh token in `localStorage` (web) | XSS could steal it | open → Phase 6 (httpOnly cookie) |
| 5 | Mock webhook jobs held in memory | lost on restart (mock only) | n/a (real gateways retry) |
| 6 | Redis↔Postgres stock drift only self-heals via TTL | rare mis-counts | open → Phase 2 Sprint 4 (reconciler) |
| 7 | KYC data (NIC, bank no.) stored in clear text | PII exposure | open → Phase 3/6 (encryption + Vault) |
| 8 | No MFA for sellers/admins | account takeover | open → Phase 3/6 |
| 9 | Client IP from `X-Forwarded-For` is spoofable if services are reachable directly | audit/rate-limit evasion | open → Phase 6 |
| 10 | Shipments mirror the order status | multi-seller orders can't progress independently | open → Phase 4 (Logistics) |
| 11 | Single currency, flat delivery fee | — | open |
| 12 | Auto-migrate on startup | races with multiple replicas | ✅ **closed** in Phase 2 — `--migrate-only` jobs, auto-migrate only in Development ([ADR-0019](adr/0019-database-per-service.md)) |
| 13 | Gateway cannot revoke by user (only by `jti`) | suspended user keeps token ≤ 15 min | open → Phase 6 |
| 14 | Outbox delivery lag is only checked by hand (`SELECT now() - min(enqueue_time) FROM <schema>.outbox_message`) | a stuck broker/delivery service goes unnoticed until someone looks | new → Phase 6 (alerts) |
