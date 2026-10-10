# Passive OAR staging tools

Offline, bounded adapters around MutSea's inherited region archive loader. They do not restore an archive, mutate a running world, issue a WorldAction, or grant distribution rights. See [the native experiment and limitations](../../docs/mutsea/oar-compatibility.md).

```sh
python3 tools/MutSea.Oar/quarantine.py /absolute/source.oar /absolute/NEW-output
python3 -m unittest discover -s tools/MutSea.Oar -p 'test_*.py'
```

`quarantine.py` accepts a single-region OAR 0.8, 256 × 256 m, with one R32 terrain and region settings. It bounds compressed bytes (64 MiB), expanded bytes (256 MiB), member bytes (8 MiB), headers (10,000), XML nodes (100,000) and depth (64). XML structure limits are checked during parsing. Paths, headers, links/devices/PAX, duplicates, finite transforms and terrain heights are checked. Bounded native GNU long names are supported, including the inherited writer's names without a terminating NUL.

The new private output contains a deterministic `staging.oar` and `report.json`, with source/member/derivative hashes, source UUIDs, owners, creators and transforms. Original input bytes remain intact. Existing destinations are refused. Task inventory, saved script state, media and script/bytecode/state assets are removed; nested inventory-object assets are excluded. Region scripts are disabled in the derivative. Run the simulator with its script engines disabled as well, including after restart. Texture/mesh codecs and general dependency/provenance closure are separate validation gates.

Native OAR restoration resets object UUIDs. Reconcile a **read-only** native database export before streaming verification:

```sh
python3 tools/MutSea.Oar/runtime_report.py /absolute/report.json \
  /absolute/native-parts.json INSTANCE-OWNER-UUID /absolute/NEW-runtime-report.json
```

Each native part needs `id`, `group_id`, `owner`, `name`, `link_number`, `position`, `offset`, `scale` and `rotation`. Export from the isolated database after native backup. Root names/positions/scales/counts and part names/full local transforms must uniquely match; a retained link number breaks equal-geometry ties. Unexpected ownership, changed transforms, extra parts and ambiguity reject. This is a receipt for one isolated restore, not a general instance-ID allocator or transaction remapper.

## Experimental network guard

`netguard.Dockerfile` builds a small iptables sidecar. Apply it **only to the designated staging simulator's network namespace**, with its own PostgreSQL IPv4 address as the sole argument. The simulator itself has all capabilities dropped. The sidecar temporarily needs `NET_ADMIN`; it exits after installing the rules. No host firewall or unrelated container namespace is changed.

```sh
docker build -t mutsea:oar-netguard -f tools/MutSea.Oar/netguard.Dockerfile tools/MutSea.Oar
docker run --rm --network container:STAGING-SIMULATOR --cap-drop ALL \
  --cap-add NET_ADMIN --security-opt no-new-privileges:true \
  mutsea:oar-netguard POSTGRES-IPV4
```

It permits loopback, established/related replies and new PostgreSQL TCP/5432 connections, rejects other IPv4 output and drops IPv6 output. Use only with a fresh, paused staging simulator before unpausing; this is not a production conversion-worker supervisor. It intentionally refuses duplicate installation. Rules disappear with the network namespace: reapply after every container restart, before client access. Docker's internal/non-masquerading networks alone did **not** block outbound traffic in the tested Docker Desktop configuration. Verify a negative outbound probe as well as database/client connectivity. No public port or Harbor volume belongs in this fixture.
