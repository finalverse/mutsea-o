# OAR compatibility experiment — 2026-10-10

**PASS for a passive, isolated OpenVCE prim-based region.** This is the integrated Phase 4B archive compatibility milestone. It does not replace Harbor, publish a Starter Library template or complete Phase 4B.

## Source and rights

The creator's [OpenVCE project](https://openvce.net/) provides the [2022 archive](https://openvce.net/downloads/2022/OpenVCE.oar). Its [version 1.4 attribution/license document](https://openvce.net/downloads/2022/Readme-OpenVCE-Assets.txt), dated 27 February 2022, offers LGPL-3.0 or alternative CC-BY-4.0 for the OpenVCE models. Attribute Clever Zebra's design/development and AIAI, University of Edinburgh contributors Austin Tate and Stephen Potter, as described there. Other modular contributors retain their respective licenses. Whole-archive redistribution clearance remains **open**; no source archive or imported asset is committed or published. This is legacy compatibility content, not final Harbor art.

| Input | Bytes | SHA-256 |
|---|---:|---|
| OpenVCE.oar | 17,346,885 | `8f85eef46c43b6832503a5eaf9c21f21857b8398983a3303b74c8da8bae34399` |
| Creator README | 2,098 | `07023a9fa3b3f8519d11e943991ea5dd9f28aa3a05905a25bd1839e8a32f15c3` |
| Passive derivative | Measured separately in private report | `30ed5f94444953eaf80fa2043b4a1f8994bdd518a6b7c674f78f93e4f966614b` |

Private source, snapshots, accounts, logs and receipts are under `~/Finalverse/dev/phase4b/oar-20261010`, outside repositories. Converter `mutsea.oar.quarantine/0.1.0` inspected 937 regular entries and 51,453,524 expanded payload bytes. All original bytes and member hashes are retained.

## Source-derived native boundary

`MutSea/Region/CoreModules/World/Archiver/ArchiverModule.cs` registers `load oar`, including merge/replacement, terrain/parcel and asset options. `default-user` resolves a **First Last name**, not a UUID. `ArchiveReadRequest.DearchiveRegion()` normally clears the scene unless merging, imports assets/settings/terrain/parcels, resets object UUIDs and starts scripts. There is an internal boolean script-start overload, but no inspected console quarantine option. The inherited tar reader is not a bounded untrusted-upload validator. The console restore is not routed through Finalverse's ContentKernel/WorldAction journal or Undo.

The new [offline quarantine](../../tools/MutSea.Oar/README.md) checks bounded archive/XML structures before the native reader sees them, normalizes names and removes executable/inventory/media content. Native script engines are disabled independently. No server archive API or renderer was rewritten.

## Isolated runtime

Fresh database `mutsea_oar`, nonprivileged database role, PostgreSQL **16.14 ARM64**, separate data/config/secret volumes, no Harbor mounts and only loopback TCP/UDP port **18095**. Original Harbor remains stopped and retains its own PostgreSQL/data volumes. New region UUID `b9cdc55f-f947-4378-a439-3c36522ebf1f`; the inherited standalone generator still labels this separate region “MutSea Harbor.” The display label does not identify the original region.

The retained ARM64 simulator image `mutsea:citizens-phase3-20261008` has image ID `sha256:3e75b46160f230632a74c162d3499499bc62c3c5a78b58f573dc42742da089a4`. It uses BulletSim/Meshmerizer. The experiment overlays three previously tested native DLLs from the stopped local PostgreSQL fixture; it is **not** a fresh reproducible production image build:

| Assembly | SHA-256 |
|---|---|
| MutSea.Data.PGSQL.dll | `8976098b0783174d783f9a597ecbe5e1bfab8d817deee8613423c03416f20d51` |
| MutSea.Services.UserAccountService.dll | `7325eb039524667fa37ccf4415e4466af354e4e766a709250067de98095f9bf2` |
| MutSea.Services.LLLoginService.dll | `92f6f2fecb16a732f75c0e6aba4dd98ba56d0566dc1910cc01bdf8caa72d5740` |

YEngine, XEngine and NPC modules are disabled. No AI gateway or Lumi citizen is attached to this staging world. New account **MutSea Developer** has an ordinary starter appearance; its red pants are unrelated to the original FirstLook outfit-color regression. Credentials remain in the private fixture file.

The simulator has dropped capabilities. A one-shot namespace firewall permits client replies and its separate PostgreSQL connection but rejects other output. Docker internal/non-masquerading networks alone allowed a direct external TCP connection; a negative outbound probe passed only after this firewall was installed. The guard was reapplied while paused after restart. This manual fixture isolation is not a deployed upload/conversion service.

## Actual restore and acceptance

An empty-instance SQL snapshot and private config were saved first. Native console commands were `load oar /import/staging.oar`, `backup`, then `shutdown`. Import logs span approximately one second; this is one local observation, not a performance guarantee. The stopped restored database was snapshotted before clean restart. `Startup.txt` is empty: restart does not replay the import.

