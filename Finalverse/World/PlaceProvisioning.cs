// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Globalization;
using System.Text;
using System.Text.Json;
using Nini.Config;
using OpenMetaverse;
using MutSea.Region.Framework.Scenes;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes.Serialization;

namespace Finalverse.World;

// Operator place provisioning: compose an approved PlaceTemplate into the host
// region, optionally retiring (snapshot, then remove) the sandbox objects it
// replaces. One journaled operation; compensation on failure; explicit rollback
// restores the retired objects with their original IDs. Private configuration:
//   [FinalversePlaces]
//   Enabled = true
//   HostRegion = <region UUID>
//   Catalog = /config/places
//   Provision = "place_id@version|x,y,z|yaw|retire-id,retire-id; ..."
//   ; or, alone: Rollback = <place plan id>
public sealed partial class WorldModule : IPlaceWorld
{
    bool placesEnabled;string placesHost="",placesCatalog="",placesRollback="";
    readonly List<(string id,string version,double[] anchor,double yaw,string[] retire)> placeRequests=new();
    PlaceKernel? places;
    string PlaceSnapshots=>Path.Combine(directory,RegionId+".places");

    void InitialisePlaces(IConfigSource source)
    {
        var c=source.Configs["FinalversePlaces"];
        placesEnabled=c?.GetBoolean("Enabled",false)??false;
        if(!placesEnabled)return;
        placesHost=c!.GetString("HostRegion","");placesCatalog=c.GetString("Catalog","/config/places");placesRollback=c.GetString("Rollback","");
        foreach(var entry in c.GetString("Provision","").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var parts=entry.Split('|',StringSplitOptions.TrimEntries);
            var at=parts.Length is 3 or 4?parts[0].Split('@'):[];
            var anchor=parts.Length>1?parts[1].Split(',').Select(v=>double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:double.NaN).ToArray():[];
            if(at.Length!=2||anchor.Length!=3||anchor.Any(double.IsNaN)||!double.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var yaw))
                throw new InvalidDataException("Place provisioning must be place_id@version|x,y,z|yaw[|retire-id,...].");
            placeRequests.Add((at[0],at[1],anchor,yaw,parts.Length==4?parts[3].Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries):[]));
        }
        if(!Guid.TryParse(placesHost,out _)||placesRollback!=""&&(!Guid.TryParse(placesRollback,out _)||placeRequests.Count>0))
            throw new InvalidDataException("Places need a host region; rollback names one plan and excludes provisioning.");
    }

    void StartPlaces(ContentCatalog catalog)
    {
        if(!placesEnabled||RegionId!=placesHost)return;
        places=new PlaceKernel(this,catalog,new PlaceCatalog(placesCatalog,catalog),directory,RegionId);
    }

    string PlaceOperator=>scene.RegionInfo.EstateSettings.EstateOwner.ToString();

    void PlacesPrimsLoaded(Scene s)
    {
        if(places==null)return;
        lock(gate)
        {
            try
            {
                if(placesRollback!="")
                {
                    if(!places.Entries.Any(e=>e.plan.plan_id==placesRollback&&e.status=="rolled_back"))
                    {var done=places.Rollback(placesRollback);log.Info(JsonSerializer.Serialize(new{@event="place_rolled_back",plan_id=placesRollback,done.plan.place.place_id,done.plan.place.version}));}
                    return;
                }
                foreach(var (id,version,anchor,yaw,retire) in placeRequests)
                {
                    if(places.Active(id,version) is {} active){log.Info(JsonSerializer.Serialize(new{@event="place_already_provisioned",place_id=id,version,plan_id=active.plan.plan_id}));continue;}
                    var plan=places.Plan(id,version,anchor,yaw,PlaceOperator,retire);
                    var done=places.Provision(plan);
                    log.Info(JsonSerializer.Serialize(new{@event="place_provisioned",plan_id=plan.plan_id,place_id=id,version,placements=plan.placements.Length,retired=plan.retired.Length,operation_id=done.operation_id}));
                }
            }
            catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="place_provisioning_failed",exception_type=ex.GetType().Name,error=ex.Message,recovery_required=places.RecoveryRequired}));}
        }
    }

    void IPlaceWorld.CheckPlace(PlacePlan plan)
    {
        var owner=new UUID(plan.actor_id);var retire=plan.retired.Select(r=>new UUID(r.group_id)).ToHashSet();
        // The place rectangle in region coordinates (rotated bounds' AABB) must lie inside the region on dry ground.
        double a=plan.yaw_degrees*Math.PI/180,hx=plan.place.half_extent[0],hy=plan.place.half_extent[1];
        double ex=Math.Abs(hx*Math.Cos(a))+Math.Abs(hy*Math.Sin(a)),ey=Math.Abs(hx*Math.Sin(a))+Math.Abs(hy*Math.Cos(a));
        double minX=plan.anchor[0]-ex,maxX=plan.anchor[0]+ex,minY=plan.anchor[1]-ey,maxY=plan.anchor[1]+ey;
        ContentCatalog.Require(minX>1&&minY>1&&maxX<SizeX-1&&maxY<SizeY-1,"Place crosses the region boundary.");
        foreach(var p in plan.placements)
        {
            var ground=Height(p.anchor[0],p.anchor[1]);
            ContentCatalog.Require(ground>WaterHeight+.3&&p.anchor[2]>=ground-.25&&p.anchor[2]-ground<=8.5,"Placement is not on dry ground within the place envelope.");
            ContentCatalog.Require(p.entity_ids.All(id=>scene.GetSceneObjectPart(new UUID(id))==null),"Placement ID already exists.");
            foreach(var part in p.package.parts)
                ContentCatalog.Require(CanCreate(plan.actor_id,p.package.parts.Length,p.anchor.Zip(PlaceKernel.Rotate(part.position,p.yaw_degrees),(x,o)=>x+o).ToArray()),"Native parcel rez policy denied a placement.");
        }
        foreach(var r in plan.retired)
        {
            var g=scene.GetSceneObjectGroup(new UUID(r.group_id));
            ContentCatalog.Require(g!=null&&!g.IsDeleted&&!g.IsAttachment&&g.GetSittingAvatars().Count==0&&g.UUID.ToString()!=Kernel.LumiId,"A retired object is missing, worn, occupied or a citizen.");
        }
        // Nothing else may intrude into the place; siblings overlap by design and are created afterwards.
        foreach(var existing in scene.GetSceneObjectGroups().Where(g=>!g.IsDeleted&&!g.IsAttachment&&!retire.Contains(g.UUID)&&!galleryIds.Contains(g.UUID.ToString())))
        {
            existing.GetAxisAlignedBoundingBoxRaw(out var x0,out var x1,out var y0,out var y1,out _,out _);var pos=existing.AbsolutePosition;
            ContentCatalog.Require(pos.X+x1<minX||pos.X+x0>maxX||pos.Y+y1<minY||pos.Y+y0>maxY,"Place overlaps an existing object ("+existing.Name+"); retire it explicitly or move the place.");
        }
    }

    PlaceRetirement[] IPlaceWorld.Retire(PlacePlan plan)
    {
        var folder=Path.Combine(PlaceSnapshots,plan.plan_id);Directory.CreateDirectory(folder);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var result=new List<PlaceRetirement>();
        foreach(var r in plan.retired)
        {
            var g=scene.GetSceneObjectGroup(new UUID(r.group_id))??throw new InvalidDataException("Retired object disappeared.");
            var xml=Encoding.UTF8.GetBytes(SceneObjectSerializer.ToXml2Format(g));var file=Path.Combine(folder,r.group_id+".xml");
            using(var f=new FileStream(file,FileMode.CreateNew,FileAccess.Write)){f.Write(xml);f.Flush(true);}
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            result.Add(r with{snapshot_sha256=ContentCatalog.Hash(xml)});
            scene.DeleteSceneObject(g,false);
            ContentCatalog.Require(scene.GetSceneObjectGroup(new UUID(r.group_id)) is null or {IsDeleted:true},"Retirement was not confirmed.");
        }
        return result.ToArray();
    }

    void IPlaceWorld.Restore(PlacePlan plan)
    {
        foreach(var r in plan.retired.Where(r=>r.snapshot_sha256!=null))
        {
            if(scene.GetSceneObjectGroup(new UUID(r.group_id)) is {IsDeleted:false})continue;
            var file=Path.Combine(PlaceSnapshots,plan.plan_id,r.group_id+".xml");var xml=File.ReadAllBytes(file);
            ContentCatalog.Require(ContentCatalog.Hash(xml)==r.snapshot_sha256,"Retirement snapshot changed.");
            var g=SceneObjectSerializer.FromXml2Format(Encoding.UTF8.GetString(xml))??throw new InvalidDataException("Retirement snapshot could not be parsed.");
            ContentCatalog.Require(g.UUID==new UUID(r.group_id)&&scene.AddNewSceneObject(g,true),"Simulator rejected a restored object.");
            g.HasGroupChanged=true;scene.ForceSceneObjectBackup(g);g.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
        }
    }
}
