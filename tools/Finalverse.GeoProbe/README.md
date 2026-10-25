# Geographic native protocol probe

Build this authored .NET 8 project after building MutSea's protocol dependencies:

```sh
dotnet build tools/Finalverse.GeoProbe/Finalverse.GeoProbe.csproj -c Release
```

`MutSeaBin` defaults to the repository's `bin` directory. Override it with `-p:MutSeaBin=/absolute/path/to/a/compatible/protocol-sdk` when using an independently built SDK, including the portable SDK used by the macOS acceptance run. The directory must contain the five assemblies referenced in the project; no dependency binaries or machine paths are committed here.

The executable takes a private configuration file as documented by its usage and the geographic integration report. Passwords and session capabilities must remain outside Git and command arguments. This is an authenticated integration probe, not a replacement world-login or authorization implementation.

The related `Finalverse.GeoTests` project runs the deterministic unit/contract suite without a world login. Its four real-dataset cases require the explicitly supplied candidate, publication and oracle arguments. `Finalverse.GeoBind` builds the reviewed geographic binding candidate with the same shared geometry/frame code.
