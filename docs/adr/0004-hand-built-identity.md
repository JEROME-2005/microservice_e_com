# ADR-0004: Hand-built JWT + refresh tokens before OpenIddict
Date: 2026-11-11 · Status: Accepted
## Context
OpenIddict needs an interactive login UI for Authorization-Code flow — too much to learn at once.
## Decision
Identity issues RS256 JWTs itself (ASP.NET Core Identity for users/passwords), rotates refresh tokens with reuse detection, blacklists logged-out `jti`s and publishes a JWKS document.
## Alternatives
- OpenIddict now · Keycloak · a hosted IdP.
## Consequences
+ We learn what OIDC servers automate; the public `/api/v1/auth/*` contract stays identical if we migrate in Phase 6. − We own the security of the token code (covered by tests).
