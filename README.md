# Tree Editor

A web app that lazily edits a large, arbitrarily deep tree stored in PostgreSQL.

## Stack

- **Frontend:** Svelte 5 + Vite
- **Backend:** ASP.NET Core (.NET 10)
- **Persistence:** PostgreSQL + EF Core
- **Realtime/cache:** Redis + SignalR
- **Environment:** Nix + devenv

## Run with devenv

Requires [Nix](https://nixos.org/download) with flakes enabled.

```bash
devenv up
```

Starts PostgreSQL, Redis, the API and the UI:

- UI: <http://localhost:5173>
- API: <http://localhost:5080>

The API applies EF migrations and seeds the sample tree on first start.

For a shell (`direnv allow`, or `nix develop --no-pure-eval`), then run `devenv up` inside.

## Run with Docker

Requires Docker only — one command starts PostgreSQL, Redis, the API and the UI.

```bash
docker compose up --build -d
```

- UI: <http://localhost:5173>
- API: <http://localhost:5080>

The API applies EF migrations and seeds the sample tree on first start. Stop with
`docker compose down` (add `-v` to also delete the database volume).

## Test

Backend tests need a running PostgreSQL (integration tests create a throwaway `treeeditor_test` database).

```bash
devenv test                      # with devenv
# or
dotnet test backend/TreeEditor.slnx
```
