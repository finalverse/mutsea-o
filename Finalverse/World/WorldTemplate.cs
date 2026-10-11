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
public sealed record WorldTemplateManifest(int schema_version,string kind,string world_id,string version,string title,string description,string license,
    int[] region_size,double water_height,string? terrain_r32_sha256,string environment_preset,WorldSpawn spawn,WorldPlace[] places,string? manifest_sha256=null);
public sealed record WorldEntry(string operation_id,string status,DateTimeOffset timestamp,string world_id,string version,string manifest_sha256,string region_id,
    string? terrain_before_sha256,double? water_before,string[] place_plans,string? error);

// The region adapter for world provisioning.
public interface IWorldTemplateRegion
{
    string RegionId{get;}
    bool IsEmpty();                                    // no persistent scene objects
    string SnapshotTerrain(string path);               // R32 snapshot; returns SHA-256
    void LoadTerrain(byte[] r32);                      // apply, persist and push to clients
    double WaterHeight{get;}
    void SetWaterHeight(double height);
    void SetLanding(double[] position,double[] lookAt);
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
            var world=JsonSerializer.Deserialize<WorldTemplateManifest>(bytes,ContentCatalog.Json)! with{manifest_sha256=ContentCatalog.Hash(bytes)};
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
        if(terrain!=null)
            for(int i=0;i<terrain.Length;i+=4){var h=BitConverter.ToSingle(terrain,i);ContentCatalog.Require(float.IsFinite(h)&&h>=0&&h<=255,"Terrain heights must be finite and within 0–255 m.");}
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

    public WorldEntry Provision(string id,string version)
    {
        ContentCatalog.Require(!RecoveryRequired&&!region.Places.RecoveryRequired,"World recovery required.");
        var (world,terrain)=catalog.Get(id,version);
        ContentCatalog.Require(region.IsEmpty(),"World templates provision only into an empty region.");
        string operation=Guid.NewGuid().ToString();Directory.CreateDirectory(snapshots);
        string terrainBefore=region.SnapshotTerrain(Path.Combine(snapshots,operation+".before.r32"));double waterBefore=region.WaterHeight;
        var intent=Append(new(operation,"intent",DateTimeOffset.UtcNow,world.world_id,world.version,world.manifest_sha256!,region.RegionId,terrainBefore,waterBefore,[],null));
        var plans=new List<string>();
        try
        {
            if(terrain!=null)region.LoadTerrain(terrain);
            region.SetWaterHeight(world.water_height);
            foreach(var p in world.places)
            {
                double z=p.anchor_on_terrain?region.Ground(p.anchor[0],p.anchor[1]):p.anchor[2];
                var plan=region.Places.Plan(p.place_id,p.version,[p.anchor[0],p.anchor[1],z],p.yaw_degrees,region.Operator);
                region.Places.Provision(plan);plans.Add(plan.plan_id);
            }
            region.SetLanding(world.spawn.position,world.spawn.look_at);
            return Append(intent with{status="committed",timestamp=DateTimeOffset.UtcNow,place_plans=plans.ToArray()});
        }
        catch(Exception ex)
        {
            // Compensate: roll back the places this operation provisioned (each fingerprint-guarded),
            // then restore terrain and water. Anything unconfirmed fences world writes.
            bool clean=!region.Places.RecoveryRequired;
            foreach(var plan in Enumerable.Reverse(plans)){try{region.Places.Rollback(plan);}catch{clean=false;}}
            try{region.LoadTerrain(File.ReadAllBytes(Path.Combine(snapshots,operation+".before.r32")));region.SetWaterHeight(waterBefore);}catch{clean=false;}
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
        var entry=done!;var intent=Append(entry with{status="rollback_intent",timestamp=DateTimeOffset.UtcNow});
        try
        {
            foreach(var plan in Enumerable.Reverse(entry.place_plans))region.Places.Rollback(plan);
            region.LoadTerrain(File.ReadAllBytes(snapshot));region.SetWaterHeight(entry.water_before??region.WaterHeight);
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
