# Audited SQLite fixture transfer

`migrate_sqlite.py` performs a deliberately bounded offline transfer from read-only SQLite snapshots into an **empty** native PostgreSQL schema. It preserves entity/account/inventory/asset IDs, transfers in one transaction and compares projected cells/blobs. Dry-run is the default; `--apply` and read-only `--verify` are mutually exclusive. Reports refuse overwrite and contain hashes/counts, not credentials. Diagnostics remain private.

The source schema is allowlisted. Nondefault unsupported fields reject migration. Transient authentication tokens and provider migration metadata are intentionally omitted. It reconstructs native SQLite link order and decodes the two known base64 binary fields. It is not a general arbitrary-world migration. Never run a live source during transfer or overwrite a populated destination.

```sh
python3 -m unittest discover -s tools/MutSea.PostgreSQL -v
python3 tools/MutSea.PostgreSQL/migrate_sqlite.py --help
```

Eight converter tests cover NULL versus empty text, quote/newline/null-marker escaping, lossless bytes, supported base64 conversion, invalid binary rejection, native booleans and identifier injection. Actual transfer, restart and backup restore must be tested separately. See [the full migration sequence and limits](../../docs/mutsea/postgresql.md).
