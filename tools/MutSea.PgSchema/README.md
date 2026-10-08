# Native PostgreSQL schema initialization

Uses the source-matched `MutSea.Data.PGSQL` embedded resources and native migration runner. Requires an existing private database/role; it creates/upgrades native tables and does not transfer SQLite rows or start a simulator. Stop clients/simulators before running migrations on an existing database, and preserve a tested backup.

```sh
dotnet build tools/MutSea.PgSchema/MutSea.PgSchema.csproj -c Release \
  -p:MutSeaBin=/absolute/path/to/matching/native-bin \
  -p:MutSeaComponents=/private/component-build \
  -o /private/schema-tool
dotnet /private/schema-tool/MutSea.PgSchema.dll /private/database.json
```

Private JSON fields: `host`, `database`, `user`, `password`, optional `port`. Run the tool in a matching .NET 8 environment that can reach the database; for Docker, mount its output and private configuration into the matching simulator image on the database network and override the entrypoint to `dotnet`. Secrets never belong in arguments, source or output. Diagnostics go beside the private JSON file with Unix mode 0600.

The inherited runner can swallow SQL errors and advance versions, so success additionally verifies selected native migration versions, required tables and repaired column contracts. It does not prove every historical schema constraint is correct. The offline transfer tool performs further actual-column validation; a complete deployment still requires native service/runtime tests. See [PostgreSQL operations](../../docs/mutsea/postgresql.md).
