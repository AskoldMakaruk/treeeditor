# Tree Editor

A web application that edits a large, arbitrarily deep tree stored in a relational database.
The tree is loaded lazily level by level, so it never loads in full. You edit it directly:

- Expand nodes on demand; each expansion loads only that level into the client-side cache.
- Edit values, add children and delete nodes right in the tree; everything is held locally until you
  press **Apply**. **Reset** restores the initial sample data.

Because the client cache is keyed by element id and each element carries its `parentId`, separately
loaded elements nest correctly, and a tree of thousands of nodes stays responsive.

Stack: **Svelte 5 + Vite** (frontend), **ASP.NET Core (.NET 10)** (API), **PostgreSQL + EF Core**
(persistence), **Redis** (read cache + distributed lock), **Nix + devenv** (dev environment).

---

## Quick start (one command, with devenv)

Requires [Nix](https://nixos.org/download) with flakes enabled.

```bash
devenv up
```

That's it — from the project root, `devenv up` starts **PostgreSQL + Redis + the API + the UI**
(and creates the `treeeditor` role/database on first run):

- UI: <http://localhost:5173>
- API: <http://localhost:5080>
- Redis: `127.0.0.1:6379`, PostgreSQL: `127.0.0.1:5432`

The API applies EF migrations and seeds the sample tree on first start.

**Entering a dev shell (optional).** To get the toolchains in your shell:

```bash
direnv allow                     # loads the shell automatically (recommended)
# or
nix develop --no-pure-eval       # note the flag
```

Then run `devenv up` from inside. Bare `nix develop` **will fail** with
"devenv was not able to determine the current directory" — that is a devenv + Flakes limitation:
Flakes evaluate purely by default and devenv must read the working directory, so the
`--no-pure-eval` flag (or direnv, which passes it) is required. The `devenv` CLI does this itself,
which is why `devenv up` works without entering a shell.

Other helpers: `devenv test` (backend tests), `devenv run migrate`, `devenv run reset-data`.

> **Troubleshooting.** If port 5432 is already in use, devenv allocates another port and exports
> `PGPORT`; the API follows it automatically. If you previously started an older configuration and
> the database looks wrong, stop the stack and run `rm -rf .devenv` to recreate it from scratch.

`devenv up` starts **PostgreSQL + Redis + the API + the UI**:

- UI: <http://localhost:5173>
- API: <http://localhost:5080>
- Redis: `127.0.0.1:6379`, PostgreSQL: `127.0.0.1:5432`

The API applies EF migrations and seeds the sample tree on first start. Other helpers:
`devenv test` (backend tests), `devenv run migrate`, `devenv run reset-data`.

---

## Quick start (no devenv: Docker + .NET + Node)

Requirements: **Docker**, **.NET 10 SDK**, **Node.js 20+**.

```bash
# 1. start PostgreSQL and Redis
docker compose up -d

# 2. start the API (applies migrations + seeds on startup)
dotnet run --project backend/src/TreeEditor.Api

# 3. in another terminal, start the UI
cd frontend
npm install
npm run dev
```

Then open <http://localhost:5173>. The API listens on <http://localhost:5080>.

There is also a `Makefile` with the same steps: `make services`, `make api`, `make web`.

> Both paths use the same credentials:
> `Host=localhost;Port=5432;Database=treeeditor;Username=treeeditor;Password=treeeditor`.
> With Docker they are set by `docker-compose.yml`; with devenv, `devenv.nix` creates the
> `treeeditor` role (with that password) and the `treeeditor` database on first start.
> The API retries the initial database connection for up to ~60s, so it is safe for
> `devenv up` to start PostgreSQL and the API at the same time.

---

## Trying the behaviour

1. Click a chevron to expand a node. That level is loaded from the database into the client cache and
   shown — nothing more is fetched.
2. Expand a deep branch (e.g. `packages/svelte/src/internal/...`) and a shallow one in either order.
   Everything you reveal is edited in place; nodes nest by their loaded parent.
3. Edit a value (tagged **edit**), add a child (tagged **new**), delete a node (tagged **deleted**).
   Nothing reaches the database yet.
4. Press **Apply**. Changes are sent as one batch and applied atomically.
5. Delete a node that has children you never loaded — after **Apply** the whole subtree is gone from
   the database.
6. Press **Reset** to restore the sample data.

The sample data is the directory/file tree of `sveltejs/svelte` (see below): **13,003 elements**,
**10 levels deep**, a single root, so lazy loading is exercised at scale.

---

## Architecture

Layered solution with dependencies pointing inward:

```
backend/src/
  TreeEditor.Domain          entities + domain rules (no dependencies)
  TreeEditor.Application     interfaces (ports), DTOs, use-case services
  TreeEditor.Infrastructure  EF Core, migrations, Redis cache/lock, seeding
  TreeEditor.Api             ASP.NET Core host, MVC controllers, DI, error mapping
```

- **Application** defines the ports: `IElementRepository`, `ICacheService`, `IDistributedLock`,
  `ITreeQueryService`, `IApplyService`, `IResetService`, `ISampleDataProvider`, `IDatabaseInitializer`.
- **Infrastructure** implements them (`EfElementRepository`, `RedisCacheService`,
  `RedisDistributedLock`, …) and is wired up in `DependencyInjection.AddInfrastructure`.
- **Api** depends on Application + Infrastructure and exposes attribute-routed MVC controllers
  (`TreeController`, `AdminController`, `HealthController`).

### Redis is used in two ways

1. **Read-through cache** for `GET /tree/{id}` and `GET /tree/{id}/children` (`ICacheService`).
   Keys are under the `tree:` prefix and are invalidated on every Apply and flushed on Reset.
2. **Distributed lock** (`IDistributedLock`) serialises Apply and Reset (`SET NX PX` via
   `IDatabase.LockTakeAsync`).

Both degrade gracefully: if Redis is unreachable the cache becomes a no-op and the lock falls back to
an in-process semaphore (this is what the integration tests use via `Redis:Enabled=false`).

### Database schema

Single self-referencing table `elements`:

| column       | type        | notes                              |
| ------------ | ----------- | ---------------------------------- |
| `id`         | integer PK  | identity (`GENERATED BY DEFAULT`)  |
| `value`      | text        | element value, not empty           |
| `parent_id`  | integer FK  | self reference, `NULL` = root      |
| `is_deleted` | boolean     | soft-delete flag, default `false`  |
| `version`    | bigint      | tree revision the row last changed at |
| `created_at` | timestamptz |                                    |
| `updated_at` | timestamptz |                                    |

Plus a single-row `tree_revision(id, revision)` table holding the monotonic tree revision.

- Index on `parent_id`.
- `DELETE` behaviour is `RESTRICT`; deletion is done explicitly (see below).
- A global EF query filter (`e => !e.IsDeleted`) hides soft-deleted rows everywhere.

### Sample data

- Source of truth: **`data/sample-tree.json`** — the directory/file tree of
  `github:sveltejs/svelte@707c2814` (pinned), rewritten into `{id, value, parentId}` elements:
  **13,003 nodes**, **10 levels deep**, single root. Stored in the repo (~680 KB) and embedded into
  `TreeEditor.Infrastructure` as a resource, so seeding never depends on the network.
- Regenerate it with `dotnet run scripts/build-sample-tree.cs` (a .NET 10 single-file app; edit the
  `Repo`/`Commit` constants inside for a different tree). It is pinned to the same commit, so it
  reproduces the committed file byte-for-byte.
- `SampleDataProvider` reads the embedded file; `EfElementRepository.AddRangeAsync` seeds it with a
  Postgres **binary `COPY`** (not EF change tracking), so inserting all 13k rows — and **Reset** —
  takes well under a second.
- Because the tree is large and deep, it only ever loads the levels you expand.

### Important implementation decisions

- **Parent links are immutable.** There is no re-parent operation; elements can only be created under
  a parent, renamed, or deleted — matching the requirement.
- **Deletion cascades to unloaded descendants.** Apply runs a single `WITH RECURSIVE` CTE that marks
  the node and every descendant as deleted in one statement, so descendants that were never loaded
  into the cache are still removed. Deleted rows are soft-deleted and immediately invisible/uneditable.
- **Apply is atomic.** All updates, additions and deletions run in one transaction while holding the
  lock; any invalid operation rolls the whole batch back (covered by a test). Additions may reference
  another pending addition by its temporary (negative) id, so a child can be added to an unsaved node.
- **The tree view is the cache.** There is a single tree; expanding a node lazily loads its children
  into the client cache, and edits/additions/deletions stay there until Apply. The browser only calls
  the API when loading a level, applying changes, or reconciling versions. The hierarchy is rebuilt
  from the flat cached element list by `parentId` (`frontend/src/lib/components/TreeView.svelte`),
  which is why separately loaded elements nest correctly.
- **Reset** truncates the table (restarting the identity sequence), re-inserts the deterministic sample
  data and flushes the Redis cache.

---

## Multi-client sync (version check + real-time)

Yes — this is viable for a **partially loaded** tree, provided the check is **per node**. A single
whole-tree version would be useless here: any change would invalidate everything and force a full
reload of a tree that is "too large to load in full". Per-node versions let a client ask only about
the ids it actually holds.

**How it works**

- Every element carries a `version` — the global tree revision it was last changed at — and the
  `tree_revision` counter increments on each Apply/Reset. Change detection is a plain version
  comparison (**no hashing**): a node is stale iff its server version differs from the local one.
- **Structural changes are visible too:** adding/removing a child bumps the *parent's* version, so the
  parent's version changes and clients know to refetch that level. A recursive delete stamps the subtree.
- `POST /api/tree/check` takes the ids a client holds and returns `{ revision, nodes:[{id,version}], deleted }`.
  The client refetches only nodes whose version differs and drops the ones reported as `deleted`
  (reported per id, so a client never has to have loaded a whole subtree to notice it is gone).
- **Real-time propagation:** when any client applies, the server broadcasts `TreeChanged { revision,
  changedIds }` over SignalR (`/hubs/tree`, with a Redis backplane so it reaches clients on every API
  instance). Receiving clients run the version check.
- **After a lost connection:** `withAutomaticReconnect` fires `onreconnected`, which runs the same
  version check, so changes made while offline are picked up. The header shows a **Live/Offline** indicator.
- **Conflicts:** if a node changed on the server while the client has an unsaved local edit, the local
  value is kept and the row is flagged **conflict** rather than silently overwritten.

The check is deliberately cheap: one indexed row per requested id, no child queries, and the returned
payload is only `{id, version}` pairs. The client decides what to refetch.

---

## API

| Method | Route                        | Description                                   |
| ------ | ---------------------------- | --------------------------------------------- |
| GET    | `/api/tree/roots`            | Root elements (with `hasChildren` and `version`). |
| GET    | `/api/tree/{id}`             | A single element (used to load into cache).   |
| GET    | `/api/tree/{id}/children`    | Direct children only (lazy expand).           |
| POST   | `/api/tree/apply`            | Apply pending updates/additions/deletions.    |
| GET    | `/api/tree/revision`         | Current global tree revision.                 |
| POST   | `/api/tree/check`            | Version check for a set of ids (see below).   |
| POST   | `/api/admin/reset`           | Restore the initial sample data.              |
| GET    | `/api/health`                | Health probe.                                 |
| WS     | `/hubs/tree`                 | SignalR hub pushing `TreeChanged` events.     |

`POST /api/tree/apply` body:

```json
{
  "updates": [{ "id": 42, "value": "renamed-element" }],
  "additions": [{ "tempId": -1, "parentId": 3, "value": "Databases" }],
  "deletions": [8]
}
```

Response: counts plus a `tempId → id` map used to rebase the client cache.

---

## Tests

Backend integration tests boot the real API against a throwaway PostgreSQL database
(`treeeditor_test`), created automatically. Redis is disabled so a cache/lock no-op is used.

```bash
devenv test                      # with devenv
# or
dotnet test backend/TreeEditor.slnx
```

Covered: lazy roots/children, apply update, add child, nested additions, recursive cascade delete of
unloaded descendants, deleted elements being uneditable, atomic rollback on an invalid operation, and
Reset restoring the sample data.

---

## Configuration reference

| Setting                      | Default (Docker / plain)                                                    |
| ---------------------------- | --------------------------------------------------------------------------- |
| `ConnectionStrings:Default`  | `Host=localhost;Port=5432;Database=treeeditor;Username=treeeditor;Password=treeeditor` |
| `Redis:Configuration`        | `localhost:6379`                                                            |
| `Redis:Enabled`              | `true`                                                                      |
| `VITE_API_URL` (frontend)    | `http://localhost:5080`                                                     |

Redis can be turned off with `Redis__Enabled=false`; the app still runs (no cache, in-process lock).
