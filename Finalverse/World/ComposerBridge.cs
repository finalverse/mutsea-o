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
    readonly Dictionary<string,(PlacePlan plan,DateTimeOffset expires)> composerPending=new();
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
        composerPlaces=places??new PlaceKernel(this,catalog,new PlaceCatalog("",catalog),directory,RegionId);
    }

    static bool IsComposition(Plan proposal)=>proposal.actions is [{type:"compose_place"}];
    bool ComposedPlan(string planId)=>composerPending.ContainsKey(planId)||composerPlaces?.Entries.Any(e=>e.plan.plan_id==planId)==true;

    object ComposePrepare(Plan proposal,string actor)
    {
        ContentCatalog.Require(composerPlaces!=null,"Composing places is not enabled in this region.");
        ContentCatalog.Require(proposal.schema_version==1&&proposal.request?.Length is >0 and <=2048&&proposal.provider?.Length is >0 and <=80&&proposal.model?.Length is >0 and <=120,"Invalid request or provenance.");
        var p=proposal.actions[0].parameters;
        ContentCatalog.Require(proposal.actions[0].target_id==null&&p.ValueKind==JsonValueKind.Object&&p.EnumerateObject().All(x=>x.Name is "draft" or "site" or "provenance"),"A composition carries only a draft, a site rule and provenance.");
        ContentCatalog.Require(!p.TryGetProperty("site",out var site)||site.GetString()=="in_front_of_actor","Composed places are sited in front of you.");
        var since=DateTimeOffset.UtcNow.AddHours(-1);
        ContentCatalog.Require(composerPlaces!.Entries.Count(e=>e.status=="committed"&&e.plan.actor_id==actor&&e.timestamp>since)<MaxComposedPerHour,"You have built many places this hour. Undo one or try later.");
        var draft=JsonSerializer.Deserialize<PlaceTemplate>(p.GetProperty("draft").GetRawText(),ContentCatalog.Json)!;
        // Identity comes from the authenticated actor, never from the gateway.
        var slug=draft.place_id[(draft.place_id.LastIndexOf('.')+1)..];
        ContentCatalog.Require(slug.Length is >0 and <=40&&slug.All(ch=>ch is >='a' and <='z' or >='0' and <='9' or '-'),"Invalid place name.");
        draft=draft with{place_id="user."+actor.Replace("-","")[..8]+"."+slug,version="0.1."+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss")};

        // Site: the entrance faces the person, the place starts just beyond them, on their ground.
        var at=Avatar(actor);var q=Orientation(actor);
        double heading=Math.Atan2(2*(q[3]*q[2]+q[0]*q[1]),1-2*(q[1]*q[1]+q[2]*q[2]));
        var entrance=draft.entrances.FirstOrDefault()?.position??[0,-draft.half_extent[1]-1,0];
        double toward=Math.Atan2(entrance[1],entrance[0]);
        double yaw=Math.Atan2(-Math.Sin(heading),-Math.Cos(heading))-toward;
        double distance=Math.Sqrt(entrance[0]*entrance[0]+entrance[1]*entrance[1])+1.0;
        double cx=at[0]+distance*Math.Cos(heading),cy=at[1]+distance*Math.Sin(heading);
        double yawDegrees=Math.Round(Math.IEEERemainder(yaw*180/Math.PI,360),4);
        var anchor=new[]{Math.Round(cx,3),Math.Round(cy,3),Math.Round(Height(cx,cy),3)};
        // Level ground across the footprint: parts sit on the anchor plane.
        double a=yaw,hx=draft.half_extent[0],hy=draft.half_extent[1];
        foreach(var (u,v) in new[]{(-1d,-1d),(1d,-1d),(-1d,1d),(1d,1d),(0d,-1d),(0d,1d),(-1d,0d),(1d,0d)})
        {
            double px=cx+u*hx*Math.Cos(a)-v*hy*Math.Sin(a),py=cy+u*hx*Math.Sin(a)+v*hy*Math.Cos(a);
            if(px<=1||py<=1||px>=SizeX-1||py>=SizeY-1)throw new InvalidDataException("The place would cross the region boundary. Turn or move and try again.");
            ContentCatalog.Require(Math.Abs(Height(px,py)-anchor[2])<=.6,"The ground here is not level enough for this place. Try flatter ground.");
        }
        PlacePlan plan;
        lock(gate)plan=composerPlaces.PlanDraft(draft,anchor,yawDegrees,actor);
        foreach(var stale in composerPending.Where(x=>x.Value.expires<=DateTimeOffset.UtcNow||x.Value.plan.actor_id==actor).Select(x=>x.Key).ToArray())composerPending.Remove(stale);
        ContentCatalog.Require(composerPending.Count<64,"Too many pending compositions. Try again shortly.");
        var expires=DateTimeOffset.UtcNow.AddMinutes(2);
        composerPending[plan.plan_id]=(plan,expires);

        // One preview line per kind of piece; the viewer shows the first six.
        var steps=plan.placements.Select((c,i)=>(c,role:plan.place.placements[i].role)).GroupBy(x=>(x.c.package.package_id,x.role))
            .OrderByDescending(g=>g.Key.role is "seating" or "focal" or "landmark").ThenByDescending(g=>g.Count())
            .Select(g=>{var first=g.First().c;var title=first.package.title;
                return new{type="create",id=plan.plan_id,before=(object?)null,after=new{id=first.entity_ids[0],name=(g.Count()>1?g.Count()+" × ":"")+title+" ("+g.Key.role+")",
                    position=first.anchor,rotation=new[]{0d,0,Math.Sin(first.yaw_degrees*Math.PI/360),Math.Cos(first.yaw_degrees*Math.PI/360)},
                    scale=new[]{1d,1,1},color=new[]{1d,1,1,1}}};}).ToArray();
        string adjustments="";
        if(p.TryGetProperty("provenance",out var prov)&&prov.ValueKind==JsonValueKind.Object&&prov.TryGetProperty("adjustments",out var adj)&&adj.ValueKind==JsonValueKind.Array)
            adjustments=string.Join(" ",adj.EnumerateArray().Take(6).Select(x=>x.TryGetProperty("reason",out var r)&&r.ValueKind==JsonValueKind.String?"Changed: "+r.GetString()+".":""));
        var summary=(plan.place.title+" — "+plan.place.description+" "+adjustments).Trim()+$" {plan.placements.Length} pieces, built as yours; Undo removes them all.";
        log.Info(JsonSerializer.Serialize(new{@event="composition_prepared",plan.plan_id,actor_id=actor,place_id=plan.place.place_id,placements=plan.placements.Length,anchor,yaw_degrees=yawDegrees,proposal.model}));
        return new{plan_id=plan.plan_id,actor_id=actor,expires_at=expires,proposal,steps,scene_revision="composition",summary};
    }

    object ComposeCommit(string planId,string actor)
    {
        ContentCatalog.Require(composerPending.TryGetValue(planId,out var pending)&&pending.plan.actor_id==actor&&pending.expires>DateTimeOffset.UtcNow,"Approval is missing, expired or belongs to another session.");
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
