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

WorldLine is private append-only JSONL, flushed before mutation and after result. Commits use the inherited synchronous object backup API. Semantic metadata and Lumi's home/creation memory are projections of committed records. The optional Phase 3 citizen bridge binds Lumi to a persistent linked entity and separate agent service, as described below. Provider/model provenance is an unsigned caller assertion.

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


## Phase 3: opt-in persistent citizen

Enable only in an isolated development region with the separate `services/agent-runtime` service. Keep `[FinalverseWorld]` enabled. Add to private simulator configuration:

```ini
[FinalverseCitizens]
Enabled = true
ControllerId = REPLACE_WITH_EXISTING_SANDBOX_ACCOUNT_UUID
TokenFile = /config/service-token
RuntimeUrl = http://host.docker.internal:18766
```

`RuntimeUrl` above is for Docker Desktop calling the host service. A same-host native simulator uses loopback instead. Share a private 32–128-character token with the runtime's token file, readable only by the simulator service user. Configuration, passwords and tokens never belong in the image or repository. Keep simulator TCP/UDP on loopback for this development workflow.

`CitizenBridge.cs` implements fixed-controller authentication, actual bounded scene perception, canonical Lumi embodiment, speech, semantic home navigation, durable intent/receipt handling and an approved three-shrub garden recipe. `CitizenNavigator.cs` bounds route search/distance/time and checks clearance/ownership/entry while moving. Inherited scene position/rotation, UDP update and backup APIs remain the executor. No renderer or avatar-system replacement is included.

The human `FinalverseWorld` capability accepts `op: citizen` with commands `inspect`, `say`, `pause`, `resume`, `cancel`, `approve`, `undo`. Actor identity comes from the logged-in avatar. The private service endpoint `/finalverse/citizen/<region UUID>` accepts only `ensure`, `observe`, `navigate_home`, `stop`, `speak`, `prepare_garden`, `commit_garden`, `undo_garden`. Mutations require a UUID action correlation and durable intent; uncertain intents are not replayed. Garden commit/undo is restricted to this citizen's recorded plans. The service cannot administer accounts, edit inventory, run scripts or arbitrarily delete world objects.

The runtime provides one-second perception without inference polling. A five-second lease stops motion when it disappears. Simulator restart restores the embodiment from scene backup but never resumes movement automatically. Preserve `.citizens.worldline.jsonl`, the existing kernel journal and scene databases together. The seven-part primitive embodiment deliberately avoids the sandbox's inherited cloud appearance; articulated NPC animation is future work.

```sh
dotnet build tools/Finalverse.CitizenSmoke -c Release -p:MutSeaBin=/absolute/path/to/protocol-sdk/bin -o /private/citizen-smoke
dotnet /private/citizen-smoke/Finalverse.CitizenSmoke.dll /private/account.json /private/receipt.json observe
dotnet /private/citizen-smoke/Finalverse.CitizenSmoke.dll /private/account.json /private/questions.json questions
# Requires Lumi to start away from home; verifies actual UDP motion:
dotnet /private/citizen-smoke/Finalverse.CitizenSmoke.dll /private/account.json /private/navigation.json navigate
# Against the captured receipt, after normal runtime/simulator/combined restart:
dotnet /private/citizen-smoke/Finalverse.CitizenSmoke.dll /private/account.json /private/receipt.json verify
# After human garden approval; repeat with the same receipt after restart:
dotnet /private/citizen-smoke/Finalverse.CitizenSmoke.dll /private/account.json /private/garden.json garden_verify
# After human Undo; checks the same objects are absent and history/memory remain:
dotnet /private/citizen-smoke/Finalverse.CitizenSmoke.dll /private/account.json /private/garden.json garden_undo_verify
```

If only a newer .NET host is installed, prefix the .NET 8 probe with `DOTNET_ROLL_FORWARD=Major`; the simulator image uses native .NET 8. Protocol SDK assemblies come from its documented SDK build, not unrelated simulator binaries. Receipts contain private world/controller state and stay outside Git. Complete service-specific docs are in `~/Finalverse/services/agent-runtime/docs/finalverse/agents/`.

## Citizen representation (Genesis animated embodiment)

The citizen's logical identity and root entity ID never change. Its *representation* can move from the historic seven-part linked primitive to one native animated mesh (animesh), skinned to the viewer's pinned avatar skeleton and driven by native library animations. This is an operator action set in private configuration; it is never a model tool:

```ini
[FinalverseCitizens]
Representation = /config/representations/lumi-v0.1.1
RepresentationSha256 = <sha256 of that compiled.json>
```

