# ADR-0018: Angular client instead of React
Date: 2026-11-11 · Status: Accepted (supersedes the React choice in guide decision 014)
## Context
The guide builds a Vite + React storefront. The project owner prefers Angular, and the solution already contains an Angular 22 client.
## Decision
Keep `e_com_app.client` (Angular) as the web storefront. Its dev server proxies `/api` to the gateway (`src/proxy.conf.js`); the gateway CORS list includes its origin.
## Alternatives
- React per the guide.
## Consequences
+ One front-end stack the owner knows. − The guide's §1.13 React code must be translated (HTTP interceptor with single-flight refresh, auth service, checkout page) when the storefront is built.
