# PostgreSQL standalone storage

## Scope and observed result

On 2026-10-09 the local Phase 4B standalone fixture moved from eight SQLite databases to the existing native `MutSea.Data.PGSQL.dll` provider. The viewer protocol and loopback login URI are unchanged. PostgreSQL 16.14 runs in a dedicated container on a private Docker network, with no published database port. The simulator uses a non-superuser application role that owns its own database/schema. Neighboring databases and remote deployments were not changed.

This moves native accounts, authentication, inventory, appearance, assets, region/terrain/parcel/estate state, friends, grid-user state and the configured profile/preferences/presence services. The standalone region registry remains `NullRegionData`: it is rebuilt when this simulator registers its region. Finalverse WorldLine/content/citizen journals and script/cache files remain in the persistent runtime directory. The separate agent-runtime memory database remains SQLite; it needs an independent migration design and is not silently redirected by the simulator's configuration.

SQLite allows one writer per database file. Concurrent inventory, appearance, asset and region writes can contend on those files. PostgreSQL removes that particular bottleneck. It does not repair application bugs: the earlier pants-save defect was an inventory asset-reference acknowledgement failure in the viewer, and a native provider column-cache publication race affected both SQLite and PostgreSQL. Those fixes are retained.

## Native provider repairs

- Region migration 58 used MySQL backticks. PostgreSQL quoting is now correct; migration 59 repairs installations that already recorded version 58 without creating `StartStr`.
- AgentPrefs migration 2 and GridUserStore migration 3 convert selected fixed-width `char` fields to the same-width `varchar`, trimming padding. PostgreSQL/Npgsql otherwise returned padded strings that differed from native stored preferences, positions and timestamps.
- Generic-table and authentication column caches now publish a complete schema list under a private lock. Concurrent first requests cannot observe a half-populated list.

The inherited migration runner can catch SQL failures and still advance a version. `tools/MutSea.PgSchema` therefore checks current embedded versions, required tables and repaired column types after invoking it. This is a bounded readiness check, not a complete audit of every historical migration.

## Fresh configuration

Provision an empty database and a login role with `NOSUPERUSER NOCREATEDB NOCREATEROLE`, owned schema access and native migration permissions. Keep its administrator credentials separate. Do not reuse a neighboring application's database or expose port 5432 publicly.

Create a private JSON file outside Git with `host`, `database`, `user`, `password` and optional integer `port` (default 5432). The generator accepts DNS/IPv4 host strings, conventional database/role identifiers and a generated URL-safe password of 24–256 characters. It rejects INI/connection-string delimiters. Unix input mode must be 0600; output directory/files become 0700/0600. Remote PostgreSQL TLS settings are not supplied by this development generator; a remote/managed deployment needs a separately reviewed native connection string with verified TLS.

```sh
python3 deploy/standalone/configure.py \
  --host grid.example.org --public-tls \
  --database-config /private/mutsea/database.json \
  --output /private/mutsea/config
```

The database default is `postgresql`; a missing private database configuration fails rather than creating SQLite files. Use `--database sqlite` only when deliberately creating a separate legacy development fixture. The generator refuses a nonempty output directory and must never be run over an existing world.

The PostgreSQL profile explicitly sets `StorageProvider` and `ConnectionString` for DatabaseService, SimulationDataStore, EstateDataStore, AssetService, InventoryService, AvatarService, AuthenticationService, UserAccountService, GridUserService, FriendsService, PresenceService, UserProfilesService and AgentPreferencesService. Every connection string is double-quoted: **Nini treats unquoted semicolons as comments**, truncating an unquoted string at its first field. Never print generated INI files or `docker compose config` containing private paths/values into shared logs.

For a separately provisioned PostgreSQL container, set `MUTSEA_DATABASE_NETWORK` to its existing private Docker network and use:

```sh
docker compose -f deploy/standalone/compose.yaml \
  -f deploy/standalone/compose.postgresql.yaml up -d
```

The overlay attaches MutSea to both its ordinary network and the database network; it creates no database, changes no credentials and publishes no PostgreSQL port. PostgreSQL must be healthy before starting MutSea. Use a tested immutable image, private persistent database storage and independent health/backup supervision. The simulator container's UID remains 1000; arrange private config/runtime ownership accordingly.

## Bounded existing-fixture migration

The converter is intended for the audited local development fixture. It is **not** an automatic production migration or general converter for arbitrary SQLite worlds. Unsupported tables or nondefault unsupported columns fail closed.

