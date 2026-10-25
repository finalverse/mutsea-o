# MutSea geographic world binding

`mutsea.geographic-binding/v1` is MutSea's authoritative, versioned statement of where one
live region sits on the Earth. It is the native producer that Earth Core's
`earth.world-binding/v1` proposal asked for.

## Authority

| Concern | Owner | In this contract |
|---|---|---|
| Published geographic scene (terrain, buildings, roads, frames) | SpatialDB | Pinned by graph, version, root SHA-256, frame and geography evidence hash. Never copied into live objects. |
| Live region, objects, avatars, sessions, ground | MutSea | The binding, its journal, the live heightmap and all object state. |
| Navigation, camera, map | Earth | Consumes `earth.world-binding/v1`, `earth.world-entry/v1` and `earth.navigation/v1` projections. |

The binding derives one simulation representation from the publication: the region heightmap,
because avatars need ground to stand on. Its provenance and residuals are recorded. Buildings
and roads stay SpatialDB representations. MutSea indexes them as read-only correspondence and
never instantiates them as objects.

## Transform

All arithmetic is IEEE f64 on WGS84 (a = 6378137, f = 1/298.257223563). The formulas mirror
SpatialDB's `spatialdb-types/src/frames.rs` term for term.

```text
ECEF(p) = ECEF(anchor) + ENU(anchor)ᵀ · scale · R(heading) · p
```

- `p` is a region-local position in metres. +X and +Y are horizontal at the anchor; +Z is up.
- `anchor` is the WGS84 position (longitude, latitude, ellipsoidal height) of region-local (0, 0, 0).
- `heading_degrees` is the clockwise angle from true north *at the anchor* to region +Y. Region +X lies 90° clockwise of +Y. The range is [0, 360).
- `scale` is Earth metres per region metre.
- Region +Z is height above the anchor's tangent plane. It is offset so that z equals CGVD2013 orthometric height at the publication origin. Within the bound district z differs from orthometric height by at most `vertical.max_deviation_metres` (from curvature, tangent tilt and geoid slope).

`transform.region_to_ecef` is the same transform as a column-major 4×4. `region_to_publication`
maps region-local to the pinned SpatialDB frame (+x east, +y up, +z south). Both are checked
on load against anchor, heading and scale, so a consumer may use either directly.

## Identity and versions

- `world_id`: explicit configured UUID, never derived from a name.
- `region_id`: the native region UUID.
- `binding_id`: UUIDv5-style SHA-256 of (schema, world, region). Stable across every revision.
- `version`: SHA-256 over the RFC 8785 canonical JSON of every member except `version`. Any JCS implementation reproduces it; in JavaScript, sorted keys plus `JSON.stringify` is enough. See `tools/geo/core-conformance.mjs`.
- `revision` and `previous_version` form a chain. A revision must directly supersede the active version. Retiring pops back to the predecessor. Every issued version stays content-addressed in `<DataDirectory>/<region>.geo/`.

## Issuance (operator only)

```ini
[FinalverseGeography]
Enabled = true
WorldId = <world UUID>
Candidates = /config/geo              ; <version>.binding.json, .correspondence.json, .terrain.r32
Issue = <region UUID>@<version>       ; or, alone: Retire = <region UUID>@<version>
LoginURI = http://127.0.0.1:19070/
```

On region load the candidate is checked before anything changes:

- full structural and numeric validation against the live region;
- the binding version recomputed from its content;
- the correspondence and terrain hashes matched to their pins;
- the terrain range checked;
- the world ID and revision chain checked.

The journal (`<region>.geobinding.jsonl`) then records intent, a terrain and water snapshot, the
postcondition, and the outcome. A failed application is compensated; a failed compensation
fences further issuance as `recovery_required`.

Two constraints protect live state:

- New ground replaces old only in a region without live objects.
- Retirement restores the pre-issue ground only if terrain is unchanged since issuance.

No client operation can issue, revise or retire a binding.

`tools/Finalverse.GeoBind` derives candidates from a local copy of an immutable publication.
It hash-verifies every root, subtree, geography and mesh byte, then:

