# Finalverse world operations for MutSea

Opt-in region module; MutSea remains the simulator/world brand. Inherited scene, permission, streaming and backup APIs execute the changes. The new kernel contains no viewer, renderer or model dependency. New files use BSD-3-Clause under the repository's `LICENSE.txt`; inherited notices remain intact.

## Build and run

The standalone recipe now builds the opt-in module against the **same** freshly compiled simulator assemblies. This also keeps the account service and its provisioning callback compatible:

```sh
docker build -f deploy/standalone/Dockerfile -t mutsea:verify .
```

The optional add-on recipe remains available for a verified native base. It runs the kernel/content suites and recompiles the module against that base. Account provisioning additionally requires the matching updated `MutSea.Services.UserAccountService.dll`; an older base without its callback is unsuitable:

```sh
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

Use the existing standalone entrypoint/configuration procedure. The data directory must be writable by the simulator service user. For local fixtures, keep ports on loopback. The dedicated backend topology and its separate external-entry gate are documented in [backend deployment](../../docs/mutsea/finalverse-backend.md). Back up scene data and WorldLine together; packaging alone does not enable the module or public admission.

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
Assignments = "<user UUID>|/config/genesis/<package>|<compiled.json SHA-256>; ..."
; or, alone: Rollback = <wardrobe action id>
; optional QA gallery (independent of assignments and rollback):
Gallery = <x>,<y>,<floor z>,<facing degrees>,<spacing m>
GalleryPackages = "/config/genesis/<package>|<compiled.json SHA-256>; ..."
```

Quote list values. The INI reader treats an unquoted `;` as the start of a comment, so everything after the first entry would be silently dropped.

There is one assignment per wearer. A new pin for a wearer who already has an active Genesis outfit replaces it: the previous action is rolled back (journaled) before the new one is provisioned, so a swap needs a single restart.

After prims load on the host region, each assignment is provisioned once while the wearer is offline, using the native services:

1. Content-derived mesh, material, alpha-wearable and object assets.
2. A `Genesis · <title> <version>` inventory folder holding the rigged object and the alpha item.
3. Current Outfit links to both.
4. Appearance with the Alpha wearable slot and the attachment appended on its point.

Adding items does not bump folder versions in this service, and viewers trust their inventory cache by version. The wardrobe therefore bumps the Current Outfit, Clothing and Genesis folder versions itself.

Current Outfit links carry the order code viewers validate: `@` followed by wearable type × 100 plus the layer, so the alpha link is `@1300`. Attachment links carry none. Folder names add the form when it differs from the title, for example `Genesis · Ignis 0.2.0 (ignis-female)`. Folders made before this rule keep their names.

**Retired templates.** `AvatarCatalog.Retired` maps each retired package id to its successor. Today that is `finalverse.genesis.elara-male` → `finalverse.genesis.auren`, because Elara is exclusively female. Provisioning and the gallery refuse a retired package and name the successor; nothing is silently substituted. To move a wearer, assign the successor: the replace rule rolls back the old outfit, and items already in the inventory stay. The Studio registry `manifests/genesis-templates-v0.2.json` lists the active `package_id@version` templates and their pins.

**Alpha-hidden regions** are published by the viewer as `IMG_INVISIBLE` in the baked slots (viewer fix `6adb096935`). `CitizenSmoke wearer` reports, from a second account, the baked slots it receives and which regions arrive hidden.

The journal (`genesis.wardrobe.jsonl`) stores the appearance before provisioning. Rollback restores it, removes the Current Outfit links and bumps the folder version; the user keeps the items. `Finalverse.CitizenSmoke wearer <compiled.json>` checks from a second account that the wearer's attachment carries the Genesis mesh.

The QA gallery rezzes each pinned package (worn or animated) as a standing animated-mesh mannequin, in a row along the mannequins' left, on the host region. Each mannequin:

- is owned by the estate owner and is phantom;
- uses the package's own idle, or the native `stand` for worn packages, which carry no motions;
- has a content-derived object ID;
- is excluded from WorldPlans edits.