Build the directory with Studio's `scripts/build_lumi.py`, then `tools/MutSea.AvatarCompile`. Verify it independently with `tools/MutSea.Content/verify_avatar_mesh.py`. `AvatarCatalog` accepts only original CC0, non-selectable `animated_object` packages. They must bind the pinned 133-bone skeleton (≤110 joints), have four strictly reducing LODs within an explicit avatar budget, 1–8 factor-PBR materials, and map every role to its native library animation. The small-asset 32-part/4,096-triangle limits are unchanged.

After persisted prims load (region modules load before prims), the adapter:

1. Stores the mesh and materials under content-derived IDs. Existing IDs must hold identical bytes.
2. Writes an exact native XML snapshot (`<region>.citizen-representation/<action>.before.xml`, mode 0600).
3. Journals `representation_intent` in the citizen WorldLine.
4. Replaces the body with the same root ID, owner, permissions and heading, at ground + `root_height`.
5. Journals `representation_result`, with an explicit preserved/retired component mapping.

A failure after deletion compensates from the snapshot. An unresolved intent is completed only if the world observably matches one side of it; otherwise citizen writes are blocked for an operator. A matching body is never re-migrated. A mesh or material change migrates again.

Rollback is explicit: unset `Representation`, set `RepresentationRollback = <migration action id>` and restart. The snapshot hash is checked; only the body created by that migration is replaced. Without configuration, a migrated body stays as journaled; nothing rolls back implicitly.

The animated profile roots at the pelvis, so navigation uses a representation-specific lift and walking pace: 0.9 m and 2 m/s for the linked primitive, `root_height` and 1.2 m/s for animesh. Obstacle checks stay ground-relative, so the historic behaviour is unchanged. Animation roles are `idle`, `walk`, `turn_left`/`turn_right` (turn in place before walking, and face outward after arriving home), `talk` (scaled to reply length) and `greet` (a human greeting through the viewer relay, or a greeting reply). They use exactly one native object animation at a time; PostgreSQL persists the set and startup resets it to idle. `lumi.avatar_binding` reports the representation, package, mesh asset, root height and current animation. The embodiment is excluded from generic WorldPlan edits.

Live verification uses `tools/Finalverse.CitizenSmoke` modes:

- `observe` / `verify`: representation-aware; `verify` encodes the runtime's eight-goal retention.
- `animesh compiled.json`: binding, UDP root/scale, ViewerAsset mesh hash, and animation states during a real walk (optional `FINALVERSE_WALK_FROM=x,y` native owner move) and a reply.
- `displace`, `greet` (timed with `FINALVERSE_GREET_AT`), and `animwatch`: `ObjectAnimation` packets on the wire.

### Package motions (compiled schema 2)

A schema-2 package may bind roles to its own validated native keyframe motions, `"package:<local_id>"`, instead of the native library motion. Every required role must be covered; `listen` is an optional extra role. `NativeKeyframe` accepts only LLKeyframeMotion v1.0 with priority 0–4, at most 30 s, at most 64 joints and 1,024 keys per channel, pelvis-only translation, and no emote, hand pose, constraints or trailing bytes. The compiler additionally checks joint names against the pinned skeleton.

Motions are stored as native animation assets under content-derived IDs. A package change that keeps the same mesh and materials is journaled as `package_updated` rather than migrated. A human greeting through the viewer relay plays `greet`; other messages play `listen` (at most 20 s) until the reply plays `talk`.

## Genesis wardrobe (worn humanoid avatars)

Worn Genesis avatars are compiled packages with `representation: attachment`: one rigged mesh skinned to the pinned skeleton, joint position overrides, an attach point and a generated alpha wearable (`IMG_INVISIBLE` on the selected head/upper/lower/eyes/hair regions). They have no motions or collision of their own; the wearer's agent animates and collides. The high-LOD budget is 40,000 triangles; animesh avatars keep 20,000.

```ini
[FinalverseGenesis]
Enabled = true
HostRegion = <region UUID that performs provisioning>
Assignments = <user UUID>|/config/genesis/<package>|<compiled.json SHA-256>; ...
; or, alone: Rollback = <wardrobe action id>
```

After prims load on the host region, each assignment is provisioned once while the wearer is offline, using the native services:

1. Content-derived mesh, material, alpha-wearable and object assets.
2. A `Genesis · <title> <version>` inventory folder holding the rigged object and the alpha item.
3. Current Outfit links to both.
4. Appearance with the Alpha wearable slot and the attachment appended on its point.

Adding items does not bump folder versions in this service, and viewers trust their inventory cache by version. The wardrobe therefore bumps the Current Outfit, Clothing and Genesis folder versions itself.

The journal (`genesis.wardrobe.jsonl`) stores the appearance before provisioning. Rollback restores it, removes the Current Outfit links and bumps the folder version; the user keeps the items. `Finalverse.CitizenSmoke wearer <compiled.json>` checks from a second account that the wearer's attachment carries the Genesis mesh.
