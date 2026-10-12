# Source-matched incremental native build

Build the SQLite and PostgreSQL providers, UserAccountService and LLLoginService against an already compiled **matching** MutSea runtime directory. References and embedded migration resources come from the repository's `prebuild.xml`; missing assemblies or an existing output directory fail. No source build directories are rewritten.

```sh
python3 tools/MutSea.ComponentBuild/build.py \
  --mutsea-bin /absolute/path/to/matching/native-bin \
  --output /private/new-component-build
```

Requires a .NET SDK capable of building net8.0. Copy the resulting assemblies into a separate development image/container using the same baseline, preserving the prior image/runtime for rollback. This is a development iteration tool, not a full server build or distribution artifact. Run the normal standalone/worldops Docker builds before releasing a complete image.

`tools/MutSea.ProvisioningTests` uses the resulting assemblies to exercise actual native provisioning and schema-cache code. `tools/MutSea.PgSchema` uses the PostgreSQL assembly's embedded migrations.
