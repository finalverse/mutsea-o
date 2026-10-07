# MutSea platform verification — 2026-10-07

Server baseline: `13c546ce413e49e957e387547d97e79800cf33ca`. Simulator source was rebuilt with .NET 8 rather than reusing the Phase 0 binaries. Tests use fresh isolated standalone worlds; the original development sandbox and neighboring services were preserved. MutSea remains the server brand; Finalverse remains the client brand.

| Target | Observed result | Remaining limit |
|---|---|---|
| Linux x86-64 | Fresh Release source build and Docker image; zero compiler warnings/errors. ubODE, YEngine and SQLite initialize. Real HTTP login, UDP scene stream, all 40 object IDs/names/positions/scales, capability origin and logout pass. | No public router path or deployment to the requested destination host proved. |
| Linux ARM64 | Native Docker image built on ARM64. Same real login/40-object/transform/logout checks pass, including restart without a startup import. | Full asset import, scripts, voice, teleport and multi-region coverage remain pending. |
| ARM64 native HTTPS | Valid public certificate, private Docker DNS alias, native TLS on port 443. Login and advertised seed origin are HTTPS on `grid.mutsea.com:443`; UDP streaming and all 40 transforms pass after normal shutdown/restart. Capability drop and `no-new-privileges` restrictions also pass. | This isolated test is not external world entry. Simulator certificate renewal/synchronization and public UDP forwarding remain deployment gates. |
| Finalverse macOS Intel/Rosetta → ARM64 server | Actual branded viewer entered Harbor, rendered the scene, moved and quit normally. | Avatar remains a cloud; inherited account-benefits/home notices remain. No complete avatar-appearance claim. |
| Windows simulator | No native Windows build/runtime executed this delivery. | A native builder and third-party/native dependency verification are required. |

## Reproducible inputs and receipts

The Dockerfile pins .NET SDK and runtime multi-platform manifest digests. Observed SDK was 8.0.425 and runtime 8.0.31. Fresh final local image identifiers, before any publication:

- x86-64: `sha256:541ef9a89f7ae7236aa1dad61de56424ed5544f4131330d2fcd76efb4aa85ce3`.
- ARM64: `sha256:bf75cb36aa53f09b0e1ccce14ebfab91a0c9a2f7cbb3897a78a98386ffb9c559`.

These are local Docker image identifiers, not registry publication receipts. Neither image was pushed. Record `docker image inspect` again when rebuilding: build context, base manifests, native dependencies and source revision must travel with a release.

`MutSea.StarterScene` builds against the newly compiled inherited assemblies with zero warnings/errors. Its real XML2 serializer/loader round-trip checks all 40 objects. Additional CLI checks reject a zero owner UUID and a nonempty destination while preserving its files. The configuration generator creates private files, rejects invalid hostnames and refuses to overwrite an existing configuration. These are development tools, not AI mutation endpoints.

The protocol probe uses a separate, pinned Unix-compatible test SDK; see [its build instructions](../../tools/MutSea.ProtocolSmoke/README.md). The simulator keeps its inherited assemblies. Its upstream SDK emits 77 warnings on a full build and an inherited logger configuration warning at runtime; these do not disappear merely because the probe itself builds cleanly. Avatar appearance is deliberately outside this probe. Machine-readable receipts contain object identities/transforms and capability origin, with no passwords or capability tokens. Raw logs, screenshots, account files, certificates, database snapshots and OARs remain in private operational storage outside Git.

Both native platform worlds retained exactly 40 objects through normal shutdown/restart with `startup.txt` empty. The x86-64 final image additionally started against retained data under the Compose privilege restrictions and passed the protocol check. Normal console OAR exports contain 40 scene objects; OAR export is not a full account/inventory/database backup.

## TLS regression

The inherited server extracted SAN names by splitting `AsnEncodedData.Format()` display text. Linux formats that text differently, causing a valid multi-name certificate to fail startup when the requested SAN differed from its CN. The source now delegates hostname matching to [.NET 8's X509Certificate2.MatchesHostname](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.matcheshostname?view=net-8.0), rejecting invalid input and malformed certificate data. This matcher does not establish certificate trust; the real HTTPS probe uses normal client certificate verification.

Build `tools/MutSea.TlsSmoke` against the freshly compiled server binaries, then run its DLL. It exercises the actual server constructor and checks 12 cases: SAN names, case handling, single-label wildcards, IPv4/IPv6, mismatched names/IPs and invalid input. It also verifies that startup rejects a certificate for the wrong hostname. All checks passed. A self-signed certificate exists only inside the temporary constructor test; the network fixture uses the valid public certificate.

## Public deployment state

AWS Route 53 records were created and reached `INSYNC`; existing NS/SOA records were preserved. `https://mutsea.com` and `https://www.mutsea.com` return 200. `https://grid.mutsea.com` deliberately returns 503 with `setup_pending` and `login_enabled=false`. A certificate covers all three hosts; the renewal dry-run and nginx configuration check passed. The scoped DNS updater timer has also run successfully. Website browser visual QA could not be completed after the Mac locked; HTTP/TLS checks are not a visual test.

The requested simulator destination was unreachable through the gateway, including SSH and its existing service port. No simulator deployment there or public world entry is claimed. A private fresh account/configuration and 40-object scene are prepared for first startup; that account is not registered until the destination initializes and a login is verified.

Next deployment gates: restore destination connectivity; inspect its current services/storage/capacity; install the verified image with private persistent data; synchronize and renew the native TLS certificate; configure and verify public UDP 19000; then perform external Finalverse login, avatar/object interaction and normal logout before enabling the grid route. Back up edited world/account data and retain the previous image for rollback.
