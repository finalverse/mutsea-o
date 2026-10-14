# Operator-approved pilot admission

This is a bounded provisioning foundation, verified on the single-host backend on 2026-10-15. It uses MutSea's inherited identity, inventory, appearance and home services. It is not public web registration or an invitation redemption endpoint. No model can create an identity, choose arbitrary package paths, set privileges or activate an account.

## Stage a selection

`deploy/standalone/admit-pilot.py` reads two operator-owned private inputs: `avatar-choices.json` in the native configuration directory and a request file outside Git. The existing `accounts/newcomers.json` must be a regular owner-only file; an empty initial batch is `{"accounts":[]}`. The native configuration must already enable `[FinalverseAccounts]` for the provisioned host region and its dry arrival.

The request has exactly six fields:

| Field | Contract |
| --- | --- |
| `admission_id` | Fresh nonzero canonical lowercase UUID; correlation, not an authentication token |
| `first_name`, `last_name` | Each 1–31 ASCII letters/digits, beginning with a letter |
| `password` | Unique generated private password, 12–128 printable characters; never in command arguments or Git |
| `avatar` | Exact ID in the operator's pinned supported choices |
| `experience` | `harbor`, the only currently provisioned experience |

Each server-controlled choice contains `id`, `directory`, `package_id`, `version` and `compiled_sha256`. Its directory must be `/config/genesis/<id>` and its compiled package must match the pinned identity/version/hash and native wearable attachment representation. The native pipeline additionally verifies all mesh/material/alpha payloads, canonical template and owned appearance contracts before creating an identity. Lumi's animated citizen representation is not a supported wearable; the retired provisional Elara Male template is rejected. Existing Genesis identifiers and previously provisioned inventories are unchanged.

Run the trusted operator tool, using private paths only:

```sh
python3 deploy/standalone/admit-pilot.py \
  --config /private/backend/config \
  --request /private/backend/pilot-request.json
```

Success reports `staged_for_native_provisioning` and the correlation UUID. It does **not** mean the account exists or can log in. The tool sends no message and starts no service. Same exact request replay returns `already_staged` without rewriting the file. Changes to an already staged name, password or admission are refused. Unsupported experience/avatar, modified package pin, duplicate JSON keys, linked/overly readable files, unknown privilege fields and concurrent staging are refused without changing the native file. A nonblocking file lock and atomic owner-only file replacement prevent lost updates. The tool is for Linux/macOS operators, not a mobile/client registration UI.

## Native activation and replay

At a reviewed native startup, the world must be ready. The existing native pipeline preflights the pinned package, creates an inactive identity, stores and reads back its selected home, provisions and verifies its owned wardrobe/alpha/Current Outfit, verifies the inherited core appearance, then activates it. See [selected appearance before activation](newcomer-activation.md). Account, home and activation records carry the admission UUID and a SHA-256 selection fingerprint; passwords are never journaled. There is no new identity database.

An existing name may be replayed only when that native identity is active and its append-only ledger has a matching `account_activated` record for this admission, account UUID and exact selection. Confirmed replay performs no password, wardrobe or home write. Changing the region, arrival, character path or compiled pin requires explicit operator review, not implicit replay/migration. Legacy newcomer entries without admission IDs retain their earlier provisioning behavior.

If the ledger is malformed, an activation is uncertain or the account remains inactive, replay fails closed. Pending/failed accounts and their inventory remain available for operator diagnosis. Do not delete identities, reset passwords, erase journals or auto-retry a failed admission. An activation write followed by failed journal durability can leave an active identity requiring reconciliation; this is not an atomic database/file transaction.

The native batch remains bounded to 16 records. During reviewed maintenance, archive completed private input files and retain the append-only activation ledger. Never hand completed identity data to a model or repurpose a former admission to rename an account. Credential delivery and account recovery still need a separate authenticated product flow.

## Actual verification

Twelve operator-staging tests and 17 native parser/selection/replay checks passed. Existing 21 native account-service provisioning/concurrency checks also passed. A fresh architect pilot was created through this staged selection, observed pending/home/wardrobe then active, and logged into its selected dry Harbor home. The native probe confirmed all 422 scene parts, three semantic contexts, eight owned Current Outfit links and normal logout. A clean native restart preserved every scene, appearance, inventory and account row plus all journals; the admission was not executed twice. A separate PostgreSQL restore matched all 31 native tables.

```sh
python3 deploy/standalone/test_admit_pilot.py
dotnet run --project tools/MutSea.AdmissionTests -c Release \
  -p:MutSeaBin=/absolute/path/to/matching/native/bin
```

These checks establish operator provisioning and preservation. Public invitation/signup UI, rate limits, recovery, real first-use research, external native world entry and distribution remain product work. Admission UUIDs must never be presented as one-time bearer invitations.
