# World Library operator procedure

Use a private run directory outside Git. Archives, credentials, database dumps, private native INI files and user data must never be committed.

## Acquire and inspect

```sh
python3 tools/MutSea.Library/ingest.py deploy/library/sources.json /absolute/NEW-private-run --budget-mib 1024
python3 -m unittest discover -s tools/MutSea.Library
python3 -m unittest discover -s tools/MutSea.Oar
DOTNET_ROLL_FORWARD=Major dotnet run --project tools/MutSea.LibraryTests -c Release
```

The batch exits nonzero if any selected entry is rejected. Inspect `run.json`, each receipt and its hash. It preserves an earlier run. A successful batch is **not** a production admission.

Build `tools/MutSea.OarAudit` against the exact tested native assemblies using `-p:MutSeaBin=/absolute/native-assemblies`. Run the DLL with `<passive-directory> <NEW-audit.json>`. Where the known blank-texture collision exists, run `<passive-directory> <NEW-rekey-directory> --rekey-white`; quarantine `rekeyed.oar` into another new directory and audit the final passive bytes. Retain the entire provenance chain. Never use `--force-assets` to suppress a collision.

## Admit a new region

1. Confirm source terms and contributor notes; retain credits and license evidence. Current automatic copy policy is CC0 only. Hold unknown/mixed/NC/ND content rather than relabeling it.
2. Review native dependency closure, terrain/spawn, part count, ownership, performance budget and visual suitability. Keep scripts and task inventory disabled.
3. Allocate a new region UUID, non-overlapping grid position and unique UDP port. Verify router forwarding, host firewall and container publishing for that exact port. Do not broadly expose console, PostgreSQL, private AI, remote admin or internal provisioning services.
4. Take a private consistent PostgreSQL backup, configuration backup and world-journal checkpoint. Record active users and the exact rollback image/compose configuration.
5. Stage with a **separate** PostgreSQL database and private estate identity, no production volumes, no public ports and an enforced egress guard. Docker's internal-network flag alone is insufficient proof of isolation.
6. Put the final `staging.oar`, pinned `report.json` and provenance under the operator catalog's sibling `imports/<archive-id>/` directory. The destination's `archive` carries `id`, `sha256`, `report_sha256`, `source_sha256` and `expected_parts` (maximum 10,000). Do not also specify an original `template` for that region.
7. With both `[YEngine] Enabled=false` and `[XEngine] Enabled=false`, the native per-region adapter imports **only an empty region**. Check its durable receipt, count, ownership and parcel policy. A pending/failed receipt requires operator recovery; deleting that marker and retrying over partial content is unsafe.
8. Prove authenticated discovery, native teleport/return, object streaming, interactions, copy permissions and restart persistence. Inspect a combined visual review and record unresolved visual/collision/load gates honestly.
9. Deploy only the passed new regions with a rollback image and explicit port rules. Preserve existing accounts, inventories, Harbor, journals and certificates. Recheck externally through the public grid; a loopback test does not prove public UDP.

For original Confluence, use the Studio-generated place/world manifests and the existing `WorldTemplateKernel`; no OAR path is needed. Pin citizen/account workers to their intended host region before enabling any additional regions.

## Recovery and release

An OAR native restore is not atomic. Keep its private pre-import database snapshot. Retire only newly allocated region configuration and restore its scoped data from the snapshot; do not reimport over Harbor, delete unrelated user work or erase shared assets indiscriminately.

Creator publication is a separate lifecycle: private immutable recipe → explicit free CC0 publication → permission-checked remix → separate commit. Unpublication removes discovery, not an already granted license. Priced offers cannot accept payment in v0.1. Do not advertise a functioning paid marketplace until an actual payment/payout/entitlement/refund path is implemented and verified.
