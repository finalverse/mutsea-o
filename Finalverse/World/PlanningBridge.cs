// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using System.Text.Json.Nodes;
using Nini.Config;
using OpenMetaverse;
namespace Finalverse.World;

public sealed partial class WorldModule
{
    ServerPlanner? serverPlanner;
    readonly HashSet<UUID> planning=new();
    readonly Dictionary<UUID,Queue<DateTimeOffset>> planningRequests=new();
    void InitialisePlanning(IConfigSource source)
    {
        var c=source.Configs["FinalversePlanning"];
        if(c?.GetBoolean("Enabled",false)==true)serverPlanner=new ServerPlanner(c.GetString("SocketPath","/run/finalverse-planner/planner.sock"));
    }
    object HumanPlan(JsonElement data,UUID agent,Guid session)
    {
        bool recipe=ServerPlanner.Recipe(data);JsonObject? context=null;JsonNode? undo=null;
        void Session(){if(!sessions.TryGetValue(agent,out var active)||active!=session||scene.GetScenePresence(agent) is not {IsChildAgent:false})throw new InvalidDataException("Your region session is no longer active.");}
        lock(gate)
        {
            Session();
            if(serverPlanner==null)throw new InvalidDataException("Server planning is not enabled here.");
            if(WorldRecoveryRequired)throw new InvalidDataException("World creation is paused for recovery.");
            if(recipe&&composerPlaces==null)throw new InvalidDataException("Recipes are not enabled here.");
            // Bounded actor/session admission independent of the executor throttle.
            foreach(var id in planningRequests.Where(x=>x.Value.Count==0||DateTimeOffset.UtcNow-x.Value.Last()>TimeSpan.FromMinutes(2)).Select(x=>x.Key).ToArray())planningRequests.Remove(id);
            if(!planningRequests.TryGetValue(agent,out var recent)){
                if(planningRequests.Count>=256)throw new InvalidDataException("The planner is busy. Try shortly.");
                planningRequests[agent]=recent=new();
            }
            while(recent.Count>0&&DateTimeOffset.UtcNow-recent.Peek()>TimeSpan.FromMinutes(1))recent.Dequeue();
            if(planning.Contains(agent)||recent.Count>=6)throw new InvalidDataException("Please wait before requesting another plan.");
            if(!recipe){
                var inspection=HumanInspect(agent.ToString(),data.TryGetProperty("selected_id",out var selected)?selected.GetString():null);
                context=ServerPlanner.Context(inspection);undo=JsonSerializer.SerializeToNode(inspection)!["undoable_plan_id"]?.DeepClone();
            }
            planning.Add(agent);recent.Enqueue(DateTimeOffset.UtcNow);
        }
        try {
            // Never hold the scene/transaction gate across inference.
            var result=serverPlanner!.Propose(data,context);
            lock(gate){Session();if(WorldRecoveryRequired)throw new InvalidDataException("World creation is paused for recovery.");}
            result["undoable_plan_id"]=undo;
            return result;
        }
        finally{lock(gate)planning.Remove(agent);}
    }
}
