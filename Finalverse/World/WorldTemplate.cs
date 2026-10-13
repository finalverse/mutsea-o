// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text;
using System.Text.Json;

namespace Finalverse.World;

// WorldTemplate: a reusable starter world composed of approved PlaceTemplates, with
// an optional pinned terrain heightmap, water height, spawn and an environment
// preset reference. It is provisioned into an empty region as one journaled
// operation; each place keeps its own place journal and rollback.
public sealed record WorldPlace(string local_id,string place_id,string version,string manifest_sha256,double[] anchor,double yaw_degrees,bool anchor_on_terrain=false);
public sealed record WorldSpawn(double[] position,double[] look_at);
public sealed record WorldLanding(string parcel_id,double[] position,double[] look_at,byte landing_type);
public sealed record WorldEntityContext(string world_id,string version,string manifest_sha256,string region_id,string operation_id,string place_plan_id);
public sealed record WorldTemplateManifest(int schema_version,string kind,string world_id,string version,string title,string description,string license,
    int[] region_size,double water_height,string? terrain_r32_sha256,string environment_preset,WorldSpawn spawn,WorldPlace[] places,string? manifest_sha256=null);
public sealed record WorldEntry(string operation_id,string status,DateTimeOffset timestamp,string world_id,string version,string manifest_sha256,string region_id,
    string? terrain_before_sha256,double? water_before,string[] place_plans,string? error,
    WorldLanding? landing_before=null,WorldLanding? landing_after=null,string? terrain_after_sha256=null,double? water_after=null,string? terrain_hash_format=null);

// Shared arrival policy for starter worlds and newcomer homes. This checks a
// standing footprint against authoritative terrain; it is not navigation or a
// substitute for continuous collision/appearance acceptance.
public static class WorldArrival
{
    public static double[] StandingPosition(double x,double y,Func<double,double,double> ground,double water)
    {
        ContentCatalog.Vector([x,y],2,1,255);
        var heights=new List<double>();
        foreach(double dx in new[]{-.5,0,.5})foreach(double dy in new[]{-.5,0,.5})heights.Add(ground(x+dx,y+dy));
        ContentCatalog.Require(heights.All(h=>double.IsFinite(h)&&h>water+.1)&&heights.Max()-heights.Min()<=.6,"Arrival needs dry, level terrain across its standing footprint.");
        return [x,y,heights.Max()+1.1];
    }
}

// The region adapter for world provisioning.
public interface IWorldTemplateRegion
{
    string RegionId{get;}
    bool IsEmpty();                                    // no persistent scene objects
    bool IsEmptyExcept(string[] rootIds);               // no later objects to erase or strand
    string TerrainHashFormat{get;}
    string TerrainHash();
    string SnapshotTerrain(string path);               // R32 snapshot; returns SHA-256
    void LoadTerrain(byte[] r32);                      // apply, persist and push to clients
    double WaterHeight{get;}
    void SetWaterHeight(double height);
    void SetLanding(double[] position,double[] lookAt);
    WorldLanding CaptureLanding(double[] position);
    bool MatchesLanding(WorldLanding landing);
    void RestoreLanding(WorldLanding landing);
    double Ground(double x,double y);
    PlaceKernel Places{get;}
    string Operator{get;}
}

