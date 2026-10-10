# Native OAR delivery smoke test

Read-only .NET probe using the existing OpenMetaverse client assemblies. It logs into **only** the registered loopback fixture on port 18095, verifies the actual region UUID and capability origin, receives every mapped part, checks positions/scales, fetches the five largest referenced JP2 samples through native `GetTexture`, verifies their hashes and logs out normally. It disables appearance sending. It performs no restoration or scene edits.

```sh
dotnet build tools/MutSea.OarSmoke/MutSea.OarSmoke.csproj -c Release \
  -p:ProbeBin=/absolute/existing-native-client-assemblies \
  -o /absolute/probe-output
dotnet /absolute/probe-output/MutSea.OarSmoke.dll \
  /absolute/private-staging-account.json /absolute/runtime-report.json \
  /absolute/NEW-receipt.json
```

The account is an owned private fixture file containing `first_name`, `last_name`, `password`, `registered=true`, `user_id`, `region_id` and `grid_url=http://127.0.0.1:18095/`. Keep it and raw client logs outside Git; never put the password in arguments. The report must be the reconciled runtime report from `MutSea.Oar/runtime_report.py`, with matching instance ownership. Account/report/texture sizes are bounded. Receipts omit passwords and capability URLs. Receipt paths must be new; failures print only stage and exception type.

The project targets .NET 8. This Mac has only .NET 10.0.3 installed; the verified command uses `DOTNET_ROLL_FORWARD=Major` explicitly. Do not interpret a host-runtime launch failure as a network or scene failure. The simulator remains on its retained .NET 8 image.

Successful streaming proves this passive scene's delivery. It does not prove Genesis rigging, every texture's visual decoding, mesh/PBR OAR import, IAR inventory compatibility, script safety of arbitrary assets or production WorldTemplate provisioning. [Results](../../docs/mutsea/oar-compatibility.md).
