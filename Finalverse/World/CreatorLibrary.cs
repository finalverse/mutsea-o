// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Finalverse.World;

public sealed record CreatorContent(int schema_version,string owner_id,string region_id,string source_plan_id,string title,string license,JsonElement recipe,string? parent_release);
public sealed record CreatorRelease(string release_id,DateTimeOffset created_at,CreatorContent content);
public sealed record CreatorListing(string release_id,string owner_id,string title,string license,long price_minor,string currency,bool published,bool checkout_enabled,DateTimeOffset updated_at);

// Immutable content-addressed recipes; not arbitrary native object/OAR uploads.
// Region journal storage is already file-based. This is metadata beside WorldLine,
// not a second identity database or an unimplemented payment processor.
public sealed class CreatorLibrary
{
    readonly string root;static readonly object Gate=new();
    public CreatorLibrary(string root){this.root=root;Directory.CreateDirectory(root);}
    public CreatorRelease Save(string actor,string region,string plan,string title,JsonElement recipe,string? parent=null)
    {
        Require(WorldLibrary.CanonicalRegion(actor)&&WorldLibrary.CanonicalRegion(region)&&WorldLibrary.CanonicalRegion(plan),"Invalid release identity.");
        Require(!string.IsNullOrWhiteSpace(title)&&title.Length<=80&&!title.Any(char.IsControl),"Use a title of 1–80 characters.");
        Require(recipe.ValueKind==JsonValueKind.Object&&recipe.GetRawText().Length<=60000&&recipe.GetProperty("license").GetString()=="CC0-1.0","Only validated CC0 Starter Library recipes can be released in this version.");
        lock(Gate)
        {
            if(parent!=null)
            {
                var source=Get(parent);
                Require(source.content.license=="CC0-1.0","Parent license cannot be remixed by this release version.");
            }
            parent??=Parent(actor,plan);
            var content=new CreatorContent(1,actor,region,plan,title,"CC0-1.0",recipe.Clone(),parent);
            string id=Hash(JsonSerializer.SerializeToUtf8Bytes(content));
            string target=Path.Combine(root,id+".release.json");
            if(File.Exists(target)){var existing=Get(id);Require(existing.content.owner_id==actor,"Release ownership differs.");return existing;}
            Require(Mine(actor).Length<100,"Creator release quota reached.");
            var release=new CreatorRelease(id,DateTimeOffset.UtcNow,content);
            Write(target,release,false);return release;
        }
    }
    public void LinkRemix(string actor,string plan,string parent)
    {
        Require(WorldLibrary.CanonicalRegion(actor)&&WorldLibrary.CanonicalRegion(plan),"Invalid remix identity.");
        Get(parent);
        lock(Gate)Write(Path.Combine(root,plan+".origin.json"),new{owner_id=actor,parent_release=parent},false);
    }
    string? Parent(string actor,string plan)
    {
        var file=new FileInfo(Path.Combine(root,plan+".origin.json"));if(!file.Exists)return null;
        Require(file.Length<=512&&(file.Attributes&FileAttributes.ReparsePoint)==0,"Invalid remix origin.");
        using var doc=JsonDocument.Parse(File.ReadAllBytes(file.FullName));var v=doc.RootElement;
        Require(v.GetProperty("owner_id").GetString()==actor,"Remix ownership changed.");
        return v.GetProperty("parent_release").GetString();
    }
    public CreatorRelease Get(string id)
    {
        Require(ValidId(id),"Invalid release ID.");
        var f=new FileInfo(Path.Combine(root,id+".release.json"));
        Require(f.Exists&&f.Length is >0 and <=262144&&(f.Attributes&FileAttributes.ReparsePoint)==0,"Release is unavailable.");
        var release=JsonSerializer.Deserialize<CreatorRelease>(File.ReadAllBytes(f.FullName));
        Require(release!=null&&release.release_id==id&&Hash(JsonSerializer.SerializeToUtf8Bytes(release.content))==id,"Release integrity check failed.");
        return release!;
    }
    public CreatorRelease[] Mine(string actor)=>Directory.EnumerateFiles(root,"*.release.json").Take(10001)
        .Select(p=>Get(Path.GetFileName(p).Split('.')[0])).Where(r=>r.content.owner_id==actor).OrderByDescending(r=>r.created_at).Take(101).ToArray();
    public CreatorListing Publish(string actor,string id,bool publish,long price,string currency)
    {
        lock(Gate)
        {
            var release=Get(id);Require(release.content.owner_id==actor,"Only the release owner may manage its listing.");
            Require(price is >=0 and <=100000000&&currency is "USD" or "CAD" or "EUR","Invalid offer price or currency.");
            // A priced listing is an offer, never a working purchase button.
            var listing=new CreatorListing(id,actor,release.content.title,release.content.license,price,currency,publish,false,DateTimeOffset.UtcNow);
            Write(Path.Combine(root,id+".listing.json"),listing,true);return listing;
        }
    }
    public CreatorListing[] Published()=>Directory.EnumerateFiles(root,"*.listing.json").Take(10001).Select(p=>{
        var f=new FileInfo(p);Require(f.Length is >0 and <=8192&&(f.Attributes&FileAttributes.ReparsePoint)==0,"Invalid listing.");
        var l=JsonSerializer.Deserialize<CreatorListing>(File.ReadAllBytes(p))!;
        var r=Get(l.release_id);Require(l.owner_id==r.content.owner_id&&!l.checkout_enabled,"Invalid listing authority.");return l;
    }).Where(l=>l.published).OrderByDescending(l=>l.updated_at).Take(256).ToArray();
    static bool ValidId(string id)=>id!=null&&Regex.IsMatch(id,"^[a-f0-9]{64}$");
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    void Write<T>(string target,T value,bool replace)
    {
        var temporary=Path.Combine(root,"."+Guid.NewGuid()+".tmp");
        try
        {
            using(var f=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(f,value);f.Flush(true);}
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temporary,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            File.Move(temporary,target,replace);
        }
        finally{File.Delete(temporary);}
    }
}
