// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Finalverse.World;
public sealed record ContentFace(double[] color,bool fullbright,string? material_id=null);
public sealed record ContentMaterialFactors(string local_id,double[] base_color_linear,double metallic_factor,double roughness_factor,double[] emissive_linear,string alpha_mode,double alpha_cutoff,bool double_sided);
public sealed record ContentMaterial(string local_id,string asset_path,string asset_sha256,ContentMaterialFactors factors);
public sealed record ContentRelationship(string type,string target);
public sealed record ContentMetadata(string[] tags,string[] affordances,ContentRelationship[] relationships);
public sealed record ContentSeat(int capacity,double[] position,double[] rotation);
public sealed record ContentCollision(string shape,double[]? minimum=null,double[]? maximum=null);
public sealed record ContentLight(double[] color_linear,double intensity,double radius,double falloff);
public sealed record ContentPart(string local_id,string name,string semantic_type,string description,double[] position,double[] scale,double[] rotation,ContentFace[] faces,string mesh_path,string mesh_sha256,int triangles,ContentMetadata? semantics=null,ContentSeat? seat=null,ContentCollision? collision=null,ContentLight? light=null);
public sealed record ContentPackage(int schema_version,string package_id,string version,string title,string manifest_sha256,string license,ContentPart[] parts,string lod_policy,string collision_policy,ContentMaterial[]? materials=null,int[]? lod_triangles=null);
// yaw_degrees rotates the whole linked asset about its anchor (place placements); direct imports keep 0.
public sealed record ContentPlan(string plan_id,string actor_id,DateTimeOffset expires_at,ContentPackage package,double[] anchor,string[] entity_ids,string[] asset_ids,double yaw_degrees=0);
public sealed record ContentEntry(string operation_id,string status,DateTimeOffset timestamp,ContentPlan plan,string? fingerprint,string? error);
public interface IContentWorld
{
    void Check(ContentPlan plan);
    string Create(ContentPlan plan,byte[][] meshes,byte[][] materials);
    string? Fingerprint(ContentPlan plan);
    bool MatchesFingerprint(ContentPlan plan, string fingerprint) => Fingerprint(plan) == fingerprint;
    void CheckDelete(ContentPlan plan) { }
    void Delete(ContentPlan plan);
}

