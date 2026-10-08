# Convenience targets for the no-devenv (Docker) workflow.
# With devenv, prefer: `devenv up`.

.PHONY: services services-down api web install build test reset

services:
	docker compose up -d

services-down:
	docker compose down

install:
	npm --prefix frontend install

api:
	dotnet run --project backend/src/TreeEditor.Api

web:
	npm --prefix frontend run dev

build:
	dotnet build backend/TreeEditor.slnx
	npm --prefix frontend run build

test:
	dotnet test backend/TreeEditor.slnx

reset:
	curl -fsS -X POST http://localhost:5080/api/admin/reset
