# Native world registration

Finalverse and MutSea use one native world account. Create it at
[mutsea.com/account/#register](https://mutsea.com/account/#register), then use
**First Last** and the chosen password in the portal and Finalverse. The avatar
UUID and optional email are not login names. Finalverse.com links to this authority;
it does not maintain another password database or share cross-domain cookies.

## What was reused

The existing .NET 8 native runtime already provides UserAccountService,
PasswordAuthenticationService, GridUserService, inventory, appearance and assets.
The ASP.NET Core portal calls those exact tested assemblies. The older Rust
Finalverse projects inspected locally contain world/model/streaming scaffolding,
not a usable registration/authentication service. No runtime or language rewrite
was necessary. AI does not participate in password processing or account activation.

```mermaid
sequenceDiagram
    participant Browser
    participant Portal
    participant Queue as PostgreSQL queue
    participant World as Native region worker
    participant Native as Existing native services
    Browser->>Portal: Origin + CSRF + allowlisted registration fields
    Portal->>Queue: Reserve name/capacity; AES-GCM encrypted request
    Portal-->>Browser: 202 + HttpOnly receipt
    World->>Queue: Claim bounded request
    World->>Native: Pending identity, password, base inventory
    World->>Native: Pinned Genesis outfit + persistent starter home
    World->>Native: Verify postconditions; activate ordinary user
    World->>Queue: Ready; remove credential ciphertext
    Browser->>Portal: Read own receipt; sign in normally
```

The native adapter extracts the existing operator admission procedure into
`ProvisionNewcomer`; both operator files and web requests use it. This is not a
second avatar provisioning implementation. The selected content is pinned by
compiled SHA-256, and retired provisional Elara Male is not selectable. Existing
accounts and already-provisioned inventory are never reassigned by registration.

## Limits and failure behavior

- First and last names each contain 1–31 ASCII letters/digits, starting with a
  letter. Reservation is case-insensitive, checked against native users and queue.
- Passwords use 12–16 printable ASCII characters because the released viewer field
  accepts 16. Native legacy password storage is retained; stronger storage and a
  longer-password protocol migration remain security work before GA.
- Only pinned, operator-configured starter choices are accepted. No role, UUID,
  filesystem path, world owner or capability is accepted from the browser.
- Registration is explicitly enabled, with a deployed preview capacity of 100.
  The public queue enforces capacity under a PostgreSQL advisory lock. A separate
  native journal budget bounds creation if the frontend is compromised.
- Three attempts per keyed IP digest per 24 hours, including failed jobs. Shared
  networks share that allowance. Other existing account-action limits also apply.
- Native readiness requires the configured host region and matching catalog
  heartbeat within 60 seconds. Queue receipts are private, opaque 256-bit values;
  only their SHA-256 digest is stored. The cookie lasts 24 hours.
- Queued data is AES-256-GCM authenticated and bound to the generated job ID.
  A separate owner-only registration key is shared with the native worker.
  Ciphertext is removed on completion/failure or after one hour; an outage can
  defer expiry cleanup until the worker resumes. Job metadata is retained for
  operations/capacity accounting. Do not claim automatic deletion during downtime.
- Processing uses `SKIP LOCKED` and a ten-minute recovery lease. Confirmed admission
  replay observes the existing account and preserves later password/home/appearance
  edits. A failed partial identity stays inactive and requires operator review.
- Native activation and the sidecar/journal are separate durable operations, not
  one distributed ACID transaction. A crash between activation and the confirmation
  journal can require operator reconciliation; never repair this by resetting an
  existing user's password or blindly repeating appearance provisioning.

Email is optional and **unverified**. Save recovery codes after sign-in. No email
reset, MFA, invitation campaign engine, public user administration API or commercial
availability guarantee is claimed.

## Operator setup

First follow [the account portal deployment](../../deploy/accounts/README.md).
Back up the existing PostgreSQL database, private configs, native journals and
image IDs. Check native active sessions before a controlled simulator restart.

1. Apply `deploy/accounts/registration.sql` once as the database operator. It adds
   only sidecar queue/readiness tables. Do not recreate native tables.
2. Grant the actual native runtime database role `USAGE` on `mutsea_portal`,
   `SELECT, UPDATE` on `mutsea_portal.registrations`, and `SELECT, INSERT, UPDATE`
   on `mutsea_portal.registration_worker`. The public portal owns the queue but
   has only `SELECT` on readiness; it cannot forge a native heartbeat or create
   native identities, inventory or world objects.
3. Generate a separate random 32-byte key encoded as 64 hex characters. Store
   `registration.key` and `registration.json` as regular mode-0600 files readable
   only by the service UID. Mount them into portal and native config directories.
   Do not put them, credentials or generated user lists in Git or image layers.
4. Configure both processes with the same registration settings:

```json
{
  "Enabled": true,
  "KeyFile": "/config/registration.key",
  "HostRegion": "<actual newcomer host region UUID>",
  "Capacity": 100,
  "Avatars": [{
    "Id": "auren",
    "Title": "Auren",
    "Directory": "/config/genesis/auren",
    "Pin": "<actual compiled package SHA-256>"
  }]
}
```

Add `"RegistrationFile": "/config/registration.json"` to the private portal JSON.
Add to the simulator INI:

```ini
[FinalverseRegistration]
Enabled = true
Configuration = /config/registration.json
```

This requires the existing `FinalverseAccounts` host/home and a ready selected
world template. Package `MutSea.Registration.dll` alongside `Finalverse.World.dll`;
the supplied module/container builds do so. Both processes use PostgreSQL, not
SQLite. Keep the database private and RemoteAdmin disabled.

5. Start the native worker first, confirm heartbeat/options, then the portal.
   Publish the registration UI only after a real account has appearance/home and
   native authentication. Ensure public HTTPS and region UDP actually connect;
   opening a website alone does not prove world entry. Preserve authenticated TLS
   upstream verification and deny citizen-internal endpoints publicly.
6. Operators manage users through the existing private simulator console and
   reviewed provisioning tools. Consult its `help` for actual commands. There is
   no newly exposed web administrator or public password-reset shortcut.

To pause admissions set `Enabled=false` in portal settings and restart the portal;
existing sign-in remains available. Stop or disable the worker before changing
package pins or keys. Drain/reconcile pending jobs first. For rollback restore the
prior tested images/configs while retaining the additive schema and current native
credentials. Do not restore old auth dumps over users' newer passwords. After a
credential rotation, rollback configs must retain the new database credential.

## Acceptance evidence, October 17, 2026

- 75 .NET 8 Linux API/security checks (72 on macOS using installed .NET 10).
- 14 real native account/PostgreSQL checks and 26 encrypted-queue concurrency,
  expiry, tamper, capacity, readiness and failure checks against a disposable DB.
- Eight checks against a running isolated clone: native authentication, level 0
  with no elevated flags, persistent home, owned wearables/alpha/mesh attachments,
  current outfit and ciphertext removal. Repeated after simulator restart.
- Real HTTPS registration on the hosted service; same credential web sign-in,
  home information and logout. One name reservation persists across requests.
- External protocol acceptance using the newly registered synthetic account:
  public TLS login, actual UDP scene streaming, HTTPS capability origin and logout.
  The test SDK retains certificate chain/name validation. No world content or
  avatar appearance was modified by this probe.
- Viewer universal rebuild and all 119 CTest tests pass. UI/release evidence is
  recorded separately in the viewer repository; server checks are not a claim
  of cross-device or GA readiness.

Test accounts, passwords, receipts, capability URLs and database backups stay in
private operator evidence directories outside all repositories.
