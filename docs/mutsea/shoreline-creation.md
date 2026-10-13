# Grounded shoreline creation — 2026-10-13

An actual local-model request now creates a small reading nook in the isolated Tropical fixture through the existing World API. The model chooses bounded PlaceIntent options. The gateway's deterministic solver emits a `compose_place` WorldPlan using exact Starter Library pins; MutSea chooses the site from real terrain, validates native permissions and object overlap, previews it, and commits through PlaceKernel/WorldLine. No model coordinates, arbitrary authoring code, replacement executor or public deployment was introduced.

## Native acceptance

The request asked for a small reading nook near water with two benches and soft lanterns, on grass without planting, roof, paving or a centerpiece. One `llama3.2:3b` call took 11,844 ms. MutSea preview/commit took 265 / 345 ms and chose anchor `[193,115,22.086]`, yaw 0, with wet terrain sampled 8 m away. All nine footprint samples were dry and within the existing 0.6 m slope tolerance. The actual placement count is four: two 13-part benches and two 4-part lanterns, totaling **34 parts**.

The isolated region's existing composer was enabled and its approved catalog was matched to all 18 exact gateway module pins. The separate PostgreSQL database/configuration were backed up first. Harbor's catalog was read without modifying Harbor or its current user creations. The Tropical fire circle and all other 1,438 pre-existing scene parts were preserved.

The first build survived native backup/shutdown/restart. Every mesh/LOD, PBR material, face binding and semantic component was verified through actual native capability/UDP delivery. Both rotated benches passed Sit/Stand; attempting Undo while seated was refused before any rollback intent was written. Two native lights matched their pinned factors. The first named creation was then undone, and a full read-only database comparison confirmed exactly the prior 1,438-part curated island state.

The saved WorldSpec was replayed without inference: gateway 14 ms, native preview 218 ms and commit 514 ms. It produced the same approved site and four-place layout with fresh transaction/instance IDs. After another clean restart:

- The scene contains **212 groups / 1,472 parts**, including 34 new parts and 1,438 unchanged prior parts.
- Identity, ownership, grouping, names, link numbers, group position, local offset, scale and rotation match the expected native state; no first-undone IDs remain.
- Native delivery verifies all 34 mesh/LOD assets and semantic components, eight material assets across the four placements, 34 material-face bindings, two seat targets and two lights. Occupied Undo remains refused on both benches.
- The whole-scene probe streams all 1,472 expected parts and verifies five referenced source JP2 payload hashes, then logs out normally.
- Scripts remain disabled, task inventory remains empty, and the simulator's outgoing namespace guard is reapplied after each restart.

## Verification fixes

`Finalverse.CitizenSmoke compose/compose_undo` no longer requires a Lumi embodiment. These are human World API operations and must work in a fixture without a provisioned citizen. Composition uses the authenticated native `inspect` result instead of a synthetic region ID and estimated avatar-minus-height context. Explicit teleport setup is limited to the named loopback Tropical port and bounded coordinates; it only positions the acceptance-test avatar. MutSea still selects the actual site.

The compose probe identifies created root groups by the exact committed operation ID, verifies native instance ownership, and counts their linked parts. Raw scene-count deltas were misleading while background scenery streamed; they are retained only as labeled diagnostics. Named Undo's actual deletion proof comes from its canonical IDs and the database comparison, not that asynchronous delta.

`MutSea.ContentSmoke` accepts an explicit containing PlaceKernel plan in its private receipt. It routes occupied Undo and journal checks to that containing transaction rather than treating a placement as a standalone ContentKernel import. Its native Sit check follows `ScenePresence.SendSitResponse`: root-relative position and world orientation, including child/root rotation. This fixed a probe mismatch on correctly rotated benches without changing the mature simulator's seating behavior.

## Scope and remaining gates

Ten composer siting/audit tests pass, including wet-terrain grounding, dry-world refusal, slope/bounds/nonfinite rejection, changed-terrain revalidation and actor-scoped history/Undo recovery. Both rebuilt native probes have zero warnings/errors. The verified private WorldSpec, canonical PlaceEntries, native delivery checks, database/config backups and combined viewer review are retained under the local shoreline development evidence directory, outside Git.

The native viewer shows the benches and two lanterns on dry shore and the original fire circle within its preserved surrounding furniture. Imported island art remains a compatibility/authoring seed. The login's fallback-arrival warning needs home/start-location cleanup. Full onboarding/appearance provisioning, human visual sitting acceptance for this placement, continuous collision/LOD review, final island art, measured product performance, reusable world packaging and member-level redistribution clearance remain open. Tropical is a separate local grid, not a Harbor teleport destination.