| Check | Actual result |
|---|---|
| Geometry | 459 groups / **4,090 parts**, no missing or unexpected groups |
| Native identity | All 4,090 runtime UUIDs are new; source UUIDs/creator/owner provenance retained in receipt |
| Remap verification | Unique root/full local transforms and retained link-number tie-break; ownership/position/offset/scale/rotation pass; zero link renumbering in this archive |
| Passive assets | **283** stored payloads byte/hash verified, including 269 textures, 12 notecards, one animation and one sound |
| Quarantine | **141** script sources and **50** nested inventory-object assets absent; **397** task inventories and one media block removed |
| Persisted scripts | Region `DisableScripts=true`; script engines disabled; `primitems=0` before and after restart |
| Terrain/parcels | Native R32 256 × 256 terrain and one parcel restored; no mesh-terrain or multiregion claim |
| Texture closure | 181 nonzero referenced texture UUIDs resolve; zero UUID is the extra sentinel; no missing nonzero references |
| Delivery | Actual loopback login/region handshake/capability check; all 4,090 parts received with matching positions/scales |
| Asset HTTP | Five largest referenced JP2 samples fetched through native GetTexture; exact hashes match |
| Restart | Clean native shutdown, SQL snapshot, restart/egress reapply, same mapped UUIDs; delivery/assets/normal logout repeat successfully |
| Viewer | Actual ARM64 QuickCheck package logged into the separate fixture; textured Plaza Hub and human starter avatar visible; one native screenshot reviewed |

Primary private receipts: `native-runtime-report.json`, `database-mapped-verified.json`, `texture-closure.json`, `protocol-assets.json`, `protocol-restart.json`, `protocol-hardened.json`; native evidence `native-openvce-plaza.png`. Package executable SHA-256 `633e186f687d42bb3561221db97e169960f11fbd29f4c996fa12f0d336da2da7`; C++ binary is unchanged. No eight-view benchmark or final visual/performance acceptance was repeated.

**26/26** quarantine/reconciliation tests and **32/32** viewer launcher/queue/comparison/process tests pass. Native probe build has zero warnings/errors. Early failures remain private: missing published port, using simulator `.ID` instead of `.RegionID`, comparing pre-reset UUIDs, trimmed object names, checking asynchronous logout too early, a missing local .NET 8 runtime and a refused duplicate-account login. The final hardened probe passed after the GUI session quit normally. Final receipts supersede those attempts; they are not erased or counted as passes.

The first staging grid file lacked native `keyname`. The packaged viewer rejected it, cleared one-session credentials and disabled autologin before showing its default login UI; no login/authentication-start marker was observed. We quit it, fixed the profile and strengthened launcher validation to require the complete native grid definition. The corrected guarded launch passed. This does not resolve the separately recorded historical cached Agni provenance incident.

## Compatibility scope and next gate

| Feature | Evidence in this increment |
|---|---|
| OAR 0.8 / prim geometry / R32 / settings / parcel / JP2 | Tested as above |
| Native archive GNU long names | Offline adversarial fixture and actual source pass |
| Script/task inventory | Removed; safe exclusion tested; execution/inventory compatibility deliberately unclaimed |
| Mesh/PBR OAR, multiregion OAR, XML2 direct import, IAR and every animation/sound | Not exercised by this source |
| Permissions/licensing/semantics of every imported entity | Source ownership retained; instance ownership verified; full permission/provenance/semantic review open |
| WorldTemplate transaction/versioning/provisioning/rollback | Not implemented by this restore |

Next: select a rights-cleared reusable component, verify all its dependencies, semantics, collision/LOD/materials and native interaction, then package it through the existing content registry and transaction boundary. Imported content is input to that review; a successful archive restore does not make it production Starter Library content. Genesis nonhuman Lumi and humanoid Elara must independently pass native skinning/animation/persistence gates.

## OpenVCE as a second Harbor region (local fixture, 2026-10-10)

At the owner's request, the script-free derivative (`openvce-passive-v010-release-check/staging.oar`, SHA-256 `30ed5f94…`) now runs as a second, non-adjacent region of the local Harbor grid. That makes it reachable by teleport. It is not published, and whole-archive redistribution clearance remains open.

- **Region.** `OpenVCE Plaza` (`fc6342cb-6166-4d4e-b04a-b50701f398e8`), Location 1010,1000 (Harbor is 1000,1000), UDP 18096 on loopback. It joined the existing default estate.
- **Safe import.** A console `load oar` with no region selected applies to *every* region of an instance, so the archive was not loaded into the running Harbor instance. A temporary instance hosted only the new region, against the same PostgreSQL, with Finalverse modules disabled and `startup.txt` = the load.
  - Result: 283 assets restored (7 already present), 459 groups / 4,090 prims, terrain, settings and 1 parcel; 0 scripts.
  - The simulator persists loaded objects on its periodic backup and exits on SIGTERM without a final backup. Wait for the prims count before stopping.
- **Harbor startup file.** Harbor's `startup.txt` still replayed `terrain load` and `load xml2`; with two regions both would apply to every region. It is now empty, as the deployment guide requires. The old content is kept as `startup.txt.pre-openvce` in the config volume.
- **Container.** Recreated from a `docker commit` snapshot (`mutsea-finalverse-phase4b:harbor-r4-20261010`, which preserves module overlays and `config-include`), adding UDP 18096. The previous container is kept stopped as `…-pre-openvce`.
- **Verification.** The real protocol (`Finalverse.CitizenSmoke teleport`) passed: Harbor → OpenVCE Plaza in 0.3 s with all 4,090 primitives streamed, back in 0.3 s with Lumi visible.
- **Native viewer.** The visual harness still pins Harbor's region in C++ (`llfinalversevisualbenchmark.cpp`). Use the World Map to teleport manually until that is generalized.
- **Rollback.**
  1. Stop the simulator and remove `Regions/OpenVCE.ini`.
  2. Restore `before-openvce-all.sql` (private `openvce-region-20261010/`), or delete the region's rows.
  3. Restart the `…-pre-openvce` container.
