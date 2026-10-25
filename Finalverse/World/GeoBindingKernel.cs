// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text;
using System.Text.Json;

namespace Finalverse.World;

// The region adapter for geographic binding issuance.
public interface IGeoRegion
{
    string RegionId{get;}
    double SizeX{get;}
    double SizeY{get;}
    bool HasLiveObjects();
    string TerrainHashFormat{get;}
    string TerrainHash();
    string SnapshotTerrain(string path);               // R32 snapshot; returns SHA-256
    byte[] TerrainR32();
    void LoadTerrain(byte[] r32);                      // apply, persist and push to clients
    double WaterHeight{get;}
    void SetWaterHeight(double height);
}

public sealed record GeoRecord(string operation_id,string kind,DateTimeOffset timestamp,string region_id,string binding_id,int revision,string version,
    string? previous_version,string? terrain_before_sha256=null,string? terrain_after_sha256=null,double? water_before=null,double? water_after=null,
    string? terrain_hash_format=null,string? error=null,string? actor_id=null,JsonElement? receipt=null);

public sealed record GeoActive(GeographicBinding binding,GeoTransformer transform,GeoCorrespondence correspondence,GeoRecord issued);

// Binding issuance as one journaled operation per revision: intent before any mutation,
// terrain and water snapshot, postcondition, and guarded compensation. Every issued version
// stays on disk (content-addressed) so history remains resolvable after it is superseded.
public sealed class GeoBindingKernel
{
    const long JournalLimit=32*1024*1024;
    readonly IGeoRegion region;readonly string journal,store,world;
    readonly List<GeoRecord> records=new();
    public bool RecoveryRequired{get;private set;}
    public IReadOnlyList<GeoRecord> Records=>records;
    public GeoActive? Active{get;private set;}

    public GeoBindingKernel(IGeoRegion region,string directory,string world)
    {
        this.region=region;this.world=world;Directory.CreateDirectory(directory);
        journal=Path.Combine(directory,region.RegionId+".geobinding.jsonl");store=Path.Combine(directory,region.RegionId+".geo");
        Directory.CreateDirectory(store);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(store,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        if(File.Exists(journal))foreach(var line in File.ReadLines(journal))records.Add(JsonSerializer.Deserialize<GeoRecord>(line,ContentCatalog.Json)!);
        RecoveryRequired=records.Any(r=>r.kind=="recovery_required")||records.Where(r=>r.kind.EndsWith("_intent",StringComparison.Ordinal)).GroupBy(r=>r.operation_id)
            .Any(g=>!records.Any(r=>r.operation_id==g.Key&&!r.kind.EndsWith("_intent",StringComparison.Ordinal)));
        var version=ActiveVersion();
        if(version!=null)Active=Load(version,records.Last(r=>r.kind=="issued"&&r.version==version));
    }

    // Issued versions form a stack: a revision pushes, retirement pops back to its predecessor.
    string? ActiveVersion()
    {
        var stack=new List<string>();
        foreach(var r in records)
        {
            if(r.kind=="issued")stack.Add(r.version);
            else if(r.kind=="retired"&&stack.Count>0&&stack[^1]==r.version)stack.RemoveAt(stack.Count-1);
        }
        return stack.Count==0?null:stack[^1];
    }

    GeoActive Load(string version,GeoRecord issued)
    {
        var binding=JsonSerializer.Deserialize<GeographicBinding>(Read(version+".binding.json",version,false),ContentCatalog.Json)!;
        ContentCatalog.Require(binding.version==version,"Stored binding differs from its journal.");
        var transform=GeoBindings.Validate(binding,region.RegionId,region.SizeX,region.SizeY);
        var index=JsonSerializer.Deserialize<GeoCorrespondenceIndex>(Read(version+".correspondence.json",binding.correspondence.sha256,true),ContentCatalog.Json)!;
        return new(binding,transform,new GeoCorrespondence(index,binding),issued);
    }

    byte[] Read(string name,string sha,bool digest)
    {
        var info=new FileInfo(Path.Combine(store,name));
        ContentCatalog.Require(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)==0&&info.Length<=64*1024*1024,"Stored geographic binding file "+name+" is missing.");
        var bytes=File.ReadAllBytes(info.FullName);
        if(digest)ContentCatalog.Require(ContentCatalog.Hash(bytes)==sha,"Stored geographic file "+name+" differs from its pin.");
        return bytes;
    }

