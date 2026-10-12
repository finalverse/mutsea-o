# Offline Starter Library mesh compiler

Validate an original asset package with `tools/MutSea.Content/validate.py` first. Build this tool against the source-matched native SDK (`-p:MutSeaBin=/absolute/native-bin`), then pass the validated package directory and a new output directory. Existing outputs are preserved.

The adapter uses inherited `ColladaLoader`/LLMesh serialization, preserving source hashes and local entity IDs. It supports bounded, untextured, unrigged static assets. Compiled format v2 includes semantic tags, affordances and relationships. `ContentCatalog` revalidates identity, semantics, budgets and mesh hashes before a server operator can instantiate an approved package. Compilation is not runtime, visual, collision or performance acceptance.

Historical packages without `definition.lods` share the high geometry across four native LOD references. New packages can pin independently reduced medium/low/lowest Collada payloads and actual triangle counts. Their quantization bounds match the high mesh. Native tests decode the actual blocks; live transitions remain a separate acceptance gate.

The current compiled v3 contract supports bounded factor-only native PBR assets, an explicit single-seat target, a normalized box or no-collision child, and at most two small native point lights. The light's RGB is linear, with intensity/radius/falloff bounded and checked against the original manifest. See [content import](../../docs/mutsea/content-import.md) for limits, native persistence and Undo evidence. Texture uploads, arbitrary hulls, projectors, place-scale packages and rigged avatars remain outside this adapter. Do not enlarge the small-asset limits to fit a pavilion without a reviewed placement/collision contract.
