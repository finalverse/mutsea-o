# Original island native acceptance — 2026-10-14

`mutsea.starter.aurens-island@0.1.0` is an original reproducible WorldTemplate foundation. This verifies the existing runtime's provisioning and recovery contracts; it does not approve final art or public release. See [the implementation contract](world-template-provisioning.md).

The loopback fixture used a fresh region, estate owner and newcomer, a separate PostgreSQL database/role, private configuration/data volumes and a namespace egress guard. It reused the existing PostgreSQL service and runtime image. The established Harbor/OpenVCE/Harbor Template and curated Tropical grids, user creations and open native viewer were not restarted or modified. No public deployment or GitHub push was made.

| Check | Observed result |
|---|---|
| Original package | Four exact place pins; 134 linked groups / 498 parts; original terrain and generator provenance; no imported OAR |
| First account provisioning | Native account service produced inventory/core appearance; pinned Auren wardrobe supplied attachment/alpha; missing home set to dry arrival above ground and read back |
| Native home session | `start_location=home`, expected account/RegionID and authenticated World.inspect UUID; 498/498 expected linked mesh parts; all four semantic place/world operation contexts |
| Inventory | Eight Current Outfit links; six typed core wearables plus Genesis attachment and alpha; raw authenticated FetchInventory2 permissions.owner_id and item bindings verified |
| Restart | World operation/place IDs preserved, no duplicate provisioning; home, inventory, scene and semantics reverified with normal logout |
| Guarded rollback | All four native place plans rolled back; 0 persistent parts; actual native terrain export matched the before-snapshot hash; persisted parcel landing position/facing/type and water matched before values |
| Identity preservation | Inventory rows and stored appearance rows identical before/after world rollback; no account or wardrobe rollback |
| Interrupted-world fence | Deliberately injected incomplete intent in the private fixture; inspection reported recovery, all 498 parts/four semantic contexts remained readable, and prepare/commit/undo/content-preview returned the recovery rejection before execution |
| Reprovision | 498 fresh entity IDs, disjoint from the rolled-back IDs; same world manifest and durable terrain; new world/place operations |
| Source checks | 50 content tests, ten composer/siting-audit tests, nine Studio world/place tests; module and native probe build with zero warnings/errors |

The source terrain SHA-256 is `621d32e3acc2fa4422310cfb6ee19c680dfc6dfaf30b11b6f416c78e3b891305`. The native durable terrain fingerprint is `c0f59218c7bfc66540e2d0edd761fb458bf9547b45ac7cc2320073584e9c009e`, with format `mutsea-variable2dgzip-r32-v1`. MutSea's inherited serializer rounds heights to millimetres. A first prototype used raw float bytes and correctly refused rollback after restart without writing rollback intent or deleting entities. Its complete journals/database/data were archived unchanged; a fresh fixture established the corrected postcondition. No historical WorldLine was rewritten to force a pass.

Representative native content probes in that preserved prototype verified the same pinned bench and fire-circle packages: 13 bench mesh/LOD assets and semantic/PBR components, two material assets, rotated native Sit/Stand and occupied-place Undo refusal; seven fire-circle mesh/LOD/semantic/PBR components, four material assets and one native light. These are protocol/behavior checks, not new visual art acceptance. The final island protocol check verifies every linked mesh binding and each place's canonical world provenance.

The old probe SDK's Simulator.ID is distinct from RegionID, and its inventory parser reads per-item agent_id while native responses place ownership under permissions.owner_id. The probe uses native RegionID, authenticated inspect and the actual inventory wire response; it neither weakens ownership checks nor changes the mature runtime protocol. Logout confirmation waits for the native asynchronous reply. Earlier probe failures are retained in private evidence and excluded from the accepted receipts.

The fault test retains its before/injected/after journals and negative-request receipt. After clean shutdown, the fixture baseline was restored only after confirming byte-identical injected history and unchanged persistent scene rows. This removes a deliberately synthetic test intent; it is **not** a production recovery/migration procedure and does not authorize rewriting a real interrupted transaction. The final normal session verifies the fence clears after the test baseline is restored.

The disposable raw-fingerprint prototype's stopped container, two volumes and separate database/role were removed only after backup decompression/hash validation. The corrected sandbox remains available for the next bounded work. No new Docker image or PostgreSQL instance was introduced. Private accounts, databases, journals, capability logs, config and operator harnesses stay outside Git.

Phase 4B remains **open**. Final art/landscape treatment, actual first-session viewer UX and appearance baking, continuous collision/LOD, controlled performance and distribution approval remain separate gates. The environment preset is a reference, not a newly deployed EEP setting. The source guard is not yet installed on the established primary/Tropical grids; updating them requires a deliberate session-preserving integration. The existing Tropical account's missing home was diagnosed, not silently repaired.
