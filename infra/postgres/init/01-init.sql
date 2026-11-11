-- Runs once, against the POSTGRES_DB database, when the data volume is empty.
-- Used by BOTH docker-compose.yml and the Aspire AppHost (WithInitFiles).
-- DEV-ONLY passwords. Production gets real secrets from Vault/K8s Secrets (Phase 6).

CREATE EXTENSION IF NOT EXISTS pg_trgm;   -- trigram index: fast ILIKE '%text%' search (Catalog)

DO $$
DECLARE
  svc text;
BEGIN
  FOREACH svc IN ARRAY ARRAY['identity','users','catalog','inventory','orders','payments']
  LOOP
    EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', svc || '_svc', svc || '_dev_pw');
    EXECUTE format('CREATE SCHEMA %I AUTHORIZATION %I', svc, svc || '_svc');
    -- each service sees its own schema first, then public (where pg_trgm lives)
    EXECUTE format('ALTER ROLE %I SET search_path = %I, public', svc || '_svc', svc);
    -- lets EF Core run "CREATE SCHEMA IF NOT EXISTS" during migrations (dev convenience)
    EXECUTE format('GRANT CREATE ON DATABASE %I TO %I', current_database(), svc || '_svc');
  END LOOP;
END $$;
