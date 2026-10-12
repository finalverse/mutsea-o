# Required native avatar provisioning checks

These tests call the actual compiled UserAccountService `CreateUser` path with explicit fault-injected native service interfaces. They cover successful activation, rejected auth/inventory writes, missing/rejected appearance/assets, invalid ownership/bindings/COF/root, activation failure and exceptions. Failed provisioning remains inactive for operator repair; the test does not claim atomic cross-service rollback or public registration readiness. Legacy `CreateDefaultAvatarEntries=false` behavior remains covered.

Four deterministic concurrent-reader checks invoke the actual SQLite/PostgreSQL generic/authentication schema-cache methods. A blocking reader exposes the old early-publication race; mocks here exercise schema discovery, not a database engine. Real concurrent login and live provisioning are separate runtime checks.

```sh
dotnet build tools/MutSea.ProvisioningTests/MutSea.ProvisioningTests.csproj -c Release \
  -p:MutSeaBin=/absolute/path/to/matching/native-bin \
  -p:MutSeaComponents=/private/component-build \
  -o /private/provisioning-tests
DOTNET_ROLL_FORWARD=Major dotnet /private/provisioning-tests/MutSea.ProvisioningTests.dll
```

The verified development suite has 18 checks. `DOTNET_ROLL_FORWARD=Major` is needed on the development Mac with .NET 10 but no .NET 8 host runtime; native simulator acceptance runs .NET 8 in Docker.
