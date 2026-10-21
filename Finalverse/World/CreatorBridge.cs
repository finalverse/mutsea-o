// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using OpenMetaverse;
namespace Finalverse.World;
public sealed partial class WorldModule
{
    CreatorLibrary? creatorLibrary;
    CreatorLibrary Creators=>creatorLibrary??=new CreatorLibrary(Path.Combine(directory,"creator-library"));
    object HumanCreator(JsonElement data,string actor)
    {
        var command=data.GetProperty("command").GetString();
        if(command=="list")return new{releases=Creators.Mine(actor),listings=Creators.Published(),checkout_enabled=false};
        if(command=="save")
        {
            string planId=data.GetProperty("plan_id").GetString()!;
            ContentCatalog.Require(!IsEarthPlan(planId),"Earth experiences retain separate data attribution and WorldBindings. Export as a generic creator recipe is not enabled in this preview.");
            var entries=composerPlaces?.Entries.Where(e=>e.plan.plan_id==planId).ToArray()??[];
            var last=entries.LastOrDefault();
            ContentCatalog.Require(last is {status:"committed"}&&last.plan.actor_id==actor&&last.fingerprints?.Length==last.plan.placements.Length,"Save requires your own active committed place recipe.");
            for(int i=0;i<last!.plan.placements.Length;i++)
            {
                var placement=last.plan.placements[i];
                ContentCatalog.Require(((IContentWorld)this).MatchesFingerprint(placement,last.fingerprints![i]),"This place changed outside its recipe. Save those edits through native inventory; recipe export refuses to lose them.");
                foreach(var id in placement.entity_ids)
                {
                    var part=scene.GetSceneObjectPart(new UUID(id));
                    ContentCatalog.Require(part!=null&&part.OwnerID.ToString()==actor,"All released pieces must still belong to you.");
                }
            }
            PlaceCatalog.Validate(last.plan.place,composerCatalog!);
            return Creators.Save(actor,RegionId,planId,data.GetProperty("title").GetString()!,JsonSerializer.SerializeToElement(last.plan.place));
        }
        if(command is "publish" or "unpublish")return Creators.Publish(actor,data.GetProperty("release_id").GetString()!,command=="publish",
            data.TryGetProperty("price_minor",out var price)?price.GetInt64():0,data.TryGetProperty("currency",out var currency)?currency.GetString()!:"USD");
        if(command=="remix")
        {
            var release=Creators.Get(data.GetProperty("release_id").GetString()!);
            ContentCatalog.Require(release.content.owner_id==actor||Creators.Published().Any(l=>l.release_id==release.release_id&&l.price_minor==0),"This release is not a published free recipe.");
            var parameters=JsonSerializer.SerializeToElement(new{draft=release.content.recipe,site="near_actor",provenance=new{parent_release=release.release_id}});
            var prepared=ComposePrepare(new Plan(1,"Remix "+release.content.title,"mutsea-creator-library","recipe-v1",[new ActionSpec("compose_place",null,parameters)]),actor);
            string plan=JsonSerializer.SerializeToElement(prepared).GetProperty("plan_id").GetString()!;
            Creators.LinkRemix(actor,plan,release.release_id);return prepared;
        }
        throw new InvalidDataException("Unknown creator operation.");
    }
}
