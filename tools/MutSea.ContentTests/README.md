# Content kernel tests

Run `dotnet run --project tools/MutSea.ContentTests -c Release`. These 38 tests (including the PlaceTemplate kernel) exercise the real kernel/catalog and an explicit world fault fixture: native-policy revalidation, authenticated sessions, semantic integrity, canonical relationship IDs, durability, interrupted/partial imports, failed state observation, recovery fences and guarded Undo. They do not prove native physics, rendering or a live PostgreSQL deployment.