It is never attached to backup. Removing `GalleryPackages` and restarting removes it, and nothing is written to the region database. Worn and animated bodies share the skinned asset, so the gallery shows the same mesh, materials and skin a wearer would show, posed on its own native skeleton. It is the cheap way to look at a whole family at once in the native viewer.

## Places (PlaceTemplate composition)

A PlaceTemplate (`kind: place`, schema 1, CC0) composes operator-approved small assets. It has no meshes of its own and leaves the 32-part / 4,096-triangle asset limits untouched.

Each placement pins `package_id@version` with the asset's manifest SHA-256, and sets an offset and yaw inside the place's half extent (at most 32 m). The template also carries place semantics (title, description, tags, affordances) and entrances.

`PlaceCatalog` loads `<Catalog>/<dir>/place.json` and checks:

- every pin against the content catalog;
- every placement footprint against the bounds;
- totals: at most 96 placements, 65,536 triangles and 12 native lights.

```ini
[FinalversePlaces]
Enabled = true
HostRegion = <region UUID>
Catalog = /config/places
Provision = "place_id@version|x,y,z|yaw|retire-group-id,...|accept-group-id,...; ..."
; or, alone: Rollback = <place plan id>
```

`PlaceKernel` turns a template into one journaled operation (`<region>.places.worldline.jsonl`):

1. **Check the place as a whole:** region bounds, dry ground, native rez permission and fresh IDs; no foreign object may intrude. Siblings overlap by design.
2. **Retire** the listed sandbox groups: write an XML2 snapshot with its SHA-256, then delete.
3. **Create every placement** through the same native content adapter, with the yaw applied about the anchor.

If any step fails, the operation compensates: it removes what it created and restores what it retired. Anything unconfirmed, or an interrupted intent, fences further place writes for an operator.

Accepted groups are existing objects the operator explicitly allows inside the footprint, such as a ground path running beneath a deck. They are recorded in the plan and never modified. Placement footprints use the horizontal extent of each part; a part tilted off vertical keeps its full 3D diagonal.

Rollback refuses if any placement changed since provisioning. Otherwise it deletes the placements and restores the retired objects with their original IDs.

Agents see each placed part's own semantics plus its place context (place identity, role and entrances in world coordinates) through the same semantic lookup as content imports. Provisioning runs as the estate owner, on the host region's prims-loaded hook, and is idempotent per `place_id@version`.

Offline checks before deploying a place:

- `MutSea.ContentNativeTests <catalog> --places <place root>` loads the real catalog, with the native material validator, and every place.
- `Finalverse.CitizenSmoke semantic` (with `FINALVERSE_ENTITIES`) shows what an agent learns about placed parts through the World relay.

**First place: Harbor Pavilion v2.** It was provisioned at (128,192) over the v1 sandbox pavilion, which it retires (12 groups), and accepts the North Promenade. Rollback and re-provision were rehearsed on the live fixture. See the viewer's `docs/finalverse/art/harbor-pavilion-validation.md`.

**Citizen navigation footprint.** World snapshots carry `body_radius`, the horizontal reach of the parts that collide at body height (0.35–1.8 m above ground). An upright part uses its XY extent; a tilted part uses its 3D diagonal. The citizen navigator uses it when present. Tree canopies without collision, and roofs overhead, no longer close walking corridors, while trunks, benches, walls and columns still block. Sources that cannot tell keep the original root-based rule. Placement safety (`clearance_radius`) is unchanged.

Verified on Harbor with the cottage garden around Lumi's home: Lumi walks home from the front-west (7 waypoints), from directly behind the house (20) and from the back-west (13).

**Operator probes.** `CitizenSmoke hold` keeps the controller avatar present for human-gated controller commands, such as Lumi's garden proposal and approval; it performs no action itself. `CitizenSmoke restyle` submits operator-authored `set_color` plans through the guarded prepare/commit path; the plans are journaled and undoable.

## Worlds (WorldTemplate provisioning)

