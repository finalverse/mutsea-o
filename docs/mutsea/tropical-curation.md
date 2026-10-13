# Tropical curation and original fire circle — 2026-10-13

The passive Tropical OAR's two absent texture UUIDs both belonged to one 14-part group named `Zoomtada Fire pit`. A read-only native probe now records declared default/override texture bindings by runtime part, source identity and face. This is dependency evidence; it does not infer rendered topology or visible faces. Both absent dependencies were also absent from the original archive, so fetching arbitrary inherited service assets was rejected as a repair strategy.

## Original reusable content

Studio's `build_starter.py` generates `mutsea.starter.fire-circle@0.1.2`: seven original mesh parts, four numerical PBR materials, zero texture dependencies and one bounded native ember light. Independently reduced LOD totals are 1,056 / 578 / 224 / 102 triangles. The asset has editable Blender source, GLB/Collada representations, package-local semantics, component relationships and exact payload hashes. No inherited fire-pit geometry, textures, particle scripts or damage behavior is reused. Generated original content is proposed as CC0; generator code retains its separate source license.

`build_place.py --place coastal-fire-circle` composes the asset as `mutsea.starter.coastal-fire-circle@0.1.0`, pinning the exact original package manifest. Its 2 m half extents retain the existing PlaceCatalog minimum; the actual hearth is approximately 1.56 m wide. Instance authority, site and retirement IDs are deliberately outside the reusable template.

Validate the authoring package with `tools/MutSea.Content/validate.py`, then build `MutSea.ContentCompile` against the source-matched native SDK and compile into a new directory. Do not rely on a historical compiler binary: one retained pre-lighting binary omitted the manifest's light, and the native catalog correctly refused it. The current compiler preserves the light and passes the existing catalog's source/compiled equivalence checks. The initial authoring trial used `gather` as an asset affordance; validation refused it, and the final asset uses supported `interact`/`look_at`/`inspect`. Gathering remains a place-level affordance.

## Targeted native transaction

Only the separate loopback Tropical fixture was changed. The existing PlaceKernel validates terrain, native parcel rez policy, exact asset pins and the footprint before retiring the named group. Six surrounding floor/canopy/seating/foliage groups have broad 2D bounding envelopes that overlap the 4 m validation rectangle; their IDs are explicitly accepted in the operator plan and remain untouched. The new hearth's base aligns with the existing floor surface. Accepted overlap is not authorization to alter those groups.

The original group is serialized through the inherited XML serializer, hashed and flushed before deletion. The new seven-part asset is created through the existing AssetService and native mesh/material/light adapters. One PlaceEntry records intent, retirement, commit and fingerprints. Existing snapshot and fingerprint checks govern rollback; no OAR-wide replacement, database reset or second mutation executor was introduced.

Place semantics now include `semantic_metadata_available`, matching directly imported ContentKernel assets. The content tests verify that this field persists across journal reload and the semantic projection disappears after rollback. This lets the same native verification probe check an asset provisioned inside a place without weakening its semantic checks.

## Verified gates

| Check | Result |
|---|---|
| Authoring validator / current compiler / World module build | Passed; current builds have zero warnings/errors |
| Content transaction tests | 45 passed, including persistence, policy refusal, compensation and rollback |
| Original asset native tests | 14 passed; actual LLMesh LOD blocks and PBR/light source equivalence checked |
| Approved place catalog | Exact asset pin, footprint and one-light budget passed |
| OAR quarantine/reconciliation tests | 26 passed |
| Native replacement delivery | All 7 mesh/LOD assets, 4 PBR assets, 7 material faces, 7 semantic components and 1 light verified |
| Rollback | All 14 original part IDs restored, all 7 replacement IDs removed; 1,445-part original scene streamed with matching position/scale |
| Final replacement and clean restart | 208 groups / 1,438 parts; the other 1,431 inherited parts retained IDs, grouping, ownership, names, links, position, local offset, scale and rotation |
| Active declared texture closure | 182 referenced IDs resolve to nonempty native texture assets; the two missing IDs are no longer referenced |
| Native source texture samples | Five largest referenced JP2 payloads matched exact source hashes |
| Passive runtime | Task inventory remains empty; scripts remain disabled; outgoing namespace firewall reapplied after every restart, with negative external TCP probe |

Private source/native archives, database/config backups, retirement XML, final PlaceEntry, texture bindings and native verification receipts are retained under the local Tropical development evidence directory. They are not committed or distributed. A native probe receipt for the single asset explicitly identifies its containing operator PlaceKernel transaction; it is not represented as a client ContentKernel import.

The original archive remains deficient. This gate applies only to the explicitly curated instance. The reserved white-texture alias still retains the platform's opaque-white encoding with separately recorded source/native hashes. Full asset decoding, native visual quality, collision/spawn review, measured performance, shoreline AI creation, member-level rights review and WorldTemplate publication remain open. Tropical is still a separate local staging grid, not a Harbor teleport destination.