    // Candidate files are operator-reviewed and private. Terrain is applied only when it differs
    // from the live heightmap, and then only into a region with no live objects to strand.
    public GeoRecord Issue(byte[] bindingBytes,byte[] correspondenceBytes,byte[] r32)
    {
        ContentCatalog.Require(!RecoveryRequired,"An interrupted geographic binding operation requires operator recovery.");
        var binding=JsonSerializer.Deserialize<GeographicBinding>(bindingBytes,ContentCatalog.Json)!;
        var transform=GeoBindings.Validate(binding,region.RegionId,region.SizeX,region.SizeY);
        ContentCatalog.Require(binding.world_id==world,"Binding belongs to another world.");
        if(Active?.binding.version==binding.version)return Active.issued;
        ContentCatalog.Require(binding.revision==(Active?.binding.revision??0)+1&&binding.previous_version==Active?.binding.version,"A revision must directly supersede the active binding.");
        ContentCatalog.Require(ContentCatalog.Hash(correspondenceBytes)==binding.correspondence.sha256,"Correspondence differs from its pin.");
        var index=JsonSerializer.Deserialize<GeoCorrespondenceIndex>(correspondenceBytes,ContentCatalog.Json)!;
        var correspondence=new GeoCorrespondence(index,binding);
        ContentCatalog.Require(ContentCatalog.Hash(r32)==binding.terrain.r32_sha256&&r32.Length==(long)region.SizeX*(long)region.SizeY*4,"Terrain differs from its pin or the region size.");
        for(int i=0;i<r32.Length;i+=4){var h=BitConverter.ToSingle(r32,i);ContentCatalog.Require(float.IsFinite(h)&&h>=binding.terrain.minimum_metres-.001&&h<=binding.terrain.maximum_metres+.001,"Terrain samples exceed their declared range.");}
        bool terrainChanges=!region.TerrainR32().AsSpan().SequenceEqual(r32);
        ContentCatalog.Require(!terrainChanges||!region.HasLiveObjects(),"Geographic terrain can only replace the ground of a region without live objects.");
        Store(binding.version+".binding.json",bindingBytes);Store(binding.version+".correspondence.json",correspondenceBytes);Store(binding.version+".terrain.r32",r32);

        string operation=Guid.NewGuid().ToString();
        string before=region.SnapshotTerrain(Path.Combine(store,operation+".before.r32"));double waterBefore=region.WaterHeight;
        var intent=Append(new(operation,"issue_intent",DateTimeOffset.UtcNow,region.RegionId,binding.binding_id,binding.revision,binding.version,binding.previous_version,before,null,waterBefore,null,region.TerrainHashFormat));
        try
        {
            if(terrainChanges)region.LoadTerrain(r32);
            region.SetWaterHeight(binding.terrain.water_height_metres);
            ContentCatalog.Require(region.TerrainR32().AsSpan().SequenceEqual(r32)&&Math.Abs(region.WaterHeight-binding.terrain.water_height_metres)<1e-6,"The region did not confirm the bound terrain and water.");
            var issued=Append(intent with{kind="issued",timestamp=DateTimeOffset.UtcNow,terrain_after_sha256=region.TerrainHash(),water_after=region.WaterHeight});
            Active=new(binding,transform,correspondence,issued);return issued;
        }
        catch(Exception ex)
        {
            bool clean=true;
            try{region.LoadTerrain(File.ReadAllBytes(Path.Combine(store,operation+".before.r32")));region.SetWaterHeight(waterBefore);clean=ContentCatalog.Hash(region.TerrainR32())==before;}catch{clean=false;}
            RecoveryRequired=!clean;
            Append(intent with{kind=clean?"issue_rolled_back":"recovery_required",timestamp=DateTimeOffset.UtcNow,error=ex.Message});
            throw;
        }
    }

