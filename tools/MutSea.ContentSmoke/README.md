# Native content acceptance probe

Build against the pinned Unix-compatible **test SDK** described in `tools/MutSea.ProtocolSmoke/README.md`, not the simulator's bundled client SDK. The latter attempts Windows-only socket controls on Unix. Neither probe replaces simulator assemblies.

Arguments: private account JSON path, receipt path, `import|verify|undo|verify_undo`, optional approved package ID and exact version. Import requires a new receipt path. Verification and Undo reuse that receipt, including the original canonical IDs. Diagnostics and evidence are private files; capability URLs must not be printed or committed.

The default target must be loopback. Remote acceptance requires HTTPS and an explicit `content_test_grid` field in the private operator account JSON, exactly matching `grid_url`. Negotiated World capability origins must match that target. Only use designated development accounts/worlds. This is an operator acceptance tool, not an LLM executor.

The tool requests a preview near the test avatar, lets native policy reject obstructed sites, commits the approved package, checks real UDP mesh bindings, compares every declared semantic record and maps component relationships to instance UUIDs. Restart verification and guarded Undo use the same saved receipt. Fixture coordinates are test setup, not product destination routing. Actual viewer rendering, collision and sitting remain separate checks.