public sealed class WorldTemplateCatalog
{
    readonly Dictionary<string,(WorldTemplateManifest world,byte[]? terrain)> worlds=new();
    public WorldTemplateCatalog(string root,PlaceCatalog places)
    {
        if(string.IsNullOrEmpty(root)||!Directory.Exists(root))return;
        foreach(var directory in Directory.GetDirectories(root))
        {
            ContentCatalog.Require((new DirectoryInfo(directory).Attributes&FileAttributes.ReparsePoint)==0,"Linked world directory.");
            var file=new FileInfo(Path.Combine(directory,"world.json"));
            ContentCatalog.Require(file.Exists&&(file.Attributes&FileAttributes.ReparsePoint)==0&&file.Length is >0 and <=65536,"Missing, linked or oversized world manifest.");
            var bytes=File.ReadAllBytes(file.FullName);
            var parsed=JsonSerializer.Deserialize<WorldTemplateManifest>(bytes,ContentCatalog.Json);
            ContentCatalog.Require(parsed!=null&&parsed.region_size is [256,256],"Only standard 256 m worlds are enabled.");
            var world=parsed! with{manifest_sha256=ContentCatalog.Hash(bytes)};
            byte[]? terrain=null;
            if(world.terrain_r32_sha256!=null)
            {
                var t=new FileInfo(Path.Combine(directory,"terrain.r32"));
                ContentCatalog.Require(t.Exists&&(t.Attributes&FileAttributes.ReparsePoint)==0&&t.Length==(long)world.region_size[0]*world.region_size[1]*4,"Terrain must be an R32 heightmap matching the region size.");
                terrain=File.ReadAllBytes(t.FullName);ContentCatalog.Require(ContentCatalog.Hash(terrain)==world.terrain_r32_sha256,"Terrain differs from its pin.");
            }
            Validate(world,terrain,places);
            ContentCatalog.Require(worlds.TryAdd(world.world_id+"@"+world.version,(world,terrain)),"Duplicate world identity.");
        }
    }
    public (WorldTemplateManifest world,byte[]? terrain) Get(string id,string version)=>worlds.TryGetValue(id+"@"+version,out var w)?w:throw new InvalidDataException("World is not in the operator-approved catalog.");

    static bool Text(string? s,int max)=>s is {Length:>0}&&s.Length<=max&&!s.Any(char.IsControl);
    public static void Validate(WorldTemplateManifest w,byte[]? terrain,PlaceCatalog places)
    {
        ContentCatalog.Require(w!=null&&w.schema_version==1&&w.kind=="world"&&w.license=="CC0-1.0","Only original CC0 world templates (schema 1) are enabled.");
        ContentCatalog.Require(Text(w!.world_id,120)&&Text(w.version,40)&&Text(w.title,120)&&Text(w.description,1024)&&Text(w.environment_preset,200),"Invalid world identity.");
        ContentCatalog.Require(w.region_size is [256,256],"Only standard 256 m regions are enabled.");
        ContentCatalog.Vector([w.water_height],1,0,100);
        ContentCatalog.Vector(w.spawn.position,3,1,255);ContentCatalog.Vector(w.spawn.look_at,3,0,256);
        double dx=w.spawn.look_at[0]-w.spawn.position[0],dy=w.spawn.look_at[1]-w.spawn.position[1];
        ContentCatalog.Require(dx*dx+dy*dy>.0001,"Spawn needs a horizontal facing direction.");
        if(terrain!=null)
        {
            ContentCatalog.Require(terrain.Length==256*256*4,"Terrain must be a standard region R32 heightmap.");
            for(int i=0;i<terrain.Length;i+=4){var h=BitConverter.ToSingle(terrain,i);ContentCatalog.Require(float.IsFinite(h)&&h>=0&&h<=255,"Terrain heights must be finite and within 0–255 m.");}
        }
        ContentCatalog.Require(w.places is {Length:>0 and <=32}&&w.places.Select(p=>p.local_id).Distinct().Count()==w.places.Length,"A world needs 1–32 uniquely identified places.");
        var rects=new List<(double x0,double x1,double y0,double y1,string id)>();
        foreach(var p in w.places)
        {
            ContentCatalog.Require(Text(p.local_id,120),"Invalid world place identity.");
            var place=places.Get(p.place_id,p.version);
            ContentCatalog.Require(place.manifest_sha256==p.manifest_sha256,"World place pin does not match the approved place manifest.");
            ContentCatalog.Vector(p.anchor,3,0,256);ContentCatalog.Vector([p.yaw_degrees],1,-360,360);
            double a=p.yaw_degrees*Math.PI/180,hx=place.half_extent[0],hy=place.half_extent[1];
            double ex=Math.Abs(hx*Math.Cos(a))+Math.Abs(hy*Math.Sin(a)),ey=Math.Abs(hx*Math.Sin(a))+Math.Abs(hy*Math.Cos(a));
            var r=(p.anchor[0]-ex,p.anchor[0]+ex,p.anchor[1]-ey,p.anchor[1]+ey,p.local_id);
            ContentCatalog.Require(r.Item1>1&&r.Item3>1&&r.Item2<w.region_size[0]-1&&r.Item4<w.region_size[1]-1,"A world place crosses the region boundary.");
            // Place rectangles may touch or overlap (they include empty margins). Provisioning checks each place,
            // in order, against every object already present, including earlier places' parts, by native AABB.
            rects.Add(r);
        }
    }
}

