// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finalverse.World;

// PlaceTemplate: an original composition of operator-approved small assets.
// Each placement pins package@version and its manifest hash, with an offset and
// yaw inside the place bounds. The template has no meshes of its own and cannot
// widen the small-asset limits; it adds template-local IDs, place semantics and
// entrances, and one journaled transaction for the whole composition.
public sealed record PlacePlacement(string local_id,string package_id,string version,string manifest_sha256,double[] offset,double yaw_degrees,string role);
public sealed record PlaceEntrance(string local_id,double[] position,double yaw_degrees);
public sealed record PlaceTemplate(int schema_version,string kind,string place_id,string version,string title,string description,string license,
    double[] half_extent,string[] tags,string[] affordances,PlacePlacement[] placements,PlaceEntrance[] entrances,string? manifest_sha256=null);
public sealed record PlaceRetirement(string group_id,string? snapshot_sha256=null);
// accepted: existing objects the operator explicitly allows inside the footprint (for example a ground path
// running beneath a deck); recorded in the plan for audit, never modified.
public sealed record PlacePlan(string plan_id,string actor_id,PlaceTemplate place,double[] anchor,double yaw_degrees,ContentPlan[] placements,PlaceRetirement[] retired,string[]? accepted=null);
public sealed record PlaceEntry(string operation_id,string status,DateTimeOffset timestamp,PlacePlan plan,string[]? fingerprints,string? error);

// The world adapter for places. Overlap between sibling placements is by design,
// so the place is checked once, as a whole, before anything is created.
public interface IPlaceWorld:IContentWorld
{
    void CheckPlace(PlacePlan plan);
    // Snapshot then remove pre-existing groups the place replaces; returns their snapshot hashes.
    PlaceRetirement[] Retire(PlacePlan plan);
    void Restore(PlacePlan plan);
}

public sealed class PlaceCatalog
{
    public const double MaxHalfExtent=32;public const int MaxPlacements=96,MaxTriangles=65536,MaxLights=12;
    readonly Dictionary<string,PlaceTemplate> places=new();
    public PlaceCatalog(string root,ContentCatalog content)
    {
        if(string.IsNullOrEmpty(root)||!Directory.Exists(root))return;
        foreach(var directory in Directory.GetDirectories(root))
        {
            var info=new DirectoryInfo(directory);ContentCatalog.Require((info.Attributes&FileAttributes.ReparsePoint)==0,"Linked place directory.");
            var file=new FileInfo(Path.Combine(directory,"place.json"));
            ContentCatalog.Require(file.Exists&&(file.Attributes&FileAttributes.ReparsePoint)==0&&file.Length is >0 and <=262144,"Missing, linked or oversized place manifest.");
            var bytes=File.ReadAllBytes(file.FullName);
            var place=JsonSerializer.Deserialize<PlaceTemplate>(bytes,ContentCatalog.Json)! with{manifest_sha256=ContentCatalog.Hash(bytes)};
            Validate(place,content);
            ContentCatalog.Require(places.TryAdd(place.place_id+"@"+place.version,place),"Duplicate place identity.");
        }
    }
    public PlaceTemplate Get(string id,string version)=>places.TryGetValue(id+"@"+version,out var p)?p:throw new InvalidDataException("Place is not in the operator-approved catalog.");
    public IEnumerable<PlaceTemplate> All=>places.Values;

