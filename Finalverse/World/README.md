# Finalverse world operations for MutSea

Opt-in region module; MutSea remains the simulator/world brand. Inherited scene, permission, streaming and backup APIs execute the changes. The new kernel contains no viewer, renderer or model dependency. New files use BSD-3-Clause under the repository's `LICENSE.txt`; inherited notices remain intact.

## Build and run

Build the existing standalone image first using `deploy/standalone/Dockerfile`. Build the add-on against that **same** image; avoid mixing unrelated simulator assemblies:

```sh
docker build -f deploy/standalone/Dockerfile -t mutsea:verify .
docker build -f deploy/worldops/Dockerfile --build-arg MUTSEA_IMAGE=mutsea:verify -t mutsea:worldops-verify .
```

The add-on recipe runs the kernel suite on native .NET 8, builds only the separate module against the base assemblies, and inherits the base entrypoint, unprivileged user and runtime. It adds one DLL; default simulator configuration does not enable it. For distribution, supply an immutable verified base image reference and review licensing/provenance separately.

Alternatively, after the repository's existing `runprebuild.sh`/solution build:

```sh
dotnet build Finalverse/World/Finalverse.World.csproj -c Release -p:MutSeaBin=/absolute/path/to/built/bin -o /private/module-build
dotnet run --project tools/Finalverse.WorldTests -c Release
```

Copy only `Finalverse.World.dll` to the simulator bin directory while the owned development simulator is stopped. Enable in the private simulator configuration:

```ini
[FinalverseWorld]
Enabled = true
DataDirectory = /data/finalverse
```

Use the existing standalone entrypoint/configuration procedure. The data directory must be writable by the simulator service user. Keep the fixture isolated, publish ports only on loopback, use a generated development account, and back up scene data and WorldLine together. No public deployment is included in this milestone.

## Contract and limits

An active root avatar's Seed response supplies the bearer `FinalverseWorld` capability. JSON POST operations: `inspect`, `prepare`, `commit`, `undo`, `journal`, `semantic`, `lumi`. Never send this URL to a model. Prepared plans are actor/session-bound, immutable and expire after two minutes. Re-login invalidates pending approvals.

The version-1 plan contains `schema_version`, `request`, `provider`, `model`, and 1–8 actions. Actions contain `type`, explicit `target_id` (null for creation), and exact per-type `parameters`. The client/planner schemas are in the sibling `services/ai-gateway/schemas`; the kernel independently validates proposals. IDs and provider claims do not confer permission.

Allowed actions: move (absolute or toward an inspected entity), rotate, scale, name, description, uniform color, box/sphere creation, restricted clone/delete, and a deterministic 17-component Lumi home. Limits: 64m inspection/edit scope, 64 inspected entities, 12m move/clone, 24 creations, 120m³ changed/created volume, component scales 0.05–12m. A home is 4–8m × 4–8m × 2.5–4m, within 16m of the avatar, on level dry ground, with conservative clearance from all regional objects. Placement is revalidated against a scene revision before execution.

Objects must be owned unlinked roots, nonphysical, unattached and inventory-free. Clone and deletion/restoration additionally require the generated plain primitive form, default uniform texture, no advanced shape/material features and exact generated permission masks. Editing existing objects preserves inherited textures. Home components cannot be individually deleted through this API; use whole-plan undo.

## Persistence and recovery

WorldLine is private append-only JSONL, flushed before mutation and after result. Commits use the inherited synchronous object backup API. Semantic metadata and Lumi's home/creation memory are projections of committed records. Lumi has no avatar binding or autonomous behavior yet. Provider/model provenance is an unsigned caller assertion.

Structured simulator logs contain actor/region, operation, HTTP status, latency, plan/operation IDs, outcome and step count. They exclude capability URLs, prompts and scene labels. The planner separately logs its request ID/provider/latency/action count; cross-service tracing, signed provenance and production metrics remain future work.

This is logical transaction execution with guarded compensation, not an atomic database/file transaction. Legacy edits can race with checks. Compensation refuses to overwrite concurrent or partially applied state. Interrupted intent, failed compensation or unconfirmed journal durability blocks further AI writes. Malformed journals prevent module startup. There is no automatic repair/admin override: inspect recorded IDs against the scene and backups, then follow a reviewed recovery procedure. Never erase history to bypass recovery.

## Verification

Kernel tests use explicit fake worlds/models. Live capability/UDP tests are separate and use a real local model for `apply`. Build the Unix-compatible protocol test SDK using the existing `tools/MutSea.ProtocolSmoke/build.py` procedure; it remains separate from server assemblies. Then:

```sh
dotnet build tools/Finalverse.WorldSmoke -c Release -o /private/world-smoke -p:MutSeaBin=/private/protocol-build/sdk/bin
dotnet /private/world-smoke/Finalverse.WorldSmoke.dll /private/account.json /private/receipt.json apply
# Instead, after creating a home in the viewer and logging out, capture that existing operation:
dotnet /private/world-smoke/Finalverse.WorldSmoke.dll /private/account.json /private/ui-receipt.json capture
# After a normal simulator shutdown/restart:
dotnet /private/world-smoke/Finalverse.WorldSmoke.dll /private/account.json /private/receipt.json verify
dotnet /private/world-smoke/Finalverse.WorldSmoke.dll /private/account.json /private/receipt.json undo
# After another normal simulator restart, verify persisted deletion and relationships:
dotnet /private/world-smoke/Finalverse.WorldSmoke.dll /private/account.json /private/receipt.json verify_undo
# Independent API-family check; temporary primitives are removed:
dotnet /private/world-smoke/Finalverse.WorldSmoke.dll /private/account.json /private/actions.json actions
```

`account.json` fields: `grid_url`, `first_name`, `last_name`, `password`. Keep it and receipts private; never put credentials in arguments, console history or Git. Do not run this probe while the same account is logged into the viewer. `apply` leaves a home for restart verification; `undo` removes it. `capture` reads an already committed home through the authenticated API and UDP, storing a new private receipt without prepare/commit/undo calls; it refuses to overwrite a receipt. It logs in and teleports the probe avatar to the home test site. Use the captured receipt for later verification of that same UI-created operation. Restart verification also compares every semantic metadata field with the captured API receipt. The probe checks authoritative execution results plus UDP existence/position/rotation/scale; it does not substitute for visual client acceptance. The full UI release gate remains in the viewer's `docs/finalverse/phase2-ai-world-creation.md`.
