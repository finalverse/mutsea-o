# MutSea.AvatarCompile

Offline compiler for Genesis AvatarPackages: a skinned glTF interchange becomes one native skinned LLMesh plus factor-PBR material assets and `compiled.json` (`AvatarCompiled`). Encodings follow the viewer decoders (`llvolume.cpp` submesh/weights, `llmodel.cpp` `LLMeshSkinInfo`, `llmeshrepository.h` header); see `docs/finalverse/art/genesis-lumi-validation.md` in the viewer repository.

```sh
dotnet build tools/MutSea.AvatarCompile -c Release -o /private/avatar-compile -p:MutSeaBin=/absolute/simulator/bin
DOTNET_ROLL_FORWARD=Major dotnet /private/avatar-compile/MutSea.AvatarCompile.dll \
  /absolute/avatar-package /absolute/viewer/indra/newview/character/avatar_skeleton.xml /absolute/NEW-output
python3 tools/MutSea.Content/verify_avatar_mesh.py /absolute/avatar-package /absolute/NEW-output
```

The skeleton must match the pinned SHA-256. The output directory must be new. Packages must be original CC0, non-selectable `animated_object` representations bound to the native skeleton with translation-only rest joints, ≤110 joints, ≤4 normalised influences, four strictly reducing LODs and one PBR material per face. Rejections never write `compiled.json`.
