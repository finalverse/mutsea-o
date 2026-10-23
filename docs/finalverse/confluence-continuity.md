# Confluence session relay · v0.7 integration

The existing `FinalverseWorld` native capability now accepts the bounded
`confluence` operations `join`, `read`, `message`, `artifact` and `leave`.
`Finalverse/World/Confluence.cs` reuses the existing private Earth HTTP client,
credential and audience configuration. It creates no public listener, new
conversation store, identity service or mutation executor.

Actor, current native login and region are derived from the capability handler,
not client-supplied JSON. A one-use handoff is bound to that native session.
The in-memory link is cleared on logout or capability renewal. Earth revalidates
the participant, grid/region, permission, expiry and revocation on each request.
Presence does not imply proposal or world-edit permission.

The relay supports at most 128 active native links and shares the existing
four-operation Earth service budget. Messages are limited to 2,048 characters;
event replay is cursor-based and response sizes are bounded. A selected artifact
must belong to the same explicitly shared non-code session. Browser sharing is
read-only, including for grants that allow native conversation proposals.

The loopback-only `MutSea.EarthBridgeSmoke` diagnostic gained a `confluence`
mode. Private account/handoff files remain outside source control. It checks real
native login, role enforcement, paginated event replay, original artifact access
and an optional bounded simultaneous-observer period. The diagnostic cannot
target an arbitrary public grid.

Local acceptance passed with two simultaneous independent avatars. An observer
with read-only access saw a new question and source artifact but could not send a
proposal. The existing Earth–World Bridge also approved a new 94-part exhibition,
recorded and inspected its WorldLine receipt, and undid it. Actual native UDP
observation confirmed all 94 receipt-bound objects were removed; existing
content remained present. The .NET 8 module image and diagnostic build passed.

Shared-session Lumi participation and hosted deployment acceptance remain open.
The existing citizen identity/controller and WorldKernel authorization paths
are preserved. See the canonical [v0.7 architecture and acceptance ledger](https://github.com/finalverse/finalverse-earth/tree/codex/confluence-v07/docs/finalverse/v07).