public sealed class WorldTemplateKernel
{
    readonly IWorldTemplateRegion region;readonly WorldTemplateCatalog catalog;readonly string journal,snapshots;
    readonly List<WorldEntry> entries=new();
    public bool RecoveryRequired{get;private set;}
    public IReadOnlyList<WorldEntry> Entries=>entries;
    public WorldTemplateKernel(IWorldTemplateRegion region,WorldTemplateCatalog catalog,string directory)
    {
        this.region=region;this.catalog=catalog;Directory.CreateDirectory(directory);
        journal=Path.Combine(directory,region.RegionId+".worlds.worldline.jsonl");snapshots=Path.Combine(directory,region.RegionId+".worlds");
        if(File.Exists(journal))foreach(var line in File.ReadLines(journal))entries.Add(JsonSerializer.Deserialize<WorldEntry>(line,ContentCatalog.Json)!);
        RecoveryRequired=entries.Any(e=>e.status=="recovery_required")||entries.GroupBy(e=>e.operation_id).Any(g=>g.Last().status is "intent" or "rollback_intent");
    }
    public WorldEntry? Active(string id,string version)=>entries.Where(e=>e.status=="committed"&&e.world_id==id&&e.version==version)
        .LastOrDefault(e=>!entries.Any(x=>x.operation_id==e.operation_id&&x.status=="rolled_back"));

    public WorldEntityContext? EntityContext(string entity)
    {
        foreach(var entry in entries.Where(e=>e.status=="committed").Reverse())
        {
            if(entries.Any(e=>e.operation_id==entry.operation_id&&e.status=="rolled_back"))continue;
            var place=region.Places.Entries.LastOrDefault(e=>e.status=="committed"&&entry.place_plans.Contains(e.plan.plan_id)
                &&e.plan.placements.Any(p=>p.entity_ids.Contains(entity))
                &&!region.Places.Entries.Any(x=>x.plan.plan_id==e.plan.plan_id&&x.status=="rolled_back"));
            if(place!=null)return new(entry.world_id,entry.version,entry.manifest_sha256,entry.region_id,entry.operation_id,place.plan.plan_id);
        }
        return null;
    }

    public WorldEntry Provision(string id,string version)
    {
        ContentCatalog.Require(!RecoveryRequired&&!region.Places.RecoveryRequired,"World recovery required.");
        var (world,terrain)=catalog.Get(id,version);
        ContentCatalog.Require(region.IsEmpty(),"World templates provision only into an empty region.");
        string operation=Guid.NewGuid().ToString();Directory.CreateDirectory(snapshots);
        string terrainBefore=region.SnapshotTerrain(Path.Combine(snapshots,operation+".before.r32"));double waterBefore=region.WaterHeight;
        var landingBefore=region.CaptureLanding(world.spawn.position);
        var intent=Append(new(operation,"intent",DateTimeOffset.UtcNow,world.world_id,world.version,world.manifest_sha256!,region.RegionId,terrainBefore,waterBefore,[],null,landing_before:landingBefore));
        var plans=new List<string>();
        try
        {
            if(terrain!=null)region.LoadTerrain(terrain);
            region.SetWaterHeight(world.water_height);
            // Landing is a registration postcondition, not merely a manifest hint.
            // Terrain-backed starter worlds must offer dry arrival ground and a
            // bounded standing height; floating/water starts need a future policy.
            var standing=WorldArrival.StandingPosition(world.spawn.position[0],world.spawn.position[1],region.Ground,region.WaterHeight);
            ContentCatalog.Require(world.spawn.position[2]>=standing[2]-1.1&&world.spawn.position[2]<=standing[2]+1.9,"World spawn must be within 3 m of its dry terrain.");
            foreach(var p in world.places)
            {
                double z=p.anchor_on_terrain?region.Ground(p.anchor[0],p.anchor[1]):p.anchor[2];
                var plan=region.Places.Plan(p.place_id,p.version,[p.anchor[0],p.anchor[1],z],p.yaw_degrees,region.Operator);
                region.Places.Provision(plan);plans.Add(plan.plan_id);
            }
            region.SetLanding(world.spawn.position,world.spawn.look_at);
            return Append(intent with{status="committed",timestamp=DateTimeOffset.UtcNow,place_plans=plans.ToArray(),landing_after=region.CaptureLanding(world.spawn.position),terrain_after_sha256=region.TerrainHash(),water_after=region.WaterHeight,terrain_hash_format=region.TerrainHashFormat});
        }
        catch(Exception ex)
        {
            // Compensate: roll back the places this operation provisioned (each fingerprint-guarded),
            // then restore terrain and water. Anything unconfirmed fences world writes.
            bool clean=!region.Places.RecoveryRequired;
            foreach(var plan in Enumerable.Reverse(plans)){try{region.Places.Rollback(plan);}catch{clean=false;}}
            try{region.LoadTerrain(File.ReadAllBytes(Path.Combine(snapshots,operation+".before.r32")));region.SetWaterHeight(waterBefore);region.RestoreLanding(landingBefore);}catch{clean=false;}
            RecoveryRequired=!clean;
            Append(intent with{status=clean?"compensated":"recovery_required",timestamp=DateTimeOffset.UtcNow,place_plans=plans.ToArray(),error=ex.Message});
            throw;
        }
    }

