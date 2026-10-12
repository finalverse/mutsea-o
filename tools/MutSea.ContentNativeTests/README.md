# Native content fingerprint tests

Build/run with `-p:MutSeaBin=/absolute/source-matched/native-bin`. Six tests use actual inherited `SceneObjectGroup`, serialization and shape/material representations. They prove that native persistence, signed zero and link numbering preserve the fingerprint while later transforms, owner/permissions, mesh and material changes block Undo. A live simulator/database restart remains a separate acceptance test.
