# MutSea Harbor v1

A 256 × 256 metre island with a flat, walkable centre, an eastern lake and surrounding sea. Water is 20 m; the centre is 25 m. Landing is near (128,128,27).

The scene has 40 named, independently addressable primitives: welcome plaza and arrival marker, welcome sign, three paths, pavilion floor/four columns/roof, three benches and backs, creation pad/sign/cube/sphere, lake overlook/sign, and eight trees (trunk and crown). The welcome sign sits east of the north promenade to keep the path clear. Purple, silver, stone, wood and green use the inherited built-in white texture; no remote texture or third-party model is needed.

`tools/MutSea.StarterScene` constructs objects with inherited `PrimitiveBaseShape`/`SceneObjectGroup` and serializes them through `SceneObjectSerializer.SOGToXml2`. Before export, it reloads every object with `FromXml2Format`, checking count, identity, owner and nonphysical status. Terrain uses the real RAW32 format (256 × 256 little-endian floats). The manifest contains content hashes, owner identity, landing and explicit `ai_actions_enabled=false`.

IDs derive deterministically from the scene version and object names. The generator rejects zero owner UUIDs, nonempty destinations, duplicate IDs and out-of-region bounds. The development account owns all objects. Next-owner Copy/Modify/Transfer is set; no public edit permission is granted. Fixed creation provenance and content hashes make exports reviewable. Generate a new version for future edits rather than treating deterministic IDs as a live-world overwrite API.

This scene establishes a first place to enter, meet and practice creation. AI policy, transactions, semantic sidecars and WorldLine are later milestones. The server's normal permissions and imports remain authoritative. No scripts, commerce, voice service or cloud AI credentials are added.

The original Phase 0 sandbox remains intact. A copied prototype included its original user cube (41 objects total); the fresh Harbor fixture has exactly 40. Neither prototype XML nor private accounts/DBs are committed. Original scene-generation source is BSD-3-Clause; inherited serialization and default texture retain their own provenance.
