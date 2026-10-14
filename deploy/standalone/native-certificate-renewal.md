# Dedicated native TLS renewal

Certbot owns the `mutsea.com` lineage and its existing webroot challenge. Nginx
reloads when Certbot renews. MutSea caches its PKCS12 identity at startup, so
nginx renewal alone does not refresh native TLS.

Install `refresh-native-certificate.py` as the root-owned
`/usr/local/libexec/finalverse-native-certificate`, install the supplied service
and timer, and create an owner-only `/etc/finalverse/native-certificate.env`:

```ini
FINALVERSE_OPERATIONS=/home/bg/finalverse-backend-20261015/operations
```

The deployment service is intentionally scoped to that dedicated Compose project
and operator directory. Adapt the service's `ReadWritePaths` together with the
environment file for a different installation. It never targets other projects.
The directory is private; `config/certificate-password` supplies the existing PFX
password via a file descriptor path, never a command-line password value.

Enable `finalverse-native-certificate.timer`. The Certbot deploy hook starts the
service asynchronously; the timer retries deferred changes every fifteen minutes.
No reset of the PostgreSQL volume or account database occurs.

The refresh process:

1. Validates the renewed leaf expiry and compares its SHA-256 with the certificate
   actually loaded by native TLS, with hostname and chain verification.
2. Checks the dedicated container identity and its current native admission state.
3. Disables new native logins and defers if root or child sessions remain. It
   restores admission on deferral. An existing operator admission fence is preserved.
4. Exports an owner-only staged PFX, retains the previous PFX, records user/world
   fingerprints, disables container restart and requests native shutdown.
5. Requires a clean exit, backs up the freshly stopped database, atomically
   replaces the PFX and starts the same container.
6. Verifies loaded native and frontend TLS, the verified native HTTPS proxy, and scene, appearance, inventory, account and journal
   preservation before restoring admission and the prior restart policy.

Errors stop with a sanitized journal event requiring operator review. The old PFX
and current database backup remain under the private `backups/` directory. A
failed startup is not repaired by restoring stale user data. Inspect the native
log and backup before deciding a rollback. The script refuses unexpected region
counts and never forcibly kills the world process; only its disposable console
attach client may be terminated.

The `--force` flag exercises export/restart with the current certificate for an
operator test. Normal service runs are idempotent and skip an already-current
certificate. An expired old native certificate does not prevent a safe attempt
to load a valid renewed certificate.

## Verified 2026-10-15

The selected Certbot lineage passed a real `renew --cert-name mutsea.com --dry-run`
ACME test. A forced native rotation passed loaded-certificate and full preservation
checks. A second forced attempt with a real logged-in pilot deferred without a
restart or world change. The native timer and private planner remained active.
Five isolated certificate workflow tests cover parsing, fail-closed region checks
and active-session deferral. This does not claim a production certificate was
newly issued during the test.
