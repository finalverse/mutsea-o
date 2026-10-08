# MutSea appearance smoke probe

This read-only development probe uses a **different** ordinary account to observe a viewer already logged into one loopback grid. It waits for that avatar's actual UDP appearance, then downloads head, upper and lower baked textures through the SDK's negotiated GetTexture capability and verifies five-channel JPEG2000 RGBHM codestreams. It does not change the target's appearance, create inventory, or substitute a mock texture.

Build against the compatible protocol SDK used by `MutSea.ProtocolSmoke` (see that tool's build script for the pinned SDK and macOS socket/thread compatibility patches):

```sh
dotnet build tools/MutSea.AppearanceSmoke/MutSea.AppearanceSmoke.csproj \
  -p:MutSeaBin=/absolute/path/to/protocol-sdk
DOTNET_ROLL_FORWARD=Major dotnet \
  tools/MutSea.AppearanceSmoke/bin/Debug/net8.0/MutSea.AppearanceSmoke.dll \
  /private/observer-account.json /private/target-account.json /private/receipt.json
```

Account files use `grid_url`, `user_id`, `first_name`, `last_name`, `password`. The two accounts must differ and use the same loopback grid. Do not place credentials in command arguments or commit account files. A receipt records time, visual parameter count, download lengths/channel counts and SHA-256 hashes; it contains no password or capability URL. Exceptions may contain capability bearer URLs, so diagnostics are saved privately rather than printed. Receipts/diagnostics are mode 0600 on Unix. The observer logs out in `finally`.

This verifies protocol distribution and downloadable bakes. It does not prove remote viewer rendering, template provisioning completeness or permanent bake-cache storage. Use actual viewer edit/save/relogin and server restart tests for persisted wearable acceptance.