    static bool Text(string? s,int max)=>s is {Length:>0}&&s.Length<=max&&!s.Any(char.IsControl);
    // Horizontal footprint radius about the asset anchor. A part turned only about Z cannot reach
    // beyond its XY diagonal; a tilted part may, so it keeps the full 3D diagonal.
    public static double Radius(ContentPackage p)=>p.parts.Max(x=>Math.Sqrt(x.position[0]*x.position[0]+x.position[1]*x.position[1])
        +(Math.Abs(x.rotation[0])<1e-6&&Math.Abs(x.rotation[1])<1e-6?Math.Sqrt(x.scale[0]*x.scale[0]+x.scale[1]*x.scale[1]):Math.Sqrt(x.scale.Sum(s=>s*s)))/2)+.25;
    public static void Validate(PlaceTemplate t,ContentCatalog content)
    {
        ContentCatalog.Require(t!=null&&t.schema_version==1&&t.kind=="place"&&t.license=="CC0-1.0","Only original CC0 place templates (schema 1) are enabled.");
        ContentCatalog.Require(Text(t!.place_id,120)&&Text(t.version,40)&&Text(t.title,120)&&Text(t.description,512),"Invalid place identity.");
        ContentCatalog.Vector(t.half_extent,2,2,MaxHalfExtent);
        ContentCatalog.Require(t.tags is {Length:<=32}&&t.tags.All(x=>Text(x,64))&&t.affordances is {Length:>0 and <=16}&&t.affordances.Distinct().Count()==t.affordances.Length
            &&t.affordances.All(a=>a is "inspect" or "sit" or "walk" or "enter" or "look_at" or "speak" or "interact" or "gather"),"Invalid place semantics.");
        ContentCatalog.Require(t.placements is {Length:>0 and <=MaxPlacements}&&t.placements.Select(p=>p.local_id).Distinct().Count()==t.placements.Length,"Place needs 1–96 uniquely identified placements.");
        int triangles=0,lights=0;
        foreach(var p in t.placements)
        {
            ContentCatalog.Require(Text(p.local_id,120)&&Text(p.role,64),"Invalid placement identity.");
            ContentCatalog.Vector(p.offset,3,-MaxHalfExtent,MaxHalfExtent);ContentCatalog.Vector([p.yaw_degrees],1,-360,360);
            ContentCatalog.Require(p.offset[2] is >=-2 and <=8,"Placement height outside the place envelope.");
            var asset=content.Get(p.package_id,p.version).package;
            ContentCatalog.Require(asset.manifest_sha256==p.manifest_sha256,"Placement pin does not match the approved asset manifest.");
            var r=Radius(asset);
            ContentCatalog.Require(Math.Abs(p.offset[0])+r<=t.half_extent[0]+.5&&Math.Abs(p.offset[1])+r<=t.half_extent[1]+.5,"Placement footprint leaves the place bounds.");
            triangles+=asset.parts.Sum(x=>x.triangles);lights+=asset.parts.Count(x=>x.light!=null);
        }
        ContentCatalog.Require(triangles<=MaxTriangles&&lights<=MaxLights,"Place triangle or light budget exceeded.");
        ContentCatalog.Require(t.entrances is {Length:<=16}&&t.entrances.Select(e=>e.local_id).Distinct().Count()==t.entrances.Length,"Invalid entrances.");
        foreach(var e in t.entrances)
        {
            ContentCatalog.Require(Text(e.local_id,120),"Invalid entrance identity.");
            ContentCatalog.Vector(e.position,3,-MaxHalfExtent-2,MaxHalfExtent+2);ContentCatalog.Vector([e.yaw_degrees],1,-360,360);
            ContentCatalog.Require(Math.Abs(e.position[0])<=t.half_extent[0]+2&&Math.Abs(e.position[1])<=t.half_extent[1]+2,"Entrance outside the place approach.");
        }
    }
}

