# Standalone MutSea deployment

## Current public state

As of 2026-10-09, a new x86-64 simulator is installed on the requested development host with a dedicated PostgreSQL 16.15 container, persistent storage and a private database network. The application role owns its database and has no superuser, database-creation or role-creation privileges. No database port is published. Existing neighboring containers and volumes were preserved.

The full native source build and Finalverse module build passed. Fresh startup provisioned an active developer account with initial appearance data and seeded 40 Harbor objects. Real LAN HTTPS login through the gateway, the advertised HTTPS capability origin, all 40 UDP object identities/transforms and logout passed. This uses explicit private test DNS mappings and is **not** proof of public world entry.

The final development image also passes bench import, all thirteen semantic records, simulator restart and guarded Undo through that gateway. A matching stopped-server backup pins the image, PostgreSQL dump, role definitions, configuration, runtime journals and scene seed. Restoring the dump into a separate verification database reproduced every row in all 31 native tables. Original Harbor streaming passed again after content acceptance; detailed receipts and backups remain private.

`https://mutsea.com` and `https://www.mutsea.com` remain the welcome website. Public grid login remains gated pending an externally verified UDP route and simulator certificate-renewal synchronization. A public UDP 19000 probe did not reach the gateway, and the router did not answer UPnP discovery. Operational addresses, account credentials, configurations, certificates and raw receipts remain outside Git.

## Prepare a new instance

Run from the repository root on a Linux host with Docker, Python 3 and the .NET 8 SDK (or build the tools inside its official SDK image). Keep all operational paths outside Git, private to the operator.

```sh
docker build --platform linux/amd64 -f deploy/standalone/Dockerfile -t mutsea:verify .
python3 deploy/standalone/configure.py --host grid.mutsea.com --public-tls \
  --database-config /private/mutsea/database.json --output /private/mutsea/config
```

The generator defaults to PostgreSQL and requires a private database configuration. Provision the database/role first; see [PostgreSQL storage and migration](postgresql.md). It refuses a nonempty output directory and creates private INI files, independent user/region UUIDs and an unregistered development account. The existing estate-owner initialization registers that account on first successful startup. Do not import this configuration into an existing world. Confirm registration through a real login; remove `DefaultEstateOwnerPassword` afterward. Keep the private account file outside repository, image and command arguments.

Build `tools/MutSea.StarterScene` against the freshly compiled simulator assemblies with `-p:MutSeaBin=/absolute/path/to/bin`. Run its DLL with the owner UUID from the private account file and a new scene directory. UUIDs are identifiers, not passwords.

For the public TLS profile, install a valid PKCS12 certificate at `/config/mutsea.p12`, using the generated certificate-password file through OpenSSL's file input. Its SAN must cover `grid.mutsea.com`. Keep certificate keys private; arrange renewal and simulator certificate reload/restart before enabling public traffic. The gateway renewal hook currently reloads nginx only. Simulator certificate synchronization is still a deployment gate; the gateway does not yet have authorized unattended SSH access to the simulator. A manual certificate installation is verified, with expiration on 2027-01-05.

Set the Compose environment privately:

```text
MUTSEA_IMAGE=<verified tag or immutable digest>
MUTSEA_CONFIG=<private config directory>
MUTSEA_DATA=<persistent writable data directory>
MUTSEA_SCENE=<generated scene directory>
MUTSEA_BIND_IP=<server LAN address>
```

Container UID/GID is 1000. Private configuration and data must be readable/writable by that identity. Do not make password files world-readable. The image runs the existing simulator as this unprivileged user, with ubODE, native PostgreSQL storage, inherited assets and YEngine. The explicit `--database sqlite` option remains for separate legacy fixtures/rollback. Keep credential initialization in the private configuration and never type passwords as console command arguments. Logs, journals, asset cache, script cache and map tiles live under `/data`; PostgreSQL has separate persistent storage and backups. Attach the simulator to an already provisioned private database network with the PostgreSQL Compose overlay; publish no database/console port.

## Seed exactly once

Only on a fresh instance, set the private startup file to:

```text
terrain load /scene/harbor.r32
load xml2 /scene/harbor.xml
```

After PostgreSQL is healthy, start with `docker compose -f deploy/standalone/compose.yaml -f deploy/standalone/compose.postgresql.yaml up -d` and the private `MUTSEA_DATABASE_NETWORK` setting. Verify initialization and the scene; then empty `startup.txt` before the next restart. Imports are operator actions and may replace scene content; never automatically replay them against a live edited world. The XML2 importer and normal region storage perform the mutations.

The public profile exposes native simulator HTTPS on container port 443 through LAN TCP 19000. Region UDP uses 19000. The gateway must proxy HTTPS to this upstream with certificate verification (including an adequate chain depth and the certificate issuer chain) and the router must forward public UDP 19000 to the simulator. The inherited service calculates capability URLs from its actual TLS listener, so port 443 is intentional. Do not expose its auxiliary HTTP listener, console or databases. Enable the grid route only after HTTPS capabilities, UDP scene streaming, region crossing assumptions and real Finalverse login have been checked from outside the LAN.

## DNS and gateway

Review `mutsea-http.nginx` and `mutsea-https.nginx` before installing into an existing gateway. HTTP config supports ACME issuance; HTTPS config redirects website traffic and leaves the grid unavailable. The certificate deploy hook validates nginx configuration before reloading it.

`update-dns.py` uses AWS CLI's existing credential provider, a supplied hosted-zone ID and gateway DDNS hostname. It validates a public IPv4 result and updates only the existing single apex A record when changed. The supplied systemd unit/timer runs every five minutes. AWS credentials are not in source or service environment. Existing NS/SOA records are preserved; the initial setup added apex A, www CNAME and grid CNAME. Zone IDs, private fleet addresses and credential locations belong in a local operational record, not this repository.

## Backup and rollback

Export a world OAR through the simulator console before changing world content. An OAR does not back up accounts, all inventory or every database. For a complete backup, shut down the simulator normally, export PostgreSQL with its role definitions and snapshot the entire runtime/config directories privately; see [backup and rollback](postgresql.md#backup-and-rollback). Retain the previous tested image digest. Roll back by restoring that matching snapshot/database with its corresponding image and keeping the grid unavailable during verification. Stop failed containers; preserve data volumes. This deployment must not modify neighboring services or chain data.
