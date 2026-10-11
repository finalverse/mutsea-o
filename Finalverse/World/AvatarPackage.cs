// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;

namespace Finalverse.World;

// A compiled Genesis representation: one skinned native mesh bound to the
// pinned viewer skeleton, rendered as a native animated object. It reuses the
// small-asset material contract but has its own explicit avatar bounds.
public sealed record AvatarFace(string material_id,double[] color);
public sealed record AvatarAnimationAsset(string local_id,string asset_path,string asset_sha256,double duration,bool loop);
public sealed record AvatarCompiled(int schema_version,string kind,string package_id,string version,string title,string manifest_sha256,
    string license,string representation,string skeleton_sha256,int joints,double root_height,double[] scale,string mesh_path,
    string mesh_sha256,int[] lod_triangles,AvatarFace[] faces,ContentMaterial[] materials,Dictionary<string,string> animations,
    ContentCollision collision,string lod_policy,AvatarAnimationAsset[]? animation_assets=null,
    string? attachment_point=null,string? alpha_asset_path=null,string? alpha_asset_sha256=null);

public static class AvatarCatalog
{
    public const string SkeletonSha256="adf352f0f4a65f19cbfd4665fc71c0450bc0a23a489ac6eb52987e343f221e19";
    public const int MaxJoints=110,MaxHighTriangles=20000,MaxAttachmentTriangles=40000,MaxMeshBytes=4<<20;
    // Worn humanoids hide the system body with a generated alpha layer on these native attach points.
    public static readonly IReadOnlySet<string> AttachmentPoints=new HashSet<string>{"Pelvis","Chest","Spine"};
    // Retired provisional templates (package id → successor). Existing inventory is left as is;
    // they are never provisioned or shown again, and wearers move to the successor by assignment.
    public static readonly IReadOnlyDictionary<string,string> Retired=new Dictionary<string,string>{
        ["finalverse.genesis.elara-male"]="finalverse.genesis.auren"};
    public static void RequireCurrent(AvatarCompiled a)=>
        ContentCatalog.Require(!Retired.TryGetValue(a.package_id,out var next),"Template "+a.package_id+" is retired; use "+next+".");
    // Native library animations only; package-supplied motion assets are a later contract.
    public static readonly IReadOnlyDictionary<string,string> NativeAnimations=new Dictionary<string,string>{
        ["idle"]="2408fe9e-df1d-1d7d-f4ff-1384fa7b350f",["walk"]="6ed24bd8-91aa-4b12-ccc7-c97c857ab4e0",
        ["turn_left"]="56e0ba0d-4a9f-7f27-6117-32f2ebbf6135",["turn_right"]="2d6daa51-3192-6794-8e2e-a15f8338ec30",
        ["greet"]="9b29cd61-c45b-5689-ded2-91756b8d76a9",["talk"]="5c682a95-6da4-a463-0bf6-0f5b7be129d1",
        ["sit"]="1a5fe8ac-a804-8a5d-7cbd-56bd83184568"};

