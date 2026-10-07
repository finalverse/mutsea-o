# MutSea operations for Finalverse

MutSea remains the simulator/server brand. Finalverse is the world client. This delivery adds deployment packaging and a deterministic starter scene around the inherited server, without replacing its world, account, inventory, asset or physics systems.

- [Deployment](deployment.md): fresh-instance preparation, TLS, persistent data and rollback.
- [MutSea Harbor](starter-world.md): scene contents, import and provenance.
- [Platform verification](platform-verification.md): observed build/runtime results and remaining gates.
- [Upstream synchronization](upstream-sync-20261008.md): merge order, branch roles and verification of the updated runtime.

Server baseline: `13c546ce413e49e957e387547d97e79800cf33ca`. New tools and deployment assets are authored separately; inherited copyright and BSD notices remain in place. Docker images include `LICENSE.txt` and `CONTRIBUTORS.txt`. This is a development deployment, not a production readiness claim.
