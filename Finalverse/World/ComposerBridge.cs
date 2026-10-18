// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using Nini.Config;

namespace Finalverse.World;

// Composed places (R&D): a person asks for a place; the AI gateway's model chose options and its
// deterministic solver produced a place draft from approved Starter Library packages. Here the draft
// is untrusted data. MutSea validates it exactly as an operator place, sites it in front of the
// person, checks their own parcel rights for every part, and provisions it as them through
// PlaceKernel (journaled, fingerprint-guarded rollback). The model never supplies coordinates.
//   [FinalverseComposer]
//   Enabled = true
//   Regions = <region UUID>[,<region UUID>...]
public sealed partial class WorldModule
{
    bool composerEnabled;readonly HashSet<string> composerRegions=new();
    PlaceKernel? composerPlaces;
    ContentCatalog? composerCatalog;
    readonly Dictionary<string,(PlacePlan plan,DateTimeOffset expires,string rule)> composerPending=new();
    const int MaxComposedPerHour=12;

    void InitialiseComposer(IConfigSource source)
    {
        var c=source.Configs["FinalverseComposer"];
        composerEnabled=c?.GetBoolean("Enabled",false)??false;
        if(!composerEnabled)return;
        foreach(var id in c!.GetString("Regions","").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            if(!Guid.TryParse(id,out _))throw new InvalidDataException("Composer regions must be region UUIDs.");
            composerRegions.Add(id.ToLowerInvariant());
        }
    }

    void StartComposer(ContentCatalog catalog)
    {
        if(!composerEnabled||!composerRegions.Contains(RegionId.ToLowerInvariant()))return;
        composerCatalog=catalog;
        composerPlaces=places??new PlaceKernel(this,catalog,new PlaceCatalog("",catalog),directory,RegionId);
    }

    static bool IsComposition(Plan proposal)=>proposal.actions is [{type:"compose_place"}];
    bool ComposedPlan(string planId)=>composerPending.ContainsKey(planId)||composerPlaces?.Entries.Any(e=>e.plan.plan_id==planId)==true;

    object HumanInspect(string actor,string? selected)
    {
        var result=JsonSerializer.SerializeToNode(kernel.Inspect(actor,selected),Kernel.Json)!;
        result["recovery_required"]=WorldRecoveryRequired;
        if(WorkspaceContext() is {} workspace)result["workspace"]=JsonSerializer.SerializeToNode(workspace);
        string? previous=result["undoable_plan_id"]?.GetValue<string>();
        var timestamp=kernel.History(actor).LastOrDefault(e=>e.plan_id==previous&&e.status=="committed")?.timestamp??DateTimeOffset.MinValue;
        var latest=ComposerAudit.LatestPlace(composerPlaces?.Entries??[],actor,timestamp);
        if(latest!=null)result["undoable_plan_id"]=latest;
        return result;
    }

    object HumanHistory(string actor)=>ComposerAudit.History(kernel.History(actor),composerPlaces?.Entries??[],actor);