public sealed class PlaceKernel
{
    readonly IPlaceWorld world;readonly ContentCatalog content;readonly PlaceCatalog places;readonly string journal;
    readonly List<PlaceEntry> entries=new();
    public bool RecoveryRequired{get;private set;}
    public IReadOnlyList<PlaceEntry> Entries=>entries;
    public PlaceKernel(IPlaceWorld world,ContentCatalog content,PlaceCatalog places,string directory,string region)
    {
        this.world=world;this.content=content;this.places=places;Directory.CreateDirectory(directory);
        journal=Path.Combine(directory,region+".places.worldline.jsonl");
        if(File.Exists(journal))foreach(var line in File.ReadLines(journal))entries.Add(JsonSerializer.Deserialize<PlaceEntry>(line,ContentCatalog.Json)!);
        // An operation whose last record is not terminal was interrupted: fence writes for an operator.
        RecoveryRequired=entries.Any(e=>e.status=="recovery_required")||entries.GroupBy(e=>e.plan.plan_id).Any(g=>g.Last().status is "intent" or "retired" or "rollback_intent");
    }
    static string Id(string plan,int index,string kind)=>new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("finalverse-place:"+plan+":"+kind+":"+index)).Take(16).ToArray()).ToString();
    public static double[] Rotate(double[] v,double yawDegrees)
    {
        double a=yawDegrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);
        return [v[0]*c-v[1]*s,v[0]*s+v[1]*c,v[2]];
    }

    public PlacePlan Plan(string placeId,string version,double[] anchor,double yawDegrees,string actor,string[]? retire=null,string[]? accept=null)
    {
        ContentCatalog.Require(!RecoveryRequired,"Place recovery required.");ContentCatalog.Require(Guid.TryParse(actor,out _),"Invalid operator actor.");
        ContentCatalog.Vector(anchor,3,0,4096);ContentCatalog.Vector([yawDegrees],1,-360,360);
        ContentCatalog.Require((retire??[]).All(id=>Guid.TryParse(id,out _))&&(retire??[]).Distinct().Count()==(retire??[]).Length&&(retire??[]).Length<=128,"Invalid retirement list.");
        ContentCatalog.Require((accept??[]).All(id=>Guid.TryParse(id,out _))&&(accept??[]).Length<=32&&!(accept??[]).Intersect(retire??[]).Any(),"Invalid accepted-overlap list.");
        return Build(places.Get(placeId,version),anchor,yawDegrees,actor,retire,accept);
    }

    // A composed (non-catalog) place: validated exactly like an approved place, never retiring anything.
    public PlacePlan PlanDraft(PlaceTemplate draft,double[] anchor,double yawDegrees,string actor)
    {
        ContentCatalog.Require(!RecoveryRequired,"Place recovery required.");ContentCatalog.Require(Guid.TryParse(actor,out _),"Invalid actor.");
        ContentCatalog.Vector(anchor,3,0,4096);ContentCatalog.Vector([yawDegrees],1,-360,360);
        ContentCatalog.Require(draft!=null&&draft.place_id.StartsWith("user.",StringComparison.Ordinal),"Composed places carry a user place identity.");
        draft=draft! with{manifest_sha256=ContentCatalog.Hash(JsonSerializer.SerializeToUtf8Bytes(draft with{manifest_sha256=null},ContentCatalog.Json))};
        PlaceCatalog.Validate(draft,content);
        return Build(draft,anchor,yawDegrees,actor,null,null);
    }

    PlacePlan Build(PlaceTemplate place,double[] anchor,double yawDegrees,string actor,string[]? retire,string[]? accept)
    {
        string plan=Guid.NewGuid().ToString();
        var plans=place.placements.Select((p,i)=>{
            var asset=content.Get(p.package_id,p.version).package;var offset=Rotate(p.offset,yawDegrees);
            return new ContentPlan(Id(plan,i,"plan"),actor,DateTimeOffset.UtcNow.AddMinutes(10),asset,anchor.Zip(offset,(a,o)=>a+o).ToArray(),
                asset.parts.Select((_,j)=>Id(plan,i*64+j,"entity")).ToArray(),asset.parts.Select((_,j)=>Id(plan,i*64+j,"asset")).ToArray(),yawDegrees+p.yaw_degrees);
        }).ToArray();
        var result=new PlacePlan(plan,actor,place,anchor.ToArray(),yawDegrees,plans,(retire??[]).Select(id=>new PlaceRetirement(id)).ToArray(),(accept??[]).ToArray());
        world.CheckPlace(result);return result;
    }

    public PlaceEntry? Active(string placeId,string version)=>entries.Where(e=>e.status=="committed"&&e.plan.place.place_id==placeId&&e.plan.place.version==version)
        .LastOrDefault(e=>!entries.Any(x=>x.plan.plan_id==e.plan.plan_id&&x.status=="rolled_back"));

    public PlaceEntry Provision(PlacePlan plan)
    {
        ContentCatalog.Require(!RecoveryRequired,"Place recovery required.");
        world.CheckPlace(plan);
        var intent=Append(new(Guid.NewGuid().ToString(),"intent",DateTimeOffset.UtcNow,plan,null,null));
        var retired=plan.retired;bool removed=false;
        try
        {
            if(plan.retired.Length>0){retired=world.Retire(plan);removed=true;plan=plan with{retired=retired};Append(intent with{status="retired",timestamp=DateTimeOffset.UtcNow,plan=plan});}
            var fingerprints=new List<string>();
            foreach(var placement in plan.placements)
            {
                var asset=content.Get(placement.package.package_id,placement.package.version);
                fingerprints.Add(world.Create(placement,asset.meshes,asset.materials));
            }
            return Append(intent with{status="committed",timestamp=DateTimeOffset.UtcNow,plan=plan,fingerprints=fingerprints.ToArray()});
        }
        catch(Exception ex)
        {
            // Compensate within this operation only: remove what it created (newest first),
            // restore what it retired, then confirm. Anything unconfirmed fences writes.
            bool clean=true;
            foreach(var placement in Enumerable.Reverse(plan.placements))
            {
                try{if(world.Fingerprint(placement)!=null)world.Delete(placement);if(world.Fingerprint(placement)!=null)clean=false;}
                catch{clean=false;}
            }
            if(removed)try{world.Restore(plan);}catch{clean=false;}
            RecoveryRequired=!clean;
            Append(intent with{status=clean?"compensated":"recovery_required",timestamp=DateTimeOffset.UtcNow,plan=plan,error=ex.Message});
            throw;
        }
    }

    public PlaceEntry Rollback(string planId)
    {
        ContentCatalog.Require(!RecoveryRequired,"Place recovery required.");
        var done=entries.LastOrDefault(e=>e.plan.plan_id==planId&&e.status=="committed");
        ContentCatalog.Require(done!=null&&!entries.Any(e=>e.plan.plan_id==planId&&e.status=="rolled_back"),"No active provisioned place with that plan.");
        var plan=done!.plan;
        for(int i=0;i<plan.placements.Length;i++)
            ContentCatalog.Require(world.MatchesFingerprint(plan.placements[i],done.fingerprints![i]),"A placement changed after provisioning; rollback refuses to erase later edits.");
        foreach(var placement in plan.placements)world.CheckDelete(placement);
        var intent=Append(done with{operation_id=Guid.NewGuid().ToString(),status="rollback_intent",timestamp=DateTimeOffset.UtcNow});
        try
        {
            foreach(var placement in Enumerable.Reverse(plan.placements)){world.Delete(placement);ContentCatalog.Require(world.Fingerprint(placement)==null,"Deletion was not confirmed.");}
            if(plan.retired.Length>0)world.Restore(plan);
            return Append(intent with{status="rolled_back",timestamp=DateTimeOffset.UtcNow});
        }
        catch(Exception ex){RecoveryRequired=true;Append(intent with{status="recovery_required",timestamp=DateTimeOffset.UtcNow,error=ex.Message});throw;}
    }

    // Semantics for any part a provisioned place created: the asset's own part semantics
    // plus its place context (role, place identity, entrances in world coordinates).
    public object? Semantic(string id,string actor)
    {
        foreach(var e in entries.Where(e=>e.status=="committed").Reverse())
        {
            if(entries.Any(x=>x.plan.plan_id==e.plan.plan_id&&x.status=="rolled_back"))continue;
            int i=Array.FindIndex(e.plan.placements,p=>p.entity_ids.Contains(id));if(i<0)continue;
            var placement=e.plan.placements[i];if(world.Fingerprint(placement)==null)return null;
            var source=e.plan.place.placements[i];var part=placement.package.parts[Array.IndexOf(placement.entity_ids,id)];var metadata=part.semantics;
            var relationships=(metadata?.relationships??[]).Select(r=>new{r.type,target=placement.entity_ids[Array.FindIndex(placement.package.parts,p=>p.local_id==r.target)]}).ToArray();
            return new{entity_id=id,structure_id=placement.entity_ids[0],part.local_id,part.semantic_type,part.description,tags=metadata?.tags??[],affordances=metadata?.affordances??[],relationships,seat=part.seat,light=part.light,
                semantic_metadata_available=metadata!=null,package_id=placement.package.package_id,package_version=placement.package.version,placement.package.manifest_sha256,license=placement.package.license,
                place=new{e.plan.place.place_id,e.plan.place.version,e.plan.place.title,e.plan.place.description,e.plan.place.tags,e.plan.place.affordances,placement=source.local_id,source.role,
                    entrances=e.plan.place.entrances.Select(x=>new{x.local_id,position=e.plan.anchor.Zip(Rotate(x.position,e.plan.yaw_degrees),(a,o)=>a+o).ToArray(),yaw_degrees=e.plan.yaw_degrees+x.yaw_degrees}).ToArray(),
                    manifest_sha256=e.plan.place.manifest_sha256,operation_id=e.operation_id},
                owner_id=e.plan.actor_id,version=1,provenance="Operator-provisioned original place template; no external authoring code executed"};
        }
        return null;
    }

    PlaceEntry Append(PlaceEntry entry)
    {
        try{using var file=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read);var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry,ContentCatalog.Json)+"\n");file.Write(bytes);file.Flush(true);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(journal,UnixFileMode.UserRead|UnixFileMode.UserWrite);entries.Add(entry);return entry;}
        catch{RecoveryRequired=true;throw;}
    }
}