    // Retiring restores the pre-issue ground, but never over terrain edited after issuance.
    public GeoRecord Retire(string version)
    {
        ContentCatalog.Require(!RecoveryRequired&&Active!=null&&Active.binding.version==version,"Only the active binding can be retired.");
        var issued=Active!.issued;
        ContentCatalog.Require(issued.terrain_hash_format==region.TerrainHashFormat&&region.TerrainHash()==issued.terrain_after_sha256,"Terrain changed after issuance; retirement preserves later edits and needs operator review.");
        var snapshot=Path.Combine(store,issued.operation_id+".before.r32");
        ContentCatalog.Require(File.Exists(snapshot)&&ContentCatalog.Hash(File.ReadAllBytes(snapshot))==issued.terrain_before_sha256,"Pre-issue terrain snapshot is missing or changed.");
        var intent=Append(issued with{operation_id=Guid.NewGuid().ToString(),kind="retire_intent",timestamp=DateTimeOffset.UtcNow,error=null});
        try
        {
            region.LoadTerrain(File.ReadAllBytes(snapshot));region.SetWaterHeight(issued.water_before??region.WaterHeight);
            var retired=Append(intent with{kind="retired",timestamp=DateTimeOffset.UtcNow});
            var previous=ActiveVersion();
            Active=previous==null?null:Load(previous,records.Last(r=>r.kind=="issued"&&r.version==previous));
            return retired;
        }
        catch(Exception ex){RecoveryRequired=true;Append(intent with{kind="recovery_required",timestamp=DateTimeOffset.UtcNow,error=ex.Message});throw;}
    }

    // Live terrain belongs to MutSea and may be edited after issuance; report it, never hide it.
    public bool TerrainMatchesIssued()=>Active!=null&&Active.issued.terrain_hash_format==region.TerrainHashFormat&&region.TerrainHash()==Active.issued.terrain_after_sha256;

    // Arrival evidence for a session that entered through discovery. The receipt is checked
    // by the caller against the native session; it carries no authority of its own.
    public GeoRecord RecordEntry(string actor,object receipt)
    {
        ContentCatalog.Require(Active!=null,"No active geographic binding.");
        var since=DateTimeOffset.UtcNow.AddHours(-1);
        ContentCatalog.Require(records.Count(r=>r.kind=="entry"&&r.actor_id==actor&&r.timestamp>since)<60,"Too many world entries this hour.");
        var b=Active!.binding;
        return Append(new(Guid.NewGuid().ToString(),"entry",DateTimeOffset.UtcNow,region.RegionId,b.binding_id,b.revision,b.version,b.previous_version,actor_id:actor,receipt:JsonSerializer.SerializeToElement(receipt)));
    }

    public object History()=>records.Where(r=>r.kind is "issued" or "retired" or "issue_rolled_back" or "recovery_required")
        .Select(r=>new{r.kind,r.revision,r.version,r.previous_version,r.timestamp}).ToArray();

    void Store(string name,byte[] bytes)
    {
        var path=Path.Combine(store,name);
        if(File.Exists(path)){ContentCatalog.Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes),"A different file already holds this binding version.");return;}
        using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write)){f.Write(bytes);f.Flush(true);}
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }

    GeoRecord Append(GeoRecord record)
    {
        try
        {
            var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record,ContentCatalog.Json)+"\n");
            ContentCatalog.Require((File.Exists(journal)?new FileInfo(journal).Length:0)+bytes.Length<=JournalLimit,"Geographic binding journal needs operator maintenance.");
            using(var f=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read)){f.Write(bytes);f.Flush(true);}
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(journal,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            records.Add(record);return record;
        }
        catch(Exception ex) when(ex is not InvalidDataException){RecoveryRequired=true;throw;}
    }
}
