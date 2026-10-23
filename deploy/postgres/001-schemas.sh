#!/usr/bin/env bash
# PostgreSQL may source non-executable init scripts; isolate shell options from its entrypoint.
(
set -euo pipefail
# Only the bootstrap process receives all credentials. Runtime roles cannot create or cross schemas.
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set=ON_ERROR_STOP=1 \
  --set=app_password="$APP_DB_PASSWORD" --set=gateway_password="$GATEWAY_DB_PASSWORD" \
  --set=app_migrator_password="$APP_MIGRATOR_PASSWORD" \
  --set=gateway_migrator_password="$GATEWAY_MIGRATOR_PASSWORD" <<'SQL'
CREATE ROLE labaik_app LOGIN PASSWORD :'app_password';
CREATE ROLE labaik_gateway LOGIN PASSWORD :'gateway_password';
CREATE ROLE labaik_app_migrator LOGIN PASSWORD :'app_migrator_password';
CREATE ROLE labaik_gateway_migrator LOGIN PASSWORD :'gateway_migrator_password';
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA app AUTHORIZATION labaik_app_migrator;
CREATE SCHEMA fake_gateway AUTHORIZATION labaik_gateway_migrator;
REVOKE ALL ON SCHEMA app, fake_gateway FROM PUBLIC;
GRANT USAGE ON SCHEMA app TO labaik_app;
GRANT USAGE ON SCHEMA fake_gateway TO labaik_gateway;
ALTER DEFAULT PRIVILEGES FOR ROLE labaik_app_migrator IN SCHEMA app
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO labaik_app;
ALTER DEFAULT PRIVILEGES FOR ROLE labaik_app_migrator IN SCHEMA app
  GRANT USAGE, SELECT ON SEQUENCES TO labaik_app;
ALTER DEFAULT PRIVILEGES FOR ROLE labaik_gateway_migrator IN SCHEMA fake_gateway
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO labaik_gateway;
ALTER DEFAULT PRIVILEGES FOR ROLE labaik_gateway_migrator IN SCHEMA fake_gateway
  GRANT USAGE, SELECT ON SEQUENCES TO labaik_gateway;
SQL
)
