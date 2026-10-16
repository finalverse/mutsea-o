# Private account service deployment

Read the [account contract and user guide](../../docs/mutsea/account-portal.md).
This service is for an existing PostgreSQL-backed MutSea world. It neither
registers users nor runs a simulator.

1. Back up native identity data privately and record the tested simulator image
   digest. Build/test the portal against its exact assemblies using this
   directory's Dockerfile. Pin the resulting portal image for deployment.
2. As a database operator, create a **dedicated** `mutsea_portal` login with
   `NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION CONNECTION LIMIT 32`. Set a
   random password with an interactive `psql \password mutsea_portal` prompt or a
   private operator tool; never put it in command arguments or logs.
3. Run `schema.sql` in the native database **once**, as its owner. It creates only
   the new `mutsea_portal` schema and explicit grants. It deliberately refuses an
   existing schema rather than overwriting it. Verify the role has no CREATE on
   `public` and no native account/role/object/asset/inventory write privileges.
   Current native migration versions must already be present; do not grant
   native migration writes to make a mismatched assembly start.
4. Create a private configuration directory owned by UID 1000, mode 0700. Put
   these three mode-0600 files inside it, without a symlink:
   - `database`: Npgsql connection string for the restricted role, the private
     PostgreSQL hostname and existing native database. Set `Maximum Pool Size=20;
     Timeout=10;Command Timeout=10;Include Error Detail=false`. No published DB port.
   - `key`: a stable, randomly generated 32-byte key encoded as 64 hex characters.
     Protect/back up it; replacing it invalidates existing sessions/recovery codes.
   - `portal.json`: the following configuration, with real region IDs/names:

```json
{
  "Origin": "https://mutsea.com",
  "Socket": "/run/mutsea-accounts/http.sock",
  "Listen": null,
  "Development": false,
  "DatabaseFile": "/config/database",
  "KeyFile": "/config/key",
  "Regions": {}
}
```

5. On the Linux host, create `/run/mutsea-accounts` owned by `1000:33`, mode0750.
   Group33 is the deployment's nginx worker group: verify that mapping first.
   Install this root-owned tmpfiles rule so reboot recreates the directory:
   `d /run/mutsea-accounts 0750 1000 33 -`. The service creates its socket0660 and
   holds a single-writer lock; it recovers a stale socket after a process crash.
6. Create a private mode-0600 Compose environment file with:
   `MUTSEA_ACCOUNTS_IMAGE`, `MUTSEA_ACCOUNTS_CONFIG` (host config directory),
   `MUTSEA_ACCOUNTS_IPC=/run/mutsea-accounts` and `MUTSEA_DATABASE_NETWORK`
   (existing private database network). Use `compose.yaml` with its own project
   name. It publishes **no TCP port** and runs as1000:33 with a read-only root
   filesystem, dropped capabilities, bounded memory/CPU and bounded container logs.

```sh
docker compose --project-name mutsea-accounts \
  --env-file /private/path/accounts.env -f deploy/accounts/compose.yaml up -d
curl --unix-socket /run/mutsea-accounts/http.sock \
  -H 'Host: mutsea.com' http://localhost/account/api/session
```

7. Only after private checks pass, add the `/account/api/` proxy in the **website**
   HTTPS server block. The mutsea-web repository's `deploy/prepare-vhost.py
   --accounts` prepares a candidate from current production. Review its diff;
   it preserves grid/ACME blocks and other services. The proxy uses the private
   socket, overwrites Host and X-Real-IP, clears forwarded identity headers and
   sets request/time limits. Never expose Kestrel or simulator admin ports.
8. Build mutsea-web with `npm run build` and deploy only `dist/`, using the
   existing checksum-verified, atomic website release and rollback scripts.
   Finalverse.com's Account navigation links directly to MutSea.
9. Verify external TLS, CSRF rejection, host-only Secure/HttpOnly cookies,
   no-store, an admitted test account's own profile/home, and logout. Test password
   mutation/recovery against the isolated fixture, not another person's account.

For **local development only**, `Development=true`, `Socket=null`, and identical
`Origin`/`Listen` values of `http://127.0.0.1:PORT` select loopback HTTP and distinct
development cookie names. Production rejects that configuration/origin pairing.
Do not attach a public reverse proxy to a development instance.

Rollback the website release/vhost first, then stop this Compose project. Keep its
schema, configuration/key and backups. Do not delete simulator/PostgreSQL volumes
or apply an old auth dump over passwords users subsequently changed. A manual
`docker kill` is considered an operator stop by Docker; explicitly `start` it
again when rehearsing crash recovery. Ordinary process exits use `unless-stopped`.

The user-facing privacy notice must match the implementation. Verified-email
recovery, MFA and public signup are not implemented in this version.
