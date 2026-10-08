{ pkgs, ... }:

{
  # --- Toolchains ---------------------------------------------------------
  languages.dotnet = {
    enable = true;
    package = pkgs.dotnet-sdk_10;
  };

  languages.javascript = {
    enable = true;
    package = pkgs.nodejs_24;
    npm.enable = true;
  };

  # --- Services -----------------------------------------------------------
  services.postgres = {
    enable = true;
    listen_addresses = "127.0.0.1";
    port = 5432;
    # Explicit role + password so the connection string matches the Docker path
    # and never depends on implicit OS-user/trust authentication.
    initialDatabases = [
      {
        name = "treeeditor";
        user = "treeeditor";
        pass = "treeeditor";
      }
    ];
  };

  services.redis = {
    enable = true;
    bind = "127.0.0.1";
    port = 6379;
  };

  # --- Environment --------------------------------------------------------
  # Default for manual runs. The API process below overrides Port with the port
  # devenv actually allocated (see $PGPORT), because it may differ from 5432.
  env.ConnectionStrings__Default = "Host=127.0.0.1;Port=5432;Database=treeeditor;Username=treeeditor;Password=treeeditor";
  env.Redis__Configuration = "127.0.0.1:6379";
  env.ASPNETCORE_ENVIRONMENT = "Development";
  env.ASPNETCORE_URLS = "http://localhost:5080";

  # --- Processes: `devenv up` starts everything ---------------------------
  # Idempotently ensure the role/database exist. `initialDatabases` only runs on
  # first init, so this also heals a data directory created by an older config.
  processes.db-init.exec = ''
    set -e
    host="''${PGHOST:-127.0.0.1}"
    port="''${PGPORT:-5432}"
    until pg_isready -h "$host" -p "$port" -q; do sleep 1; done
    psql -w -h "$host" -p "$port" -d postgres -v ON_ERROR_STOP=1 -c "DO \$\$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'treeeditor') THEN CREATE ROLE treeeditor LOGIN PASSWORD 'treeeditor'; END IF; ALTER ROLE treeeditor CREATEDB; END \$\$;"
    if ! psql -w -h "$host" -p "$port" -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'treeeditor'" | grep -q 1; then
      psql -w -h "$host" -p "$port" -d postgres -c "CREATE DATABASE treeeditor OWNER treeeditor"
    fi
    echo "db-init: treeeditor role and database are ready"
  '';

  # Build the connection string from the runtime PGHOST/PGPORT so the API follows
  # the port devenv actually assigned (it falls back to another port if 5432 is busy).
  processes.api.exec = ''
    export ConnectionStrings__Default="Host=''${PGHOST:-127.0.0.1};Port=''${PGPORT:-5432};Database=treeeditor;Username=treeeditor;Password=treeeditor"
    exec dotnet run --project backend/src/TreeEditor.Api
  '';
  processes.web.exec = "npm --prefix frontend run dev";

  scripts = {
    dev.exec = "devenv up";
    migrate.exec = ''
      export ConnectionStrings__Default="Host=''${PGHOST:-127.0.0.1};Port=''${PGPORT:-5432};Database=treeeditor;Username=treeeditor;Password=treeeditor"
      dotnet ef database update --project backend/src/TreeEditor.Infrastructure --startup-project backend/src/TreeEditor.Api
    '';
    test.exec = ''
      export TEST_POSTGRES_ADMIN="Host=''${PGHOST:-127.0.0.1};Port=''${PGPORT:-5432};Database=postgres;Username=treeeditor;Password=treeeditor"
      export TEST_POSTGRES_APP="Host=''${PGHOST:-127.0.0.1};Port=''${PGPORT:-5432};Database={0};Username=treeeditor;Password=treeeditor"
      dotnet test backend/TreeEditor.slnx
    '';
    reset-data.exec = "curl -fsS -X POST http://localhost:5080/api/admin/reset";
  };

  enterShell = ''
    echo "Tree Editor dev environment"
    echo "  devenv up      # Postgres + Redis + API (http://localhost:5080) + UI (http://localhost:5173)"
    echo "  devenv test    # backend integration tests"
    echo "  devenv run migrate   # apply EF migrations manually"
  '';
}
