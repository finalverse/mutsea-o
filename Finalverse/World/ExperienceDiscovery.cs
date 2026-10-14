// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using OpenMetaverse;
namespace Finalverse.World;

public sealed partial class WorldModule
{
    // Small, authenticated, read-only discovery. No runtime/model call, scene
    // scan, controller ID, private endpoints, memories or credential disclosure.
    object HumanExperience(string actor)
    {
        var body=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId));
        return new {
            schema_version=1,region_id=RegionId,actor_id=actor,
            recovery_required=WorldRecoveryRequired,compose_places=composerPlaces!=null,
            citizen=ExperienceAvailability.Citizen(citizensEnabled,body is {IsDeleted:false},actor==citizenOwner,
                representationReady&&!string.IsNullOrEmpty(bridgePath),representationRecovery)
        };
    }
}