// The server operator supplies the read-only catalog. No request can upload a
// package, choose a filesystem path, expand this catalog or execute authoring code.
public sealed class ContentCatalog
{
    public static readonly JsonSerializerOptions Json=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    readonly Dictionary<string,(ContentPackage package,byte[][] meshes,byte[][] materials)> packages=new();
    public ContentCatalog(string root,Func<byte[],ContentMaterialFactors,bool>? validateNativeMaterial=null)
    {
        if(string.IsNullOrEmpty(root))return;
        SafeFile(root,true);
        foreach(var directory in Directory.GetDirectories(root))
        {
            SafeFile(directory,true);
            string path=Path.Combine(directory,"compiled.json");SafeFile(path,false);
            var package=JsonSerializer.Deserialize<ContentPackage>(Read(path,32768),Json)!;
            Validate(package);
            path=Path.Combine(directory,"package.json");SafeFile(path,false);var manifest=Read(path,32768);
            Require(Hash(manifest)==package.manifest_sha256,"Manifest hash mismatch.");
            using var doc=JsonDocument.Parse(manifest);var original=doc.RootElement;
            Require(original.GetProperty("kind").GetString()=="asset"&&original.GetProperty("package_id").GetString()==package.package_id&&original.GetProperty("version").GetString()==package.version&&original.GetProperty("license").GetProperty("spdx").GetString()==package.license,"Compiled package identity mismatch.");
            var semantics=original.GetProperty("entities").EnumerateArray().ToDictionary(e=>e.GetProperty("local_id").GetString()!);
            for(int i=0;i<package.parts.Length;i++)
            {
                var part=package.parts[i];
                Require(semantics.TryGetValue(part.local_id,out var e)&&e.GetProperty("name").GetString()==part.name&&e.GetProperty("semantic_type").GetString()==part.semantic_type&&e.GetProperty("description").GetString()==part.description,"Compiled semantics mismatch.");
                var metadata=new ContentMetadata(e.GetProperty("tags").Deserialize<string[]>()!,e.GetProperty("affordances").Deserialize<string[]>()!,e.GetProperty("relationships").Deserialize<ContentRelationship[]>(Json)!);
                // v1 catalogs predate the complete semantic adapter. Hydrate
                // only from their hash-pinned original manifest; new v2
                // packages must carry the same explicit metadata themselves.
                Require(package.schema_version==1&&part.semantics==null||JsonSerializer.Serialize(part.semantics)==JsonSerializer.Serialize(metadata),"Compiled affordances or relationships mismatch.");
                package.parts[i]=part with{semantics=metadata};
            }
            Validate(package);
            Require(original.GetProperty("budget").GetProperty("triangles").GetInt32()==package.parts.Sum(p=>p.triangles),"Triangle manifest mismatch.");
            if(package.lod_triangles is {} lodCounts)
            {
                var lods=original.GetProperty("definition").GetProperty("lods");
                Require(lodCounts.Skip(1).SequenceEqual(new[]{"medium","low","lowest"}.Select(level=>lods.GetProperty(level).GetProperty("triangles").GetInt32())),"LOD counts contradict the pinned manifest.");
            }
            if(package.schema_version==3)
            {
                var definition=original.GetProperty("definition");
                var declared=definition.GetProperty("materials").Deserialize<ContentMaterialFactors[]>(Json)!;
                Require(JsonSerializer.Serialize(package.materials!.Select(m=>m.factors))==JsonSerializer.Serialize(declared),"PBR factors contradict the pinned manifest.");
                var bindings=definition.GetProperty("material_bindings").Deserialize<Dictionary<string,string[]>>(Json)!;
                Require(bindings.Count==package.parts.Length&&package.parts.All(p=>bindings.TryGetValue(p.local_id,out var faces)&&faces.SequenceEqual(p.faces.Select(f=>f.material_id))),"PBR face bindings contradict the pinned manifest.");
                var seating=definition.TryGetProperty("seating",out var seats)?seats.Deserialize<Dictionary<string,ContentSeat>>(Json)!:new();
                Require(seating.Count==package.parts.Count(p=>p.seat!=null)&&package.parts.All(p=>p.seat==null?!seating.ContainsKey(p.local_id):seating.TryGetValue(p.local_id,out var seat)&&JsonSerializer.Serialize(p.seat)==JsonSerializer.Serialize(seat)),"Native seating contradicts the pinned manifest.");
                var collision=definition.TryGetProperty("collision",out var collisionMap)?collisionMap.Deserialize<Dictionary<string,ContentCollision>>(Json)!:new();
                Require(collision.Count==package.parts.Count(p=>p.collision!=null)&&package.parts.All(p=>p.collision==null?!collision.ContainsKey(p.local_id):collision.TryGetValue(p.local_id,out var shape)&&JsonSerializer.Serialize(p.collision)==JsonSerializer.Serialize(shape)),"Native collision contradicts the pinned manifest.");
                var lights=definition.TryGetProperty("lighting",out var lightMap)?lightMap.Deserialize<Dictionary<string,ContentLight>>(Json)!:new();
                Require(lights.Count==package.parts.Count(p=>p.light!=null)&&package.parts.All(p=>p.light==null?!lights.ContainsKey(p.local_id):lights.TryGetValue(p.local_id,out var light)&&JsonSerializer.Serialize(p.light)==JsonSerializer.Serialize(light)),"Native lighting contradicts the pinned manifest.");
            }
            var meshes=package.parts.Select(p=>{
                var file=Path.Combine(directory,p.mesh_path);SafeFile(file,false);var bytes=Read(file,2*1024*1024);
                Require(Hash(bytes)==p.mesh_sha256,"Mesh hash mismatch.");return bytes;
            }).ToArray();
            var materials=(package.materials??[]).Select(m=>{
                var file=Path.Combine(directory,m.asset_path);SafeFile(file,false);var bytes=Read(file,32768);
                Require(Hash(bytes)==m.asset_sha256&&validateNativeMaterial!=null&&validateNativeMaterial(bytes,m.factors),"PBR asset does not match its bounded native material contract.");return bytes;
            }).ToArray();
            Require(packages.TryAdd(package.package_id+"@"+package.version,(package,meshes,materials)),"Duplicate catalog identity.");
        }
    }
    public (ContentPackage package,byte[][] meshes,byte[][] materials) Get(string id,string version)=>packages.TryGetValue(id+"@"+version,out var p)?p:throw new InvalidDataException("Package is not in the operator-approved catalog.");
    public static void Validate(ContentPackage p)
    {
        Require(p!=null&&p.schema_version is 1 or 2 or 3&&p.license=="CC0-1.0"&&p.parts is {Length:>0 and <=32},"Only small original CC0 asset packages are enabled in this first adapter.");
        Require(p!.package_id?.Length is >0 and <=120&&p.version?.Length is >0 and <=40&&p.title?.Length is >0 and <=120&&IsHash(p.manifest_sha256),"Invalid package identity.");
        Require(p.parts.Select(x=>x.local_id).Distinct().Count()==p.parts.Length,"Duplicate local IDs.");
        Require(p.parts.Count(x=>x.light!=null)<=2,"At most two bounded native point lights per small asset.");
        Require(p.parts.Sum(x=>x.triangles) is >0 and <=4096,"Initial small-asset triangle budget exceeded.");
        if(p.lod_triangles is {} lodCounts)Require(lodCounts.Length==4&&lodCounts[0]==p.parts.Sum(x=>x.triangles)&&lodCounts.All(n=>n>0)&&lodCounts.Zip(lodCounts.Skip(1),(a,b)=>a>b).All(v=>v),"Invalid reduced LOD metrics.");
        Require(p.schema_version==3?p.materials is {Length:>0 and <=8}:p.materials==null,"Material assets require the explicit v3 contract.");
        foreach(var material in p.materials??[])
        {
            Require(material.local_id?.Length is >0 and <=120&&material.factors?.local_id==material.local_id,"Invalid material identity.");
            Require(p.materials!.Count(m=>m.local_id==material.local_id)==1,"Duplicate material identity.");
            Require(SafePayloadName(material.asset_path)&&IsHash(material.asset_sha256),"Invalid native material path/hash.");
            ValidateMaterial(material.factors!);
        }
        foreach(var part in p.parts)
        {
            Require(part.local_id?.Length is >0 and <=120&&part.name?.Length is >0 and <=120&&part.description?.Length is >0 and <=512&&part.semantic_type?.Length is >0 and <=80,"Missing semantic metadata.");
            Vector(part.position,3,-4,4);Vector(part.scale,3,.01,4);Vector(part.rotation,4,-1,1);
            Require(Math.Abs(part.rotation.Sum(x=>x*x)-1)<.001,"Invalid part quaternion.");
            Require(part.faces is {Length:>0 and <=8}&&part.triangles>0,"Invalid face budget.");
            foreach(var face in part.faces){Vector(face.color,4,0,1);Require(p.schema_version==3?p.materials!.Any(m=>m.local_id==face.material_id)&&!face.fullbright:face.material_id==null,"Unknown PBR material or unsupported legacy binding/fullbright.");}
            Require(part.mesh_path is {Length:>0 and <=80}&&!part.mesh_path.Contains('/')&&!part.mesh_path.Contains('\\')&&!part.mesh_path.Contains("..")&&IsHash(part.mesh_sha256),"Invalid mesh path/hash.");
            Require(p.schema_version==1||part.semantics!=null,"Complete semantic metadata is required in v2 packages.");
            if(part.seat is {} seat)
            {
                Require(p.schema_version==3&&seat.capacity==1&&part.semantic_type=="seating"&&part.semantics!.affordances.Contains("sit"),"Only an explicit single native seat is enabled.");
                Vector(seat.position,3,-2,2);Vector(seat.rotation,4,-1,1);
                Require(seat.position.Any(v=>v!=0)&&Math.Abs(seat.rotation.Sum(v=>v*v)-1)<.001,"Invalid native sit target.");
            }
            if(part.collision is {} collision)
            {
                Require(p.schema_version==3&&collision.shape is "box" or "none","Unsupported explicit collision shape.");
                Require(collision.shape!="none"||part!=p.parts[0],"A native linked root must retain a collision shape.");
                ValidateCollision(collision);
            }
            if(part.light is {} light)
            {
                Require(p.schema_version==3&&part.semantic_type is "lighting" or "wayfinding","Native lights require explicit lighting or wayfinding semantics in v3.");
                ValidateLight(light);
            }
            if(part.semantics is {} metadata)
            {
                Require(metadata.tags is {Length:<=64}&&metadata.tags.All(t=>t is {Length:>0 and <=64}&&!t.Any(char.IsControl)),"Invalid semantic tags.");
                Require(metadata.affordances is {Length:>0 and <=64}&&metadata.affordances.Distinct().Count()==metadata.affordances.Length&&metadata.affordances.All(a=>a is "inspect" or "sit" or "walk" or "enter" or "look_at" or "speak" or "interact" or "customize"),"Invalid semantic affordances.");
                Require(metadata.relationships is {Length:<=64}&&metadata.relationships.All(r=>r!=null&&r.type is {Length:>0 and <=64}&&!r.type.Any(char.IsControl)&&p.parts.Any(target=>target.local_id==r.target)),"Dangling or invalid semantic relationship.");
            }
        }
    }
    public static void ValidateMaterial(ContentMaterialFactors f)
    {
        Require(f!=null&&f.local_id?.Length is >0 and <=120,"Missing material identity.");
        Vector(f!.base_color_linear,4,0,1);Vector(f.emissive_linear,3,0,1);
        Vector([f.metallic_factor,f.roughness_factor,f.alpha_cutoff],3,0,1);
        Require(f.alpha_mode is "OPAQUE" or "MASK" or "BLEND","Unsupported material alpha mode.");
    }
    public static void ValidateCollision(ContentCollision c)
    {
        Require(c.shape is "box" or "none","Unsupported collision shape.");
        if(c.shape=="none")Require(c.minimum==null&&c.maximum==null,"Noncolliding parts cannot declare a hull.");
        else
        {
            Vector(c.minimum!,3,-.5,.5);Vector(c.maximum!,3,-.5,.5);
            Require(c.minimum!.Zip(c.maximum!,(low,high)=>high-low>=.001).All(v=>v),"Collision hull must have three positive extents.");
        }
    }
    public static void ValidateLight(ContentLight light)
    {
        Vector(light.color_linear,3,0,1);
        Vector([light.intensity],1,.01,1);
        Vector([light.radius],1,.1,4);
        Vector([light.falloff],1,0,2);
        Require(light.color_linear.Any(v=>v>0),"Native light must have a nonzero linear color.");
    }
    static bool SafePayloadName(string s)=>s is {Length:>0 and <=80}&&!s.Contains('/')&&!s.Contains('\\')&&!s.Contains("..");
    static void SafeFile(string path,bool directory){var info=directory?(FileSystemInfo)new DirectoryInfo(path):new FileInfo(path);Require(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)==0,"Missing or linked catalog file.");}
    static byte[] Read(string path,int limit){Require(new FileInfo(path).Length is >0&&new FileInfo(path).Length<=limit,"Catalog payload size exceeded.");return File.ReadAllBytes(path);}
    static bool IsHash(string s)=>s is {Length:64}&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static void Vector(double[] v,int length,double min,double max)=>Require(v!=null&&v.Length==length&&v.All(x=>double.IsFinite(x)&&x>=min&&x<=max),"Invalid bounded vector.");
    public static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
}

