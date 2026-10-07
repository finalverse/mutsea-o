# Upstream synchronization — 2026-10-08

The product integration branch is `main`. `master` remains the reviewed upstream-tracking branch. The GitHub default branch is still `master`; select `main` when building the Finalverse world-operations integration.

## Source and merge order

| Reference | Commit |
|---|---|
| Starting Finalverse GitHub `master` | `13c546ce413e49e957e387547d97e79800cf33ca` |
| Direct parent `qwy16/mutsea`, `master` | `2c5c18e0947096b81a530a428169f5238fd579ce` |
| Original `opensim/opensim`, `master` | `2c5c18e0947096b81a530a428169f5238fd579ce` |
| Starting local world-operations branch | `ffab4ba878f75f9595691a843e95ad9c0ce4d7cc` |
| Upstream merge, before local work | `de89a0a8e0` |
| Local world-operations merge | `c508950985` |
| Reviewed upstream-tracking head | `f267bbf51b9928b2003c451d65648e1da8da32a9` |

GitHub fork metadata identifies `qwy16/mutsea` as the direct parent. Its fetched head also equals the original OpenSim head. Upstream had 129 commits absent from the fork; the fork had one commit absent from upstream. The upstream merge retains both histories and precedes the five local world-operations/deployment commits. No upstream commit was replaced or squashed.

The upstream changes affect 171 files, notably WebRTC/Janus voice, profiles, groups, permissions, networking, inventory and authentication. Git recognized most of the fork's existing `OpenSim/` → `MutSea/` renames. Seventeen conflicts were reviewed individually: updated implementation was retained with the existing MutSea namespace, assembly names and module extension paths. `Culture.cs` required explicit rename handling because of its inherited bare-CR line separators. New Janus and Concierge references were adapted to the actual MutSea interfaces.

The subsequent local merge had one conflict in certificate loading. The established .NET hostname-validation implementation was retained instead of localized SAN display-text parsing. The separate Finalverse module still wraps inherited scene, permission and backup APIs. MutSea remains the server brand; AI execution stays opt-in and policy-validated. Upstream does not gain a model-to-scene mutation bypass.

## Verification

The merged runtime was rebuilt using the pinned .NET 8 ARM64 Docker recipe; it completed with zero errors and four inherited `ref readonly` warnings. The add-on was built against those same runtime assemblies, with 40 kernel tests passing. The TLS probe passed 12 hostname checks and mismatched-certificate startup rejection. The starter-scene generator built without warnings and round-tripped 40 objects through the current inherited XML2 serializer. The protocol probe built without warnings.

A fresh, separate loopback fixture on port 18093 used independent configuration, account and data volumes. Live capability/UDP tests passed primitive create, rotate, scale, name, description, color, clone, deletion and guarded restoration. The existing local Ollama planner then passed grounded inspection, a selected two-metre move and Undo, and creation of a complete 17-component Lumi home with WorldLine and semantic metadata.

After a normal shutdown and restart, all home transforms, semantic relationships, Lumi memory and WorldLine matched the captured receipt. Undo passed, followed by another normal shutdown and restart on the final rebuilt image: UDP/API absence, semantic removal, Lumi unbinding and undo history all persisted. The probe also exposed an upstream presence-log format index error; the reviewed tracking branch and `main` both include its correction, and the final image completed the restart/undo checks. This source sync does not deploy production services or certify Windows, Linux x86-64, voice, hypergrid, or the full upstream test suite. The test client's inherited logger emits a configuration warning before the successful protocol checks; it is not a simulator failure.

Private credentials, world databases, model traces and raw logs remain outside Git. The original Harbor and Phase 2 fixtures are preserved. The historical platform documents describe their original measured commits; they are not evidence that every new upstream subsystem was exercised.

## Branch maintenance

Fetch `upstream/master`, integrate it into the tracking branch with review, then merge the reviewed tracking branch into `main`. Keep Finalverse tools and world operations on `main`. Use normal fast-forward pushes; do not force-push either history. Review upstream API/namespace changes before building the module against the matching new runtime assemblies.

The original fork performed broad branding changes before this integration. Source provenance remains traceable through the complete Git history and contributor files. A complete inherited-notice, bundled-asset and trademark audit remains a distribution gate; this merge is not legal clearance.
