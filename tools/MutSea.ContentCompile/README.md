# Offline Starter Library mesh compiler

Validate an original asset package with `tools/MutSea.Content/validate.py` first. Build this tool against the source-matched native SDK (`-p:MutSeaBin=/absolute/native-bin`), then pass the validated package directory and a new output directory. Existing outputs are preserved.

The adapter uses inherited `ColladaLoader`/LLMesh serialization, preserving source hashes and local entity IDs. It supports bounded, untextured, unrigged static assets. Compiled format v2 includes semantic tags, affordances and relationships. `ContentCatalog` revalidates identity, semantics, budgets and mesh hashes before a server operator can instantiate an approved package. Compilation is not runtime, visual, collision or performance acceptance.

Four LOD references share the high geometry for these small test assets. Native collision is a conservative convex hull. General uploads, place-scale packages, rigged avatars and final production LODs remain unsupported.