    public WorldEntry Rollback(string operation)
    {
        ContentCatalog.Require(!RecoveryRequired,"World recovery required.");
        var done=entries.LastOrDefault(e=>e.operation_id==operation&&e.status=="committed");
        ContentCatalog.Require(done!=null&&!entries.Any(e=>e.operation_id==operation&&e.status=="rolled_back"),"No active provisioned world with that operation.");
        var snapshot=Path.Combine(snapshots,operation+".before.r32");
        ContentCatalog.Require(File.Exists(snapshot)&&ContentCatalog.Hash(File.ReadAllBytes(snapshot))==done!.terrain_before_sha256,"Terrain snapshot is missing or changed.");
        // Old journals remain readable/idempotent but lack the postconditions
        // needed for safe world rollback. They require an operator-reviewed repair.
        ContentCatalog.Require(done!.landing_before!=null&&done.landing_after!=null&&done.terrain_after_sha256!=null&&done.water_after!=null&&done.terrain_hash_format==region.TerrainHashFormat,"Historical world lacks compatible rollback postconditions; operator review required.");
        ContentCatalog.Require(region.TerrainHash()==done.terrain_after_sha256&&region.WaterHeight==done.water_after&&region.MatchesLanding(done.landing_after!),"Terrain, water or landing changed after provisioning; rollback preserves later edits.");
        var placeEntries=done.place_plans.Select(region.Places.ValidateRollback).ToArray();
        var roots=placeEntries.SelectMany(e=>e.plan.placements).Select(p=>p.entity_ids[0]).ToArray();
        ContentCatalog.Require(region.IsEmptyExcept(roots),"Later scene objects are present; world rollback refuses to strand them.");
        var entry=done!;var intent=Append(entry with{status="rollback_intent",timestamp=DateTimeOffset.UtcNow});
        try
        {
            foreach(var plan in Enumerable.Reverse(entry.place_plans))region.Places.Rollback(plan);
            region.LoadTerrain(File.ReadAllBytes(snapshot));region.SetWaterHeight(entry.water_before??region.WaterHeight);
            region.RestoreLanding(entry.landing_before!);
            return Append(intent with{status="rolled_back",timestamp=DateTimeOffset.UtcNow});
        }
        catch(Exception ex){RecoveryRequired=true;Append(intent with{status="recovery_required",timestamp=DateTimeOffset.UtcNow,error=ex.Message});throw;}
    }

    WorldEntry Append(WorldEntry entry)
    {
        try{using var file=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read);var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry,ContentCatalog.Json)+"\n");file.Write(bytes);file.Flush(true);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(journal,UnixFileMode.UserRead|UnixFileMode.UserWrite);entries.Add(entry);return entry;}
        catch{RecoveryRequired=true;throw;}
    }
}
