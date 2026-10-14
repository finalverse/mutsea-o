# Coastal arrival WorldTemplate — local native acceptance

The subsequent world v0.3.0 composes arrival, promenade and pavilion as a bounded reusable court (95 placements / 381 parts), with the preserved nook and fire circle yielding 422 world parts. Prior versions remain available without implicit migration. The native avatar-only probe also covers the full promenade and offset return. See [selected newcomer activation](newcomer-activation.md) for the concurrent-session finding, verified provisioning fix and remaining public-onboarding gates.

The original Starter Library now has `mutsea.starter.coastal-arrival@0.1.0`, composed from two new original small assets and already approved dependencies. `mutsea.starter.aurens-island@0.2.0` changes only the arrival dependency; original terrain, 20 m water, spawn and the other three places are retained. Existing v0.1.0 worlds are preserved and receive no implicit upgrade. Studio owns editable authoring data; MutSea remains canonical authority for instances, accounts, ownership, scene state and WorldLine.

The arrival is 27 placements / 106 parts / 16,628 high triangles / four lights, versus the older arrival's 70 / 218 / 30,848 / nine. A new canopy has six parts, four individual solid posts and noncolliding overhead shell/edge. A planter has three parts, a solid low base and noncolliding hedge/blooms. Native LOD totals are 1,148/628/250/110 and 708/388/154/70. Material/semantic/collision/source hashes pass the existing native catalog; no small-asset or place limit was relaxed and no new executor introduced.

## Isolated provisioning and account checks — 2026-10-14

A named local test world uses the existing PostgreSQL service with a separate database/role, separate private config/data volumes and loopback TCP/UDP 18099. A namespace egress guard is installed while paused before client access. It reuses the current runtime image and source-matched World module (`24412b14…`), creates a fresh test identity and uses existing FinalverseWorlds/Places/Accounts and the pinned Auren wardrobe. It does not alter the primary, curated Tropical or original v0.1 island. No remote/public deployment or push was made.

Provisioning commits four places / 91 groups / 386 parts. The source-matched WorldTemplateSmoke's explicit `--hero-arrival` mode verifies native home login, expected identity/RegionID, all linked mesh bindings, four semantic contexts, eight Current Outfit links and raw authenticated inventory ownership, then confirmed logout. The original three-argument 18098 fixture remains unchanged. The additional fixture is explicitly named, loopback-only and cannot choose a remote endpoint.

`tools/MutSea.ArrivalSmoke` restricts login and setup to this exact local region, an owner-only registered test account and committed v0.2 world proof. It validates bounded input/coordinates, actual streamed root positions and uses native avatar movement packets. Teleports only position the test avatar before a case; the tested paths use continuous walking, with no flight or teleport across the obstacle. Accepted cases:

- Full 18 m arrival-axis walk, with lateral/height bounds.
- Open central approach beneath the canopy, without climbing.
- The tall solid canopy post stops native walking.
- The provisioned rotated bench supplies the expected native seat object; avatar Sit then Stand and confirmed logout.

The probe sends no model, appearance, scene-edit, provisioning or Undo request. It moves only its own test avatar; normal position persistence is expected. Private raw SDK logs, native receipts, journals/database and configuration remain outside Git.

```sh
dotnet build tools/MutSea.ArrivalSmoke/MutSea.ArrivalSmoke.csproj -c Release -p:ProbeBin=/absolute/native-sdk
dotnet tools/MutSea.ArrivalSmoke/bin/Release/net8.0/MutSea.ArrivalSmoke.dll PRIVATE_ACCOUNT WORLD_PROOF NEW_RECEIPT
```

The probe and updated WorldTemplateSmoke build with zero warnings/errors. Studio's 13 checks pass; each new compiled asset passes 15 native content checks including real LOD/hull decoding, fingerprint persistence and tamper refusal. Four-place catalog validation passes. A single combined near/far native viewer review is accepted, with raw pixels, graphics/LOD settings, foreground gating and frame CSVs retained. Local development sample: 118.6/116.4 FPS, p95 12.18/12.12 ms at 1280 × 738 on an Apple M1/16 GB host, shadows/SSAO/AA disabled. This does not close release performance or premium-art gates.

The isolated hero simulator also passes clean native backup/shutdown/restart: all 386 persisted prim rows and both World/Place journals are identical, with no duplicate provisioning and the namespace egress guard restored. Primary and Tropical scene/history are unchanged. The original island's 498 original parts are unchanged; its previous history remains an exact prefix with 12 new entries from the user's three creation/Undo cycles. Those legitimate entries are retained, never restored away. The original island viewer/profile and the user's unsubmitted request are restored after the capture.

## Compatibility and rollback

The new WorldTemplate follows the [existing native provision/rollback contract](world-template-provisioning.md), including exact place/source pins, terrain/landing/water postconditions and later-edit conflict checks. The earlier original-island rollback/fault/reprovision proof remains relevant because that runtime is unchanged. No new full rollback/fault cycle was repeated for this art-only composition. Existing accepted worlds, user Nook, inventories and journals are preserved. Do not remove a live world or restore a full database to promote this content; instantiate the explicit new version in a new empty world, or plan a separately approved migration with retained snapshots/history.

The code/procedural content declares its inherited Starter Library provenance and original-content CC0 intent; source/runtime/environment/character rights remain separately audited before distribution. Phase 4B stays open for premium terrain/planting/material and circulation work, lighting variants, broader collision/LOD, public onboarding/choices/user study, controlled release performance and distribution.