    object ComposePrepare(Plan proposal,string actor)
    {
        ContentCatalog.Require(composerPlaces!=null,"Composing places is not enabled in this region.");
        ContentCatalog.Require(proposal.schema_version==1&&proposal.request?.Length is >0 and <=2048&&proposal.provider?.Length is >0 and <=80&&proposal.model?.Length is >0 and <=120,"Invalid request or provenance.");
        var p=proposal.actions[0].parameters;
        ContentCatalog.Require(proposal.actions[0].target_id==null&&p.ValueKind==JsonValueKind.Object&&p.EnumerateObject().All(x=>x.Name is "draft" or "site" or "provenance"),"A composition carries only a draft, a site rule and provenance.");
        string rule=p.TryGetProperty("site",out var site)?site.GetString()??"":"in_front_of_actor";
        ContentCatalog.Require(rule is "in_front_of_actor" or "near_actor" or "near_water","Unsupported place siting rule.");
        var since=DateTimeOffset.UtcNow.AddHours(-1);
        ContentCatalog.Require(composerPlaces!.Entries.Count(e=>e.status=="committed"&&e.plan.actor_id==actor&&e.timestamp>since)<MaxComposedPerHour,"You have built many places this hour. Undo one or try later.");
        var draft=JsonSerializer.Deserialize<PlaceTemplate>(p.GetProperty("draft").GetRawText(),ContentCatalog.Json)!;
        // Identity comes from the authenticated actor, never from the gateway.
        var slug=draft.place_id[(draft.place_id.LastIndexOf('.')+1)..];
        ContentCatalog.Require(slug.Length is >0 and <=40&&slug.All(ch=>ch is >='a' and <='z' or >='0' and <='9' or '-'),"Invalid place name.");
        draft=draft with{place_id="user."+actor.Replace("-","")[..8]+"."+slug,version="0.1."+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss")};

        PlaceCatalog.Validate(draft,composerCatalog!);
        PlacePlan? proposed=null;ComposeSite? chosen=null;
        foreach(var candidate in ComposerSiting.Candidates(this,draft,actor,rule))
        {
            try { lock(gate)proposed=composerPlaces.PlanDraft(draft,candidate.anchor,candidate.yaw_degrees,actor);chosen=candidate;break; }
            catch(InvalidDataException) { if(composerPlaces.RecoveryRequired)throw; }
        }
        ContentCatalog.Require(proposed!=null,rule=="near_water"?"No clear, level and permitted shoreline site was found nearby. Move closer to water and try again.":"No clear, level and permitted site was found nearby. Move or turn and try again.");
        var plan=proposed!;var anchor=chosen!.anchor;var yawDegrees=chosen.yaw_degrees;
        string siting=rule=="near_water"?"On dry ground near water verified from the region terrain.":rule=="near_actor"?"On clear, level ground nearby.":"On clear ground in front of you.";
        foreach(var stale in composerPending.Where(x=>x.Value.expires<=DateTimeOffset.UtcNow||x.Value.plan.actor_id==actor).Select(x=>x.Key).ToArray())composerPending.Remove(stale);
        ContentCatalog.Require(composerPending.Count<64,"Too many pending compositions. Try again shortly.");
        var expires=DateTimeOffset.UtcNow.AddMinutes(2);
        composerPending[plan.plan_id]=(plan,expires,rule);

        // One preview line per kind of piece; the viewer shows the first six.
        var steps=plan.placements.Select((c,i)=>(c,role:plan.place.placements[i].role)).GroupBy(x=>(x.c.package.package_id,x.role))
            .OrderByDescending(g=>g.Key.role is "seating" or "focal" or "landmark").ThenByDescending(g=>g.Count())
            .Select(g=>{var first=g.First().c;var title=first.package.title;
                return new{type="create",id=plan.plan_id,before=(object?)null,after=new{id=first.entity_ids[0],name=(g.Count()>1?g.Count()+" × ":"")+title+" ("+g.Key.role+")",
                    position=first.anchor,rotation=new[]{0d,0,Math.Sin(first.yaw_degrees*Math.PI/360),Math.Cos(first.yaw_degrees*Math.PI/360)},
                    color=new[]{1d,1,1,1}}};}).ToArray();
        string adjustments="";
        if(p.TryGetProperty("provenance",out var prov)&&prov.ValueKind==JsonValueKind.Object&&prov.TryGetProperty("adjustments",out var adj)&&adj.ValueKind==JsonValueKind.Array)
            adjustments=string.Join(" ",adj.EnumerateArray().Take(6).Select(x=>x.TryGetProperty("reason",out var r)&&r.ValueKind==JsonValueKind.String?"Changed: "+r.GetString()+".":""));
        var summary=(plan.place.title+" — "+plan.place.description+" "+adjustments).Trim()+$" {plan.placements.Length} pieces, built as yours; Undo removes them all.";
        log.Info(JsonSerializer.Serialize(new{@event="composition_prepared",plan.plan_id,actor_id=actor,place_id=plan.place.place_id,placements=plan.placements.Length,anchor,yaw_degrees=yawDegrees,proposal.model}));
        return new{plan_id=plan.plan_id,actor_id=actor,expires_at=expires,proposal,steps,scene_revision="composition",summary,siting,
            part_count=plan.placements.Sum(p=>p.package.parts.Length),site=new{rule,anchor,yaw_degrees=yawDegrees,water_distance=chosen.water_distance}};
    }

    object ComposeCommit(string planId,string actor)
    {
        ContentCatalog.Require(composerPending.TryGetValue(planId,out var pending)&&pending.plan.actor_id==actor&&pending.expires>DateTimeOffset.UtcNow,"Approval is missing, expired or belongs to another session.");
        ContentCatalog.Require(ComposerSiting.Fits(this,pending.plan.place,pending.plan.anchor,pending.plan.yaw_degrees)
            &&(pending.rule!="near_water"||ComposerSiting.WaterDistance(this,pending.plan.anchor)!=null),"The approved terrain or shoreline changed. Preview again before building.");
        composerPending.Remove(planId);
        PlaceEntry entry;
        lock(gate)entry=composerPlaces!.Provision(pending.plan);
        log.Info(JsonSerializer.Serialize(new{@event="composition_committed",plan_id=planId,entry.operation_id,actor_id=actor,place_id=pending.plan.place.place_id}));
        return new{status="committed",plan_id=planId,entry.operation_id};
    }

    object ComposeUndo(string planId,string actor)
    {
        var done=composerPlaces?.Entries.LastOrDefault(e=>e.plan.plan_id==planId&&e.status=="committed");
        ContentCatalog.Require(done!=null&&done.plan.actor_id==actor&&!composerPlaces!.Entries.Any(e=>e.plan.plan_id==planId&&e.status=="rolled_back"),"No active composition owned by you.");
        PlaceEntry entry;
        lock(gate)entry=composerPlaces!.Rollback(planId);
        log.Info(JsonSerializer.Serialize(new{@event="composition_undone",plan_id=planId,entry.operation_id,actor_id=actor}));
        return new{status="undone",plan_id=planId,entry.operation_id};
    }
}