A WorldTemplate (`world.json`, `kind: world`, schema 1, CC0) is a reusable starter world. It composes approved places, each pinned to the SHA-256 of its exact `place.json`, with an anchor that may follow the terrain and a yaw. It also carries water height, spawn and an environment preset reference, plus an optional region-sized R32 heightmap (`terrain.r32`), pinned by SHA-256.

```ini
[FinalverseWorlds]
Enabled = true
HostRegion = <empty target region UUID>
Catalog = /config/worlds
Provision = world_id@version              ; or, alone: Rollback = <world operation id>
ExportRegion = <region UUID>              ; optional: export that region's terrain once
ExportTerrain = /data/finalverse/<name>.r32
```

`WorldTemplateKernel` provisions into an **empty** region as one journaled operation (`<region>.worlds.worldline.jsonl`):

1. Snapshot the region's terrain (R32 plus its SHA-256) and water height.
2. Apply the template terrain and water.
3. Provision every place, in order, through that region's PlaceKernel. Each place keeps its own journal and rollback.
4. Set the landing point.

If a place fails, the operation rolls back the places already provisioned and restores terrain and water. Rollback does the same for a committed world, after checking that the terrain snapshot is unchanged.

Place rectangles may overlap in the catalog, because they include empty margins. The live check refuses any existing object inside each new place's footprint, including the parts of earlier places. Order places from large to small.

**First starter world: `mutsea.starter.harbor-world@0.2.0`.** It holds Harbor Arrival, the pavilion, the Lake Overlook, and the North and Lake walks, on Harbor's exported terrain (`b70f57ee…`) with water at 20 m. It was provisioned into the fresh "Harbor Template" region: 180 groups and 669 parts. A second account teleported there in 0.3 s and received all 669 primitives. The world was rolled back (region emptied, terrain restored) and re-provisioned.

**Running the simulator container.** The console reads stdin. Without `-it` (OpenStdin and TTY), it prints prompts in a tight loop, which floods the container log until the disk fills. Recreate the container only with `-it`, attached to both the database network and `bridge`, with the same volumes and user.

## Newcomers (account → appearance)

An operator can prepare new local accounts that arrive already wearing a chosen Genesis template:

```ini
[FinalverseAccounts]
Enabled = true
HostRegion = <region UUID>                     ; the arrival region
Newcomers = /config/accounts/newcomers.json    ; owner-only (0600)
Home = 128,128                                 ; optional arrival x,y; default the region centre
```

```json
{"accounts":[{"first_name":"…","last_name":"…","password":"…","genesis":"/config/genesis/kai-female-v0.1.0","genesis_sha256":"<compiled.json SHA-256>"}]}
```

On the host region's `PrimsLoaded`, each newcomer is processed in turn:

