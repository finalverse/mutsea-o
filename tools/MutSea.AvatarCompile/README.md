# MutSea.AvatarCompile

Offline compiler for Genesis AvatarPackages: a skinned glTF interchange becomes one native skinned LLMesh plus native PBR material assets and `compiled.json` (`AvatarCompiled`). Encodings follow the viewer decoders (`llvolume.cpp` submesh/weights, `llmodel.cpp` `LLMeshSkinInfo`, `llmeshrepository.h` header); see `docs/finalverse/art/genesis-lumi-validation.md` in the viewer repository.

```sh
dotnet build tools/MutSea.AvatarCompile -c Release -o /private/avatar-compile -p:MutSeaBin=/absolute/simulator/bin
DOTNET_ROLL_FORWARD=Major dotnet /private/avatar-compile/MutSea.AvatarCompile.dll \
  /absolute/avatar-package /absolute/viewer/indra/newview/character/avatar_skeleton.xml /absolute/NEW-output
python3 tools/MutSea.Content/verify_avatar_mesh.py /absolute/avatar-package /absolute/NEW-output
```

The skeleton must match the pinned SHA-256. The output directory must be new. Packages must be original CC0, non-selectable native `animated_object` or `attachment` representations bound to the native skeleton with translation-only rest joints, ≤110 joints, ≤4 normalised influences, four strictly reducing LODs and one PBR material per face. Animated objects are bounded to 20,000 high-LOD triangles; worn humanoids to 40,000, with no independent collision/motions and a generated native alpha wearable. Rejections never write `compiled.json`.


## Bounded avatar surfaces (compiled schema 3)

Schema 1 (factor-only/worn) and schema 2 (package motions) retain their previous byte serialization and pins. Optional source `definition.textures` entries carry `local_id`, native `path`, interchange PNG `source_path`, `width` and `height`; `material_textures` maps existing material IDs to local texture IDs. Every payload is listed and hash-pinned in `files`. The interchange glTF must name only these package-local PNGs and bind them to UV0. Extra images, external URLs and unresolved bindings are refused.

Schema 3 adds `textures[]` and optional face `base_color_texture`. Each texture is a raw JPEG2000 RGB8 codestream (one tile, zero offsets, no subsampling), at most 1024×1024 and 1 MiB; total native surfaces are at most 4 MiB, with 1–8 unique surfaces. The authoring PNG is bounded to 1 MiB, matching RGB8 dimensions and non-interlaced IHDR. This is metadata/hash validation for trusted offline original content, not a public arbitrary-codec upload service. Studio separately decodes the PNG/JPEG2000 and proves pixel-exact equality. No normal/ORM maps or arbitrary shader extensions are enabled.

Native texture UUIDs are derived from SHA-256 of `finalverse-avatar:texture:` plus the payload hash. The runtime stores `AssetType.Texture`, binds both the inherited PBR base-colour image UUID and TextureEntry fallback, and uses the existing wardrobe snapshot/replace policy. Skin, rig, LOD, alpha, inventory and animation contracts stay intact. Versions are assigned explicitly; older folders/items are never deleted or renamed.

The first original surface revision is Auren 0.2.0: woven slate cloth, brushed bronze and soft leather, 111,201 native bytes. It retains Auren 0.1.0 mesh/alpha bytes. Elara remains female; the provisional `elara-male` package stays retired with Auren named as successor. This increment proves delivery/persistence, not final Genesis texture or face acceptance. See the viewer's `docs/finalverse/art/genesis-surface-validation-20261012.md`.

Rejection coverage uses `AVATAR_SURFACE_PACKAGE` in addition to `AVATAR_PACKAGE`, `AVATAR_WORN_PACKAGE`, `AVATAR_COMPILE_DLL`, `VIEWER_SKELETON` and `DOTNET`:

```sh
python3 tools/MutSea.Content/test_avatar_compile.py
```
