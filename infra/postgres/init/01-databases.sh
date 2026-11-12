#!/bin/bash
# Runs ONCE, on the first start of an EMPTY data volume (docker-compose, the Aspire AppHost and the staging StatefulSet).
# Creates one ROLE and one DATABASE per service (Phase 2: database-per-service — docs/adr/0019-database-per-service.md).
# Dev passwords default to "<svc>_dev_pw"; staging passes DB_PASSWORD_<SVC> via a Kubernetes Secret (scripts/k8s-secrets.sh).
#
# The body runs in a subshell: the postgres image *sources* init scripts that aren't executable, and `set -u` must not leak
# into the image's own entrypoint.
(
  set -euo pipefail

  for svc in identity users catalog inventory orders payments notifications; do
    up=$(echo "$svc" | tr '[:lower:]' '[:upper:]')
    eval "pw=\${DB_PASSWORD_${up}:-${svc}_dev_pw}"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<SQL
      CREATE ROLE ${svc}_svc LOGIN PASSWORD '${pw}';
      CREATE DATABASE ${svc}_db OWNER ${svc}_svc;
      REVOKE CONNECT ON DATABASE ${svc}_db FROM PUBLIC;   -- nobody but the owner (and the superuser) may even connect
      ALTER ROLE ${svc}_svc IN DATABASE ${svc}_db SET search_path = ${svc}, public;   -- psql convenience; EF always schema-qualifies
SQL
  done

  # pg_trgm (fast ILIKE search) must be installed by a superuser, inside the catalog database (schema public)
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname catalog_db -c "CREATE EXTENSION IF NOT EXISTS pg_trgm;"
)
