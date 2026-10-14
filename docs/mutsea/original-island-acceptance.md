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

At the original provisioning checkpoint, the source guard was not yet installed on the established primary/Tropical grids and the Tropical account's missing home was diagnosed only. The subsequent integration below supersedes that runtime status. The environment preset remains a reference, not a newly deployed EEP setting.

## Deliberate local integration and native creation — 2026-10-14

After confirming no active human sessions and the viewer was closed, both established local simulators received clean native backup/shutdown, private verified database/configuration/journal/old-module backups, the source-matched guard and restart. Deployed module SHA-256 is `347052c7ad9cdaff97c8050ad4beee27c16fcdf1dff1cac9cd58d352c6c07eac`. Tropical's namespace egress guard was installed while paused before client access. No image, PostgreSQL instance, public service or remote deployment was added.

All 5,635 primary prim rows and 1,472 Tropical prim rows were identical after integration, including IDs/transforms/ownership. All primary/Tropical inventory item rows (91/12), stored avatar appearance rows (54/10) and existing journal bytes were identical. The primary user's Nook was preserved. Historical operations without compatible postconditions remain guarded against automatic rollback; integration did not manufacture snapshots or migrate history.

Tropical's existing development account had no home. `[FinalverseAccounts]` used the existing identity, validated nine dry/level samples at `[193,107]`, assigned the home through native GridUserService at z=23.186, and confirmed it before readiness. No password, Genesis choice, outfit or inventory changed. The source-matched OAR probe passed `start_location=home`, matching account/RegionID, 1,472/1,472 native part transforms, five source texture payload hashes and normal logout; no appearance was sent. The probe builds with zero warnings/errors.

The original 498-part island also received one native viewer creation journey. A provisioned Auren account with a fresh private profile rendered correctly. A shoreline request from the crowded arrival area was refused; after a native local map teleport to the coast, the runtime grounded its approved reading-nook plan at `[194,150,22.4]`. Approval through existing prepare/commit created one bench, four pavers and two lanterns (seven groups / 29 parts). They visibly rendered. Nearby and WorldLine showed the created content and operation. Viewer Undo targeted only plan `70cb3ce6-582c-4ec3-8c17-3fc0d716c81f`; the objects visibly disappeared, and all 498 original persisted prim rows matched the pre-review baseline exactly.

The viewer review exposed and repaired a separate default-start-location comparison defect: an unspecified startup SLURL requests `last`, but was incorrectly reported as a fallback on a valid `last` reply. The native ARM64 Release build and three-case login regression suite passed, and a rebuilt deep-verified ad hoc package displayed Auren without the false warning in another fresh profile. Existing deployment-target linker warnings remain. See the viewer companion `docs/finalverse/phase4b/newcomer-creation-review.md` for details and scope.

Private integration/creation/Undo/home-login/build evidence and launch inputs live under `~/Finalverse/dev/onboarding-20261014`. No credentials, databases or raw capability logs entered Git. No repeated avatar screenshot or performance loop was performed. These are bounded local proof, not public onboarding or market acceptance.

Phase 4B remains **open** for final art/landscape and arrival treatment, public registration/character/experience choices and a first-time user study, capability-aware Lumi availability across worlds, continuous collision/LOD, controlled performance and distribution approval.

## Capability-aware first creation — 2026-10-14

The subsequent [experience discovery slice](experience-discovery.md) closes the bounded Lumi-availability gap above. All three local grids now run the same authenticated discovery module, with scene/inventory/appearance/sidecar state unchanged before protocol review. Native controller, observer and absent-citizen sessions passed. Finalverse's one combined island UI review verified direct Nearby results and guided nook/garden creation without falsely offering citizen control. The other Phase 4B product gates remain open; no new build/Undo or repeated visual/performance loop was required for this UI slice.