public sealed class ContentKernel
{
    readonly IContentWorld world;readonly ContentCatalog catalog;readonly string journal;
    readonly Dictionary<string,ContentPlan> pending=new();readonly List<ContentEntry> entries=new();
    public bool RecoveryRequired{get;private set;}
    public ContentKernel(IContentWorld world,ContentCatalog catalog,string directory,string region)
    {
        this.world=world;this.catalog=catalog;Directory.CreateDirectory(directory);
        journal=Path.Combine(directory,region+".content.worldline.jsonl");
        if(File.Exists(journal))foreach(var line in File.ReadLines(journal))entries.Add(JsonSerializer.Deserialize<ContentEntry>(line,ContentCatalog.Json)!);
        RecoveryRequired=entries.Any(e=>e.status=="recovery_required")||entries.Any(e=>e.status=="intent"&&!entries.Any(x=>x.operation_id==e.operation_id&&x.status!="intent"));
    }
    static string Id(string plan,int part,string kind)=>new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(plan+":"+kind+":"+part)).Take(16).ToArray()).ToString();
    public static bool Overlaps(double x,double y,double radius,double minX,double maxX,double minY,double maxY)=>x+radius>=minX&&x-radius<=maxX&&y+radius>=minY&&y-radius<=maxY;
    public void Invalidate(string actor){foreach(var id in pending.Where(x=>x.Value.actor_id==actor).Select(x=>x.Key).ToArray())pending.Remove(id);}
    public ContentPlan Preview(string id,string version,double[] anchor,string actor)
    {
        ContentCatalog.Require(!RecoveryRequired,"Content recovery required.");ContentCatalog.Require(Guid.TryParse(actor,out _),"Invalid authenticated actor.");
        ContentCatalog.Vector(anchor,3,0,4096);var p=catalog.Get(id,version).package;
        string plan=Guid.NewGuid().ToString();
        var preview=new ContentPlan(plan,actor,DateTimeOffset.UtcNow.AddMinutes(2),p,anchor.ToArray(),p.parts.Select((_,i)=>Id(plan,i,"entity")).ToArray(),p.parts.Select((_,i)=>Id(plan,i,"asset")).ToArray());
        world.Check(preview);
        foreach(var stale in pending.Where(x=>x.Value.expires_at<=DateTimeOffset.UtcNow||x.Value.actor_id==actor).Select(x=>x.Key).ToArray())pending.Remove(stale);
        ContentCatalog.Require(pending.Count<128,"Too many pending imports. Try again after approvals expire.");
        pending[plan]=preview;return preview;
    }
    public ContentEntry Commit(string id,string actor)
    {
        ContentCatalog.Require(!RecoveryRequired,"Content recovery required.");
        ContentCatalog.Require(pending.TryGetValue(id,out var plan)&&plan.actor_id==actor&&plan.expires_at>DateTimeOffset.UtcNow,"Approval is missing, expired or belongs to another session.");
        world.Check(plan!);pending.Remove(id);
        var entry=new ContentEntry(Guid.NewGuid().ToString(),"intent",DateTimeOffset.UtcNow,plan!,null,null);Append(entry);
        try {var assets=catalog.Get(plan!.package.package_id,plan.package.version);var fp=world.Create(plan!,assets.meshes,assets.materials);entry=entry with{status="committed",fingerprint=fp};Append(entry);return entry;}
        catch(Exception){
            // Unknown partial insertion is never blindly deleted or replayed.
            bool absent=false;
            try{absent=world.Fingerprint(plan!)==null;}catch{ /* Unobservable state is not confirmed absence. */ }
            RecoveryRequired=!absent;
            entry=entry with{status=absent?"failed":"recovery_required",error="Import failed. Inspect the recorded IDs and native scene before retrying."};Append(entry);throw;
        }
    }
    public ContentEntry Undo(string id,string actor)
    {
        ContentCatalog.Require(!RecoveryRequired,"Content recovery required.");
        var old=entries.LastOrDefault(e=>e.plan.plan_id==id&&e.plan.actor_id==actor&&e.status=="committed");
        ContentCatalog.Require(old!=null&&!entries.Any(e=>e.plan.plan_id==id&&e.status=="undone"),"No active import owned by this actor.");
        ContentCatalog.Require(world.MatchesFingerprint(old!.plan,old.fingerprint!),"Imported object changed; Undo refuses to erase later edits.");
        world.CheckDelete(old.plan);
        var entry=old with{operation_id=Guid.NewGuid().ToString(),timestamp=DateTimeOffset.UtcNow,status="intent"};Append(entry);
        try{world.Delete(old.plan);ContentCatalog.Require(world.Fingerprint(old.plan)==null,"Deletion was not confirmed.");entry=entry with{status="undone"};Append(entry);return entry;}
        catch(Exception){RecoveryRequired=true;Append(entry with{status="recovery_required",error="Undo could not be confirmed."});throw;}
    }
    public ContentEntry[] History(string actor)=>entries.Where(e=>e.plan.actor_id==actor).TakeLast(100).ToArray();
    public object? Semantic(string id,string actor)
    {
        var entry=entries.LastOrDefault(e=>e.status=="committed"&&e.plan.entity_ids.Contains(id)&&!entries.Any(x=>x.plan.plan_id==e.plan.plan_id&&x.status=="undone"));
        if(entry==null||world.Fingerprint(entry.plan)==null)return null;
        var i=Array.IndexOf(entry.plan.entity_ids,id);var part=entry.plan.package.parts[i];
        var metadata=part.semantics;
        var relationships=(metadata?.relationships??[]).Select(r=>new{r.type,target=entry.plan.entity_ids[Array.FindIndex(entry.plan.package.parts,p=>p.local_id==r.target)]}).ToArray();
        return new{entity_id=id,structure_id=entry.plan.entity_ids[0],part.local_id,part.semantic_type,part.description,tags=metadata?.tags??[],affordances=metadata?.affordances??[],relationships,seat=part.seat,light=part.light,semantic_metadata_available=metadata!=null,package_id=entry.plan.package.package_id,package_version=entry.plan.package.version,entry.plan.package.manifest_sha256,mesh_sha256=part.mesh_sha256,license=entry.plan.package.license,owner_id=entry.plan.actor_id,entry.operation_id,entry.timestamp,version=1,provenance="Operator-approved original Starter Library; no external authoring code executed"};
    }
    void Append(ContentEntry entry)
    {
        try{using var file=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read);var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry,ContentCatalog.Json)+"\n");file.Write(bytes);file.Flush(true);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(journal,UnixFileMode.UserRead|UnixFileMode.UserWrite);entries.Add(entry);}
        catch{RecoveryRequired=true;throw;}
    }
}
