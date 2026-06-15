-- lupira-cal-api: provision the `lupira_cal` database on the shared medelynas-db.
-- One role, one logical database. EF Core owns the `cal` schema, tables, and indexes — all created by
-- `--apply-schema` (the regenerated Initial migration), not here.
--
-- Apply (TrueNAS Shell), substituting a freshly generated password:
--   LUPIRA_CAL_DB_PW="$(openssl rand -hex 32)"; echo "$LUPIRA_CAL_DB_PW"   # save to your password manager
--   docker exec -i medelynas-db psql -U medelynas_admin -v app_password="'$LUPIRA_CAL_DB_PW'" postgres < grants.sql

CREATE ROLE lupira_cal_user WITH LOGIN PASSWORD :'app_password';
CREATE DATABASE lupira_cal OWNER lupira_cal_user;
REVOKE ALL ON DATABASE lupira_cal FROM PUBLIC;
GRANT CONNECT ON DATABASE lupira_cal TO lupira_cal_user;

-- pg_trgm (typo-tolerant fuzzy search) is declared in the EF model, so `--apply-schema` creates it too;
-- pre-creating it here as admin just keeps it independent of the app role's CREATE EXTENSION privilege.
\connect lupira_cal
CREATE EXTENSION IF NOT EXISTS pg_trgm;
