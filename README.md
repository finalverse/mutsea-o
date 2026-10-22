# MutSea

**The persistent spatial runtime behind [Finalverse](https://finalverse.com).**

MutSea preserves the mature OpenSim-derived region, avatar, inventory, asset,
permission and networking systems while adding semantic places, reusable world
content and accountable AI world operations. The server/platform brand is
**MutSea**; the consumer desktop experience is **Finalverse**.

[Platform website](https://mutsea.com) ·
[Finalverse Mac preview](https://github.com/finalverse/final_viewer/releases/tag/v0.4.0-preview.3) ·
[Native demo and getting started](https://github.com/finalverse/final_viewer/blob/main/docs/finalverse/getting-started.md)

**New and existing users:** [create or manage your account](https://mutsea.com/account/) with the
same avatar name/password as the viewer. View your home/identity, edit web-profile
preferences, change your world password and generate private recovery codes.
[Account and recovery guide](docs/mutsea/account-portal.md). Limited preview registration provisions a starter avatar, outfit, inventory and home before activation. Email reset is not available. [Registration architecture and operator runbook](docs/mutsea/registration.md).

> Development platform with limited preview registration. Managed hosting, general
> multi-user AI citizen ownership and a commercial SLA are not offered. Download
> the experimental Mac prerelease, create your own MutSea account, then select
> Finalverse Harbor (Preview) and sign in using **First Last**, not an avatar UUID.

## Current implementation

- Existing grid/region connectivity, scene simulation, avatars, inventory, assets,
  permissions and editing remain the bootstrap runtime.
- Native world services can inspect structured entities and prepare bounded
  actions. Policy, permissions, terrain and explicit approval precede mutation.
- Deterministic avatar-relative placement handles bounded primitive requests;
  composed places use validated versioned content and native site checks.
- WorldLine records operations, with fingerprint-guarded compensation/Undo.
- Hosted Lumi co-building uses private IPC for planning/citizen commands,
  verified world HTTPS and a dedicated PostgreSQL citizen database. The model
  does not receive world-write authority or simulator credentials.
- Avatar/home onboarding and reusable Starter Library/world-template tools are
  implemented incrementally over the inherited account/inventory/appearance model.

A real hosted-Lumi acceptance created 147 native parts after approval, retained
exact transforms/ownership/semantics and memory across restarts, then removed
the creation through Undo. Scene counts returned to the 423-part baseline.
[Actual screenshots and limitations](https://github.com/finalverse/final_viewer/blob/main/docs/finalverse/demos.md).

## Build

The native runtime targets .NET 8. Use its SDK and platform native dependencies;
see [BUILDING.md](BUILDING.md) for the retained source build:

```sh
git clone https://github.com/finalverse/mutsea-o.git
cd mutsea-o
./runprebuild.sh
dotnet build --configuration Release MutSea.sln
```

The reproducible Linux container path is
[deploy/standalone/Dockerfile](deploy/standalone/Dockerfile). Build the separate
World adapter against the **same tested native image/assemblies** using
[Dockerfile.world-module](deploy/standalone/Dockerfile.world-module). Never mix
an adapter with an unrelated runtime binary.

## Configure a separate world

Read [deployment](docs/mutsea/deployment.md),
[PostgreSQL](docs/mutsea/postgresql.md) and
[world operations](Finalverse/World/README.md) before running a new instance.
PostgreSQL is the fresh-instance default; there is no silent SQLite fallback.
The operator provisions a private database/role first and passes its mode-0600
configuration file to `deploy/standalone/configure.py`. That generator refuses
to overwrite an existing world.

Keep native control, databases, planner and citizen sockets private. Public
viewer connectivity needs the advertised region hostname, valid TLS and the
correct region UDP mapping; it does not require publishing database/control/AI
ports. A website deployment must not alter the grid vhost or world admission.
Stop the runtime gracefully before replacing the simulator, retain native data
and journals together, and keep a tested image/config/database rollback.

## Accounts and login

Accounts belong to the selected grid. On your **own** simulator console, use
interactive `create user`, `show account Firstname Lastname`, and
`reset user password Firstname Lastname`. Password prompts must remain hidden;
never put passwords in shell arguments or issue attachments. `login status`
shows admission; `login disable`/`login enable` are operator maintenance controls.
Do not grant administrative user levels to ordinary testers.

For the private Finalverse pilot, request access at
[info@finalverse.com](mailto:info@finalverse.com?subject=Finalverse%20preview%20world%20access).
No public demo credential is provided. Newcomer appearance/home provisioning is
separate from issuing a login: see [activation](docs/mutsea/newcomer-activation.md).

## Tests and integration

The current increment runs 61 World tests, 13 composer/discovery checks and 12
planner-boundary checks, plus build/protocol/persistence acceptance. From a
checkout with a compatible .NET host:

```sh
dotnet run --project tools/Finalverse.WorldTests -c Release
dotnet run --project tools/MutSea.ComposerTests -c Release
dotnet run --project tools/Finalverse.PlanningTests -c Release
```

These are policy/geometry fixtures, not a substitute for actual login, UDP,
rendered scene and restart/Undo checks. Native probes require a separate
portable protocol SDK and private account/receipt files. Do not run a probe
concurrently with the same account in the viewer.

[Documentation index](docs/mutsea/README.md) ·
[World template provisioning](docs/mutsea/world-template-provisioning.md) ·
[Native certificate renewal](deploy/standalone/native-certificate-renewal.md)

## Provenance and contributions

MutSea is BSD-licensed; preserve [LICENSE.txt](LICENSE.txt), upstream contributor
notices and file-specific licenses. It is an OpenSim-derived project; inherited
code is not newly authored Finalverse work. Imported assets/OARs have their own
license/provenance and must be reviewed independently of server source licensing.

Report reproducible issues at
[finalverse/mutsea-o](https://github.com/finalverse/mutsea-o/issues). Include source
revision, platform and redacted diagnostics. Never attach raw private INI,
database URLs, account files, capability URLs or session tokens. Prefer small,
tested changes over subsystem replacement.

## World Library and Confluence

The `codex/confluence-v07` integration branch adds a scoped canonical-session
relay over the existing native capability and private Earth transport. See
[Confluence continuity](docs/finalverse/confluence-continuity.md) for identity,
permission, replay and local acceptance boundaries. No public service is
silently upgraded by this source branch.

[Live regions and acceptance](docs/mutsea/world-library-acceptance-20261018.md) · [Architecture and API boundaries](docs/mutsea/world-library.md) · [Operator acquisition/admission guide](docs/mutsea/world-library-operations.md). The source catalog records staged, rejected and deferred archives separately from live destinations. Creator recipes support private releases, free publishing and remix previews; payment checkout is not enabled. The current Mac preview.3 has a known repeated-teleport handoff race; see the acceptance record for the source fix and installer gate.