    public static void Validate(AvatarCompiled a)
    {
        ContentCatalog.Require(a is {schema_version:1 or 2,kind:"avatar",representation:"animated_object" or "attachment",license:"CC0-1.0"},"Only original CC0 animated-object or worn avatars are enabled.");
        bool worn=a.representation=="attachment";
        ContentCatalog.Require(a.package_id is {Length:>0 and <=80}&&a.version is {Length:>0 and <=32}&&a.title is {Length:>0 and <=63},"Invalid avatar identity.");
        ContentCatalog.Require(a.skeleton_sha256==SkeletonSha256&&a.joints is >0 and <=MaxJoints,"Avatar must bind the pinned native skeleton within the per-mesh joint limit.");
        ContentCatalog.Require(double.IsFinite(a.root_height)&&a.root_height is >=.05 and <=2,"Invalid avatar root height.");
        ContentCatalog.Vector(a.scale,3,.05,4);
        ContentCatalog.Require(SafeName(a.mesh_path)&&IsHash(a.mesh_sha256)&&IsHash(a.manifest_sha256),"Invalid avatar mesh payload reference.");
        ContentCatalog.Require(a.lod_triangles is {Length:4}&&a.lod_triangles[0]<=(worn?MaxAttachmentTriangles:MaxHighTriangles)&&a.lod_triangles[3]>0
            &&a.lod_triangles.Zip(a.lod_triangles.Skip(1)).All(p=>p.First>p.Second),"Avatar LODs must strictly reduce within budget.");
        ContentCatalog.Require(a.materials is {Length:>0 and <=8}&&a.faces is {Length:>0 and <=8}&&a.faces.Length==a.materials.Length,"Avatar needs one PBR material per face (max 8).");
        foreach(var m in a.materials){ContentCatalog.ValidateMaterial(m.factors);ContentCatalog.Require(m.local_id==m.factors.local_id&&SafeName(m.asset_path)&&IsHash(m.asset_sha256),"Invalid avatar material.");}
        for(int i=0;i<a.faces.Length;i++){ContentCatalog.Require(a.faces[i].material_id==a.materials[i].local_id,"Avatar face/material order mismatch.");ContentCatalog.Vector(a.faces[i].color,4,0,1);}
        ContentCatalog.Require(a.schema_version is 1 or 2&&a.animations!=null,"Unknown avatar schema.");
        if(worn)
        {
            // A worn avatar is animated by its wearer's own agent; it carries an alpha layer instead.
            ContentCatalog.Require(a.schema_version==1&&a.animations!.Count==0&&a.animation_assets==null&&a.collision.shape=="none","Worn avatars carry no motions or collision of their own.");
            ContentCatalog.Require(a.attachment_point!=null&&AttachmentPoints.Contains(a.attachment_point)&&a.alpha_asset_path!=null&&SafeName(a.alpha_asset_path)&&a.alpha_asset_sha256!=null&&IsHash(a.alpha_asset_sha256),"Worn avatars need a native attach point and an alpha layer.");
            return;
        }
        ContentCatalog.Require(a.attachment_point==null&&a.alpha_asset_path==null,"Only worn avatars carry attachment data.");
        if(a.schema_version==1)
            ContentCatalog.Require(a.animation_assets==null&&a.animations!.Count==NativeAnimations.Count&&NativeAnimations.All(kv=>a.animations.GetValueOrDefault(kv.Key)==kv.Value),"Avatar animations must map every role to its native library animation.");
        else
        {
            // v2: each required role is its native library motion or a validated package motion; listen is optional.
            var assets=a.animation_assets??[];
            ContentCatalog.Require(assets.Length is >0 and <=8&&assets.Select(x=>x.local_id).Distinct().Count()==assets.Length,"Package motions must be 1-8 unique assets.");
            foreach(var x in assets)ContentCatalog.Require(SafeName(x.local_id)&&SafeName(x.asset_path)&&IsHash(x.asset_sha256)&&double.IsFinite(x.duration)&&x.duration is >0 and <=30,"Invalid package motion.");
            ContentCatalog.Require(NativeAnimations.Keys.All(a.animations!.ContainsKey)&&a.animations.Keys.All(r=>NativeAnimations.ContainsKey(r)||r=="listen"),"Avatar animations must cover every required role and no unknown role.");
            foreach(var (role,value) in a.animations)
                ContentCatalog.Require(value.StartsWith("package:")?assets.Any(x=>"package:"+x.local_id==value):NativeAnimations.GetValueOrDefault(role)==value,"Role "+role+" must use its native library motion or a declared package motion.");
            ContentCatalog.Require(assets.All(x=>a.animations.ContainsValue("package:"+x.local_id)),"Every package motion must be bound to a role.");
        }
        // Normalized to the mesh domain, exactly like small-asset hulls.
        ContentCatalog.ValidateCollision(a.collision);
        ContentCatalog.Require(worn||a.collision.shape=="box","Avatar collision must be a bounded box.");
    }

    public static (AvatarCompiled package,byte[] mesh,byte[][] materials,byte[][] motions) Load(string directory)=>Load(directory,out _);
    public static (AvatarCompiled package,byte[] mesh,byte[][] materials,byte[][] motions) Load(string directory,out byte[]? alpha)
    {
        var root=new DirectoryInfo(directory);
        ContentCatalog.Require(root.Exists&&(root.Attributes&FileAttributes.ReparsePoint)==0,"Missing or linked avatar directory.");
        byte[] Read(string name,int limit)
        {
            var info=new FileInfo(Path.Combine(root.FullName,name));
            ContentCatalog.Require(SafeName(name)&&info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)==0&&info.Length is >0&&info.Length<=limit,"Missing, linked or oversized avatar payload.");
            return File.ReadAllBytes(info.FullName);
        }
        var package=JsonSerializer.Deserialize<AvatarCompiled>(Read("compiled.json",1<<16),ContentCatalog.Json)!;
        Validate(package);
        var mesh=Read(package.mesh_path,MaxMeshBytes);
        ContentCatalog.Require(ContentCatalog.Hash(mesh)==package.mesh_sha256,"Avatar mesh hash changed.");
        var materials=package.materials.Select(m=>{var b=Read(m.asset_path,1<<16);ContentCatalog.Require(ContentCatalog.Hash(b)==m.asset_sha256&&ContentMaterialAsset.Matches(b,m.factors),"Avatar material changed.");return b;}).ToArray();
        var motions=(package.animation_assets??[]).Select(x=>{var b=Read(x.asset_path,NativeKeyframe.MaxBytes);ContentCatalog.Require(ContentCatalog.Hash(b)==x.asset_sha256,"Avatar motion changed.");
            var summary=NativeKeyframe.Validate(b);ContentCatalog.Require(Math.Abs(summary.duration-x.duration)<1e-3&&summary.loop==x.loop,"Avatar motion metadata differs from its payload.");return b;}).ToArray();
        alpha=null;
        if(package.alpha_asset_path!=null){alpha=Read(package.alpha_asset_path,1<<16);ContentCatalog.Require(ContentCatalog.Hash(alpha)==package.alpha_asset_sha256,"Avatar alpha layer changed.");}
        return(package,mesh,materials,motions);
    }

    static bool SafeName(string s)=>s is {Length:>0 and <=80}&&!s.Contains('/')&&!s.Contains('\\')&&!s.Contains("..");
    static bool IsHash(string s)=>s is {Length:64}&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