1. Log out clients and stop the simulator normally through its native console `shutdown`. Preserve its previous image, stopped container, config and entire data volume. Snapshot them privately; an OAR alone omits accounts/inventory and is insufficient.
2. Create a separate PostgreSQL database/role and separate runtime/config volume. Do not overwrite the SQLite fixture. Copy durable Finalverse journals and script/runtime files; do not seed/reimport the scene or copy SQLite databases into the new runtime.
3. Build source-matched native components with `tools/MutSea.ComponentBuild/build.py`, then build `tools/MutSea.PgSchema` using `MutSeaBin` and `MutSeaComponents`. Run schema initialization inside a matching .NET 8 simulator environment on the private database network, passing only the private database JSON path.
4. With the target still offline, validate then transfer from read-only source snapshots:

```sh
python3 tools/MutSea.PostgreSQL/migrate_sqlite.py \
  --source-directory /private/backup/data \
  --postgres-container mutsea-postgres --database mutsea \
  --report /private/migration-dry-run.json
python3 tools/MutSea.PostgreSQL/migrate_sqlite.py \
  --source-directory /private/backup/data \
  --postgres-container mutsea-postgres --database mutsea \
  --report /private/migration-applied.json --apply
```

The tool uses the owned container's local administrative `psql` socket, never a password argument. Application tables must be empty. It transfers data in one PostgreSQL transaction, checks counts and compares every projected cell/blob. Receipts are private and refuse overwrite. Do not restart the source while validating the destination.

SQLite migration metadata is replaced by native PostgreSQL migrations. Transient authentication tokens are omitted, requiring new sign-ins. Known column renames and binary encodings are explicit. SQLite link numbers are reconstructed from native UUID-ordered loading while preserving canonical root/entity/asset/inventory IDs. Source-only fields supported by the converter may be omitted **only when every value is default/empty**; nondefault values reject migration. Full original files remain in the backup.

`--verify` compares the destination to the frozen source without mutation. Run it **before** starting clients: subsequent login timestamps, sessions, region backups or new accounts legitimately change data and invalidate whole-snapshot equality. Never reset live data merely to make an old comparison pass.

## Backup and rollback

For a consistent application backup, log out clients and shut down MutSea normally. Keep PostgreSQL running while exporting its database with `pg_dump -Fc` and its role definitions with `pg_dumpall --globals-only`, redirecting both into private 0600 files. Snapshot the matching runtime/config/catalog files and any independently persisted agent-runtime state. Role dumps can contain password hashes; never commit or display them. Record the image digests, component hashes and native migration versions. Verify restore into a separate disposable database before relying on a release backup.

A PostgreSQL data-volume snapshot requires a clean PostgreSQL shutdown or a supported PostgreSQL backup method. Do not treat an ordinary live filesystem copy as a consistent database backup.

Rollback stops the PostgreSQL simulator, retains its volumes and restarts the preserved SQLite fixture with its original config/image/snapshot. This restores the **cutover-time state**, not later PostgreSQL edits/accounts/password changes. Preserve a PostgreSQL backup first. Never run both fixtures against the same login/UDP listener. Do not delete either data volume during the verification period.

## Validation record

The audited cutover copied **722 rows in 27 tables**, including 442 assets, 64 prims/shapes, 39 inventory items and 28 appearance rows. All projected cells and binary data matched before runtime startup. Four account records included three ordinary development users and the inherited grid-services record. Ten transient session-token rows were omitted. Later native account creation/import/normal operation changes these counts intentionally.

Native schema initialization, transfer converter tests (8), provisioning/provider concurrency tests (18), simultaneous account logins, fresh-cache viewer rendering, second-client UDP appearance and three real RGBHM baked-texture downloads passed. A new account created directly on PostgreSQL persisted 6 wearable items, 6 COF links and 6 saved wearable bindings. The existing worn pants retained RGB parameters 806/807/808 = 0.22/0.29/0.38; a separate earlier red Save As item is not the worn item.

Live Starter Library content import used the native PostgreSQL asset and scene services, producing four mesh parts, structured semantics and WorldLine records. After normal simulator shutdown and a PostgreSQL restart, the four parts and semantics persisted; guarded Undo removed them and recorded the result (12 content-journal entries total). The final viewer relogin visibly retained the worn gray-blue pants, and the newly created PostgreSQL account observed them through the second-client appearance/bake probe. Lumi's identity, autonomy, seven UDP embodiment components, home relationship, memory and goal history also passed restart comparison.

The private `pg_dump` was restored into a separate verification database. Counts and complete row-set checksums matched for all **36 native tables**; the same checksums matched across the database restart before MutSea was restarted. No SQLite `.db` file appeared in the PostgreSQL runtime directory. Neither this cutover nor one imported lantern completes Harbor/avatar/art delivery or public-registration readiness.

Private receipts, snapshots, diagnostics and credentials live outside Git. Do not interpret an early failed startup (unquoted INI connection string) or a fixture probe against an obsolete scene manifest as the final runtime result. Viewer automation must use the selected grid's own location scheme: `secondlife://` can switch to Linden's built-in grid even when a MutSea grid was supplied on the command line.
