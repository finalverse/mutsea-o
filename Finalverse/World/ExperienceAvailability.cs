// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
namespace Finalverse.World;

// Discovery describes current relay eligibility, not an authorization grant or
// an AI-provider health check. The existing executors revalidate every action.
public sealed record CitizenAvailability(bool present,bool can_interact,string state);
public static class ExperienceAvailability
{
    public static CitizenAvailability Citizen(bool enabled,bool embodied,bool controller,bool bridgeReady,bool representationFailed)
    {
        if(!enabled)return new(embodied,false,"not_enabled");
        if(!embodied)return new(false,false,"not_in_world");
        if(!controller)return new(true,false,"not_controller");
        if(representationFailed)return new(true,false,"representation_recovery");
        if(!bridgeReady)return new(true,false,"starting");
        return new(true,true,"enabled");
    }
}
