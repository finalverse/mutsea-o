# MutSea development protocol smoke test

This probe logs into an explicitly configured development instance through HTTP or HTTPS, receives the real UDP scene and requests object names. It checks every starter-scene ID, name, position and scale, the region identity and the advertised capability origin, then verifies logout. Account secrets are read only from a private JSON file and never put in command arguments or emitted in the receipt. Use only test accounts/worlds. Run in the same network namespace as a fixture advertising loopback, or against a reachable configured test grid.

The inherited OpenMetaverse binary cannot start its client UDP socket on .NET 8 Unix: it catches `SocketException` around Windows `SIO_UDP_CONNRESET`, while .NET throws `PlatformNotSupportedException`. The simulator uses its own UDP implementation and is unaffected. The probe therefore builds a **separate test SDK**, pinned to [opensim/libopenmetaverse f3ee229](https://github.com/opensim/libopenmetaverse/tree/f3ee229dc2a17e0dfd7256f8f634afe8e7610487). A narrow catch filter accepts that unsupported optimization. The idle texture pipeline also exits cooperatively instead of calling the unsupported `Thread.Abort`. No simulator binaries are replaced. The checkout retains all upstream licenses and copyright; the source patch does not alter protocol behavior or certificate verification.

With Git, Python 3, .NET 8 SDK and libgdiplus installed:

```sh
python3 tools/MutSea.ProtocolSmoke/build.py --workspace /private/new-protocol-build
dotnet tools/MutSea.ProtocolSmoke/bin/Release/net8.0/MutSea.ProtocolSmoke.dll /private/account.json /private/scene
```

The explicit ConfigurationManager package supplies the inherited logger's runtime dependency. The tool itself should build without warnings; the upstream SDK currently emits its own 77 warnings. Its native/image functionality is not exercised by this smoke test. This probe is not an agent executor or production bot runtime.