- solves the anchor and heading so the publication origin sits at the region centre and region +Y follows the published terrain grid north;
- resamples the heightmap by intersecting every 1 m region vertical with the finest published terrain triangles;
- rounds heights to MutSea's persisted 1 mm (a fixed point, so pinned bytes survive restarts);
- builds the correspondence index.

`tools/geo/geo_oracle.py` recomputes everything independently, using SpatialDB's own pyproj
normalization and source rasters.

## Discovery (public, read-only)

Discovery responses carry no identity, objects or capability. They allow CORS and say
`Cache-Control: no-cache`. The rate is bounded and answers 429 when busy. Non-GET answers 405.

| Request | Answer |
|---|---|
| `GET /finalverse/geo/v1/worlds` | Each bound region: `world_binding` (earth.world-binding/v1), `binding_id`, `binding_version`, `world_entry` (earth.world-entry/v1), `login` (login URI plus native start location), WGS84 `coverage` corners, the publication pin, `region_to_publication`, attribution, and whether live terrain still matches issuance. |
| `GET /finalverse/geo/v1/entry?latitude=&longitude=` | The region covering that point, or 404. `target` gives region-local ground position, WGS84, the published features underfoot, `world_entry` and a native `login.start`. |
| `GET /finalverse/geo/v1/entry?spatialdb_entity=<uuid>` | For a published building, road or terrain tile of a bound region: a dry standing place beside it (on an adjacent street when there is one, never inside a building) and the same `target` block. |

## Session-bound World API (FinalverseWorld capability, read-only)

| `op` | Fields | Result |
|---|---|---|
| `geo_binding` | — | The issued binding, its Earth projections, `terrain_matches_issued`, `recovery_required` and history. |
| `geo_locate` | `entity_id`? | For the caller's avatar or a live object: region-local, WGS84, publication-frame position, ground, published features underfoot, and an `earth.navigation/v1` `return_destination`. |
| `geo_resolve` | `latitude`,`longitude` or `spatialdb_entity` | As discovery `target`, inside the session. |
| `geo_entity` | `entity_id` | A live object resolves to where it stands and what it stands on. A published feature resolves to its region footprint, a standing place and the live objects on it. |
| `geo_enter` | `binding_version` and a target | An arrival receipt (`arrived` within 3 m, otherwise `elsewhere`), journaled per actor (60 an hour). A changed binding version is refused. |

Every mutation still goes through `prepare`/`commit`/`undo`, WorldKernel policy and WorldLine.
Geography never relaxes ownership, parcel or session checks.

## Earth handoff (authorization-preserving)

1. Earth discovers a world by geographic point or SpatialDB entity. It learns only where the world is, never who is in it.
2. The user signs in natively with their own credentials. The login `start` location is the discovered `uri:<region>&x&y&z`. The entry descriptor says `authorization: "required"` and carries no credential.
3. In the region, the client calls `geo_enter` with the discovered `binding_version`. MutSea checks the version and compares the native avatar position with the target. The receipt is evidence, not authority.
4. Capabilities are per native session. A logout revokes them, and a reconnect yields a new one with the same identity.
5. To return to Earth, `geo_locate` gives a validated `earth.navigation/v1` geographic destination.

## Consuming the binding

CesiumJS, with no convention to re-derive:

```js
// `world` is one entry of GET /finalverse/geo/v1/worlds; the matrix is column-major.
const regionToEcef = Cesium.Matrix4.fromArray(world.region_to_ecef);
const ecef = Cesium.Matrix4.multiplyByPoint(regionToEcef, new Cesium.Cartesian3(x, y, z), new Cesium.Cartesian3());
```

Or from the Earth Core projection alone:
`Transforms.eastNorthUpToFixedFrame(anchor)` × `Rz(−heading)` × `scale`.

FinalStorm: `region_to_publication` (column-major) places native Z-up region positions in the
publication's east-up-south frame, replacing the operator-supplied `--world-origin`
translation. The Old Montréal region is rotated 19.3155° from true east-north, so a translation
alone misplaces an object by 0.336 × its distance from the region centre: 86 m at an edge
midpoint and 121 m at a corner.
