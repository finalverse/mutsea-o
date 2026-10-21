# Earth–World Bridge · FSIP 0.1

This is an **opt-in local v0.6 integration preview**, not an enabled public
service. The nine Genesis templates, existing Harbor deployment, native viewer
protocol and renderer are unchanged. Earth remains an independent application.

`EarthBridge.cs` adapts a reviewed Earth experience to the existing
`ComposerBridge`, `PlaceKernel` and approved `ContentCatalog`. The model selects
bounded design choices; it receives no simulator capability, database access or
arbitrary code execution. The bridge never writes MutSea's database directly.

## Authority and configuration

Use the operator guide in the `finalverse/finalverse-earth` repository:
`docs/earth/bridge-v06/developer-guide.md`. It documents both additive Earth
migrations, separate service credentials, private Unix transport, exact grid and
region UUIDs, asset pins, CLI handoffs and rollback. The narrow development-only
TCP transport must not be published through a reverse proxy.

The authenticated `FinalverseWorld` capability handles `op: earth_bridge` with
`preview`, `commit`, `reject`, `inspect` and `undo`. Its actual root avatar/session
provides the actor; request bodies cannot impersonate another user. Network I/O
runs outside the world gate. The native adapter independently validates the FSIP
profile, pinned assets, terrain, collision clearance and parcel permissions.

Preview consumes a five-minute one-use handoff, then creates a two-minute pending
plan bound to that native login. Only explicit approval can authorize creation.
Generic commit/Undo routes cannot bypass this plan's bridge checks. No automatic
POST retry is used. Source-linked exhibition layouts are schematic, not implicit
geographic reconstructions; virtual layouts require no Earth dataset.

## Persistence and recovery

Native WorldLine remains the mutation authority. A private regional
`.fsip.jsonl` sidecar retains the manifest and bindings between source records and
actual native object UUIDs. An index adds read-only semantic context without
changing fingerprinted geometry. New admission reserves bounded journal space
for execution outcomes and later Undo.

After an uncertain response, **inspect the saved native plan ID**. Inspection
reconciles a proven native commit and intact fingerprints; it never repeats
creation. Incomplete native recovery remains blocked. Fingerprint-guarded Undo
uses the existing kernel and does not require a model or running Earth service.
Disabling new handoffs retains provenance, generic-route guards and owner Undo.
Receipts retain the original manifest's audience while the bridge is disabled.
Pending receipt delivery resumes on a later explicit inspection.

Generic Creator Library export is refused for FSIP places until that format can
retain separate source-data licensing and WorldBindings. This restriction does
not change existing non-FSIP recipes.

## Lumi and validation

`CitizenBridge` adds one controller-scoped, read-only `review_experience` tool.
It passes a bounded source/generated summary to the **existing** agent runtime;
it creates no second Lumi, autonomous goal or world-write grant. This does not
make Lumi migrate between regions. Hosted acceptance is a separate gate.

Run `tools/MutSea.ContentTests` and `tools/Finalverse.WorldTests`. The explicit
`tools/MutSea.EarthBridgeSmoke` diagnostic uses an isolated, operator-provisioned
local grid, private account/command files and a separate native observer. It
checks actual capability calls, duplicate and generic-commit denials, semantic
bindings, native UDP object delivery and approved mesh/material delivery. The optional material pin file comes from
the committed placement packages (deterministic native IDs and approved asset
SHA-256); the probe verifies both bytes and the native glTF wrapper through a
fresh ViewerAsset capability after restart. It does not
read the world database. See its source for the bounded development fixture.

The cross-repository acceptance ledger is
`finalverse-earth/docs/earth/bridge-v06/acceptance.md`. Unit fixtures alone do not
prove native visual, hosted Lumi, Harbor or production performance acceptance.
