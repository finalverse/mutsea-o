# Native content interaction acceptance

This operator test logs into the designated loopback Harbor fixture, checks the receipt actor, actual streamed region/IDs/root positions, and sends inherited avatar walking controls. Credentials remain in the private account file. It does not create content, mutate permissions, expose a capability URL, or accept arbitrary grids. Teleports position each test at a clear starting point; walking, not teleportation, crosses the test path.

```sh
DOTNET_ROLL_FORWARD=Major dotnet build tools/MutSea.InteractionSmoke \
  -p:ProbeBin=/absolute/portable-probe-sdk -o /absolute/new-probe-build
DOTNET_ROLL_FORWARD=Major dotnet /absolute/new-probe-build/MutSea.InteractionSmoke.dll \
  /private/account.json /private/bench-receipt.json /private/tree-receipt.json \
  /private/new-interaction-evidence.json
```

Use the Unix-compatible probe SDK separately from the simulator assemblies, as documented for `MutSea.ProtocolSmoke`. Quit the fixture viewer before logging the same test account into this probe. Output evidence is private and never overwrites a prior run. A failed attempt preserves its bounded coordinates/observations too.

Four two-second walks compare an empty lane with the bench back, then foliage clearance with the tree shaft. Samples record actual native avatar positions. Low furniture may be climbed by the inherited controller: the bench test requires obstruction or vertical clearance above the backrest, and refuses a straight passage at terrain height. The tree test requires its trunk to block, while an offset path through the visual canopy must remain open. This is a narrow fixture test, not a general physics certification or a sitting-pose/continuous-LOD test.

On 2026-10-09 `basicphysics`/`ZeroMesher` failed collision behavior. Enabling inherited ubODE gave contact response but the shaft trial climbed unexpectedly. The inherited ARM64 BulletSim + Meshmerizer trial passed all four walks, including a trunk stop near y=119.53 for the tree at (160,120,25). Only the isolated local fixture now uses that backend. Remote ubODE and general deployment defaults have not been switched; investigate the ubODE behavior before changing production physics.