1. **Account.** A missing account is created through the inherited account service, the same path as the console `create user`. That covers the password, inventory and starter appearance, with activation only after the appearance is verified. A failed creation stays stored and inactive for operator repair.
2. **Home.** A newcomer without a home gets the host arrival. While regions are still loading, the account service finds no default region and leaves the home unset.
3. **Genesis outfit.** The chosen template is applied through the [Genesis wardrobe](#genesis-wardrobe-worn-humanoid-avatars) before the first login.

Each step is journaled to `accounts.newcomers.jsonl`, with account ID, name, provisioning state and level. Passwords are never logged or journaled, and file-validation errors never quote the file.

Re-running is safe:
- An existing account is never re-keyed or re-levelled, and its home is never moved.
- The Genesis assignment is idempotent.
- No rollback deletes an account. The Genesis outfit is rolled back through the wardrobe.

The newcomer file must not be committed. Use the console `create user` for interactive accounts. A command script would echo its arguments, including the password, into the log.

## Composed places (R&D: AI Place Composer)

A person asks for a place ("make a picnic spot for six people"). The local AI gateway's model chooses only enumerated options, and its deterministic solver lays out a draft `place.json` from approved Starter Library packages. The gateway returns one `compose_place` World-plan action carrying the draft, the site rule `in_front_of_actor` and provenance (solver version, intent, adjustments). The viewer's Create panel passes it through, unchanged, to `prepare`.

```ini
[FinalverseComposer]
Enabled = true
Regions = <region UUID>[,<region UUID>...]
```

`ComposerBridge` treats the draft as untrusted data:

- **Identity.** The place ID is rewritten to `user.<actor>.<slug>` with a timestamp version. The draft must have exactly PlaceTemplate's fields; unknown fields are refused.
- **Validation.** It is validated exactly like an approved place (`PlaceCatalog.Validate`): every package pinned to the approved manifest SHA-256, plus the bounds, the 96-placement / 65,536-triangle / 12-light budgets and the semantics vocabulary.
- **Site.** MutSea computes it, never the model or gateway: the place's entrance faces the person, just in front of them. The ground across the footprint must be level within 0.6 m.
- **Rights and space.** PlaceKernel's site check runs with the person's own rights: the native parcel rez policy for every part, dry ground, inside the region, and no existing object in the footprint (refused with its name).
- **Preview.** `prepare` returns one step per kind of piece, plus a summary built from the validated draft and any adjustments the solver made. It expires after 2 minutes.
- **Build and undo.** `commit` provisions the place as the person, so they own the pieces; it is journaled in the place journal. `undo` runs PlaceKernel's fingerprint-guarded rollback.
- **Limits.** At most 12 compositions per person per hour, and 64 pending previews. The relay body limit is now 64 KB, so the largest drafts fit.

Composed places get place semantics like any other: the World relay's `semantic` op returns their title, roles and entrances.

## Grounded compositions and citizen tools

ComposerSiting evaluates at most32 nearby candidates against native height/water data, then the existing PlaceKernel checks full object footprints and parcel rights. near_water requires actual submerged terrain within12m; decorative mesh water and names are not shoreline evidence. Commit revalidates the exact approved site. Original in_front_of_actor recipes retain their one forward candidate. Failed searches mutate nothing.

Prepare reports the actual part_count and a human siting rationale. Human inspection/history joins bounded summaries from existing action/place journals, allowing Undo recovery after viewer restart. It does not create another persistence store. `prepare_place`, `commit_place` and `undo_place` are restricted citizen tools over this same executor. The bridge tracks the configured citizen/controller's own prepared and committed IDs and restores those sets from its existing journal. Human approval and the Rust runtime's192-part/180-second goal budget remain mandatory.

`tools/MutSea.ComposerTests` tests deterministic/wet/dry/steep/edge/changed-ground rules and owned journal/Undo selection. With only .NET10 installed, run net8 tools using DOTNET_ROLL_FORWARD=Major. `tools/Finalverse.CitizenSmoke` co_build_verify and co_build_undo_verify take a fourth private committed-place JSON receipt and are read-only; use the separately built portable OpenMetaverse SDK.
## Authenticated planner transport

`[FinalversePlanning] Enabled = true` enables `op: plan` on the existing
authenticated `FinalverseWorld` capability. `SocketPath` is an absolute private
Unix-socket path, default `/run/finalverse-planner/planner.sock`. Deployment uses
`compose.planning.yaml` with the private gateway service running as simulator
UID 1000. `experience` advertises `server_planning` to the viewer.

`PlanningBridge.cs` authenticates the active root session, accepts only a prompt
or WorldSpec, derives scoped native context and applies bounded actor admission.
`ServerPlanner.cs` supplies bounded HTTP-over-Unix-socket transport. Inference
does not hold the scene transaction gate; the session is rechecked afterward.
No planner response executes directly: existing Prepare, explicit approval,
Commit, WorldLine and guarded Undo remain authoritative.

`tools/Finalverse.PlanningTests` checks input/context boundaries.
`tools/MutSea.PlanningSmoke` exercises the named private pilot deployment with
strict TLS, real login/capabilities, inference, policy, UDP observation, restart
and Undo. Its private account and receipts must stay outside Git. The probe
does not alter appearance and never targets an arbitrary grid or user.

See [native certificate renewal](../../deploy/standalone/native-certificate-renewal.md)
for the separately scoped certificate hook, safe deferral and retry timer.
