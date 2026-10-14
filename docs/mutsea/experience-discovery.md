# Authenticated experience discovery

`POST` to the native `FinalverseWorld` region capability with `{"op":"experience"}` returns eligibility for the current account/world. This is read-only metadata for consumer UI, not a new identity grant, provider health check or world executor. The existing native session/root-presence check applies; the server derives actor identity from the registered capability, ignoring a supplied `actor_id`.

The response is `{"ok":true,"result":...}` with:

| Result field | Contract |
|---|---|
| `schema_version` | Integer `1` |
| `region_id`, `actor_id` | Current native region and authenticated account UUIDs |
| `recovery_required` | Native WorldTemplate/transaction recovery fence |
| `compose_places` | Existing place composer is configured |
| `citizen.present` | Canonical Lumi scene representation exists and is not deleted |
| `citizen.can_interact` | Citizen module, representation and relay are ready for this configured controller |
| `citizen.state` | One of the states below |

States in precedence order: `not_enabled`, `not_in_world`, `not_controller`, `representation_recovery`, `starting`, `enabled`. A noncontroller may see a citizen without being entitled to command it. An enabled citizen can still encounter a later runtime/provider failure; normal request handling reports that failure. A world recovery fence is separate from citizen representation recovery, so authorized reading and human pause/cancel remain possible while native mutations are rejected.

The operation performs one canonical scene lookup and reads configured state. It does not invoke an AI service or expose owner identity, memories, bearer URLs, credential locations or provider secrets. Its own 100 ms per-actor throttle prevents background discovery from consuming the existing human action throttle. Every actual citizen/world operation retains the original server policy and revalidation.

Finalverse checks strict native types/schema/scope and refuses affirmative controls on missing, unsupported or contradictory metadata. It invalidates its UI cache when actor, region or capability changes, discards old replies, and polls at most once per 30 seconds with a five-second timeout. Basic native world inspection continues on older compatible World API servers. These hints are never accepted as execution authority.

## Verification — 2026-10-14

The module and native probe build with zero warnings/errors. The 13-check composer/discovery suite includes absent/disabled citizen, observer versus controller and startup/representation recovery cases. The viewer's nine-case presentation suite covers strict types, wrong account/region, missing/future metadata, contradictory presence and world recovery.

`tools/MutSea.ExperienceSmoke` accepts an owner-only bounded private case and a new receipt path. It restricts native login to the three designated loopback fixtures/regions, negotiates the custom capability through the inherited LLSD seed contract, checks authenticated actor/region, verifies a body-supplied actor cannot change identity, and verifies normal inspection succeeds immediately after discovery. Observer/absent cases additionally require the existing native citizen policy to refuse inspection. Responses and HTTP duration are bounded; normal logout is confirmed. It sends no avatar appearance, model request or world mutation. Invocation:

```sh
dotnet build tools/MutSea.ExperienceSmoke/MutSea.ExperienceSmoke.csproj \
  -c Release -p:ProbeBin=/absolute/path/to/native-sdk-libraries
dotnet tools/MutSea.ExperienceSmoke/bin/Release/net8.0/MutSea.ExperienceSmoke.dll \
  /private/owner-only-case.json /private/new-receipt.json
```

Private cases contain the known local `grid_url`, `region_id`, `user_id`, `first_name`, `last_name`, `password` and Boolean `expect_compose`, `expect_citizen_present`, `expect_citizen_interactive`. Never commit cases or raw SDK logs. Set `DOTNET_ROLL_FORWARD=Major` only when the test host has a newer runtime and the native SDK compatibility is already established.

Accepted native sessions: Harbor controller (`enabled`), Harbor newcomer (`not_controller`) and original island newcomer (`not_enabled`). All verify authenticated scope, ignored body identity, immediate inspection and confirmed logout; both noninteractive cases verify citizen denial. The SDK's initial unnegotiated-capability attempt is preserved privately and excluded from acceptance.

Three existing local simulators received clean native backup/shutdown, verified old-module/database/configuration/journal backups, new-module integration and restart. Before probes, their complete prim rows (5,635/1,472/498), inventory item rows, stored appearance rows and sidecar bytes were identical. OAR fixtures were paused for namespace guard installation before client access; denied outbound access was verified. No image/PostgreSQL instance or public deployment was added. Module SHA-256: `24412b14cd3216ee198863bb03b8e20c331dbe9f85f18a163f2a4ff2e931d96e`.

One packaged macOS UI journey verified the absent-citizen island: four-button HUD, first-creation card, nook/garden entry points, clear-ground guidance, actual Nearby object results, disabled Lumi checkbox and a prefilled nook awaiting user action. Previous native grounding/build/WorldLine/exact Undo evidence remains in [island acceptance](original-island-acceptance.md). Phase 4B's final art, public onboarding/choices, user study, collision/LOD, controlled performance and distribution gates remain open.
