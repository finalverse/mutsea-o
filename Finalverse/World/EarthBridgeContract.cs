// Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
namespace Finalverse.World;

/// FSIP's first deterministic adapter only composes existing approved packages.
/// A trusted transport is not permission to skip native asset/parcel validation.
public static class EarthBridgeContract
{
    static bool Hash(string s)=>s.Length==64&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    static string Text(JsonElement e,string name,int max) {
        var s=e.GetProperty(name).GetString();ContentCatalog.Require(s is {Length:>0}&&s.Length<=max&&!s.Any(char.IsControl),"Invalid FSIP label.");return s!;
    }
    static void Fields(JsonElement e,params string[] names) => ContentCatalog.Require(e.ValueKind==JsonValueKind.Object&&e.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(names.Order()),"Unsupported FSIP fields.");
    public static PlaceTemplate Draft(JsonElement m,string expectedGrid,string expectedRegion,ContentCatalog catalog)
    {
        Fields(m,"protocol_version","manifest_version","experience_id","owner","title","source_workspace","world_template","transform","objects","semantic_zones","required_permissions","budget","provenance","ai_narrative","created_at");
        ContentCatalog.Require(m.GetProperty("protocol_version").GetString()=="0.1"&&m.GetProperty("manifest_version").GetInt32()==1,"Unsupported FSIP version.");
        ContentCatalog.Require(Guid.TryParse(Text(m,"experience_id",36),out var experience)&&experience!=Guid.Empty,"Invalid experience identity.");
        var owner=m.GetProperty("owner");Fields(owner,"earth_subject","mutsea_agent_id");ContentCatalog.Require(Hash(Text(owner,"earth_subject",64)),"Invalid Earth owner.");
        var t=m.GetProperty("transform");Fields(t,"layout","source_context","target","frame","description");var audience=t.GetProperty("target");Fields(audience,"grid_id","region_id");
        ContentCatalog.Require(audience.GetProperty("grid_id").GetString()==expectedGrid&&audience.GetProperty("region_id").GetString()==expectedRegion,"Experience belongs to another grid or region.");
        var frame=t.GetProperty("frame");Fields(frame,"frame_id","axes","metres_per_unit","origin","geo_anchor");
        ContentCatalog.Require(frame.GetProperty("axes").GetString()=="east_north_up"&&frame.GetProperty("metres_per_unit").GetDouble()==1&&frame.GetProperty("geo_anchor").ValueKind==JsonValueKind.Null,"Explicit unanchored metre frame required.");
        var origin=frame.GetProperty("origin").Deserialize<double[]>()!;
        ContentCatalog.Require(origin.Length==3&&origin.All(x=>x==0),"The native siting executor owns the target origin.");
        var layout=t.GetProperty("layout").GetString();
        var source=m.GetProperty("source_workspace");var provenance=m.GetProperty("provenance").EnumerateArray().ToArray();
        ContentCatalog.Require(provenance.Length is >0 and <=8,"Bounded provenance required.");
        var records=new HashSet<string>();
        foreach(var p in provenance){ContentCatalog.Require(Hash(Text(p,"sha256",64)),"Invalid provenance hash.");Text(p,"source",256);Text(p,"license",120);Text(p,"attribution",512);
            var ids=p.GetProperty("record_ids").EnumerateArray().ToArray();ContentCatalog.Require(ids.Length<=64,"Source record budget.");
            if(!p.GetProperty("generated").GetBoolean())foreach(var id in ids){var s=id.GetString();ContentCatalog.Require(s is {Length:>0 and <=128}&&!s.Any(char.IsControl),"Invalid source ID.");records.Add(s!);}}
        if(layout=="virtual") ContentCatalog.Require(source.ValueKind==JsonValueKind.Null&&t.GetProperty("source_context").ValueKind==JsonValueKind.Null&&records.Count==0&&provenance.All(p=>p.GetProperty("generated").GetBoolean()&&p.GetProperty("record_ids").GetArrayLength()==0),"Virtual geometry cannot claim Earth evidence.");
        else {
            ContentCatalog.Require(layout=="schematic_exhibition"&&source.ValueKind==JsonValueKind.Object&&records.Count>0,"Source-backed exhibition required.");
            ContentCatalog.Require(Hash(Text(source,"sha256",64))&&Guid.TryParse(Text(source,"session_id",36),out _)&&Guid.TryParse(Text(source,"artifact_id",36),out _),"Invalid source workspace.");
            var context=t.GetProperty("source_context");var anchor=context.GetProperty("anchor");
            ContentCatalog.Require(context.GetProperty("kind").GetString()=="geographic"&&anchor.GetProperty("crs").GetString()=="EPSG:4979"&&anchor.GetProperty("vertical_datum").GetString()=="WGS84_ELLIPSOID","Explicit source CRS required.");
            ContentCatalog.Vector([anchor.GetProperty("longitude_deg").GetDouble()],1,-180,180);ContentCatalog.Vector([anchor.GetProperty("latitude_deg").GetDouble()],1,-90,90);
            ContentCatalog.Vector([anchor.GetProperty("ellipsoidal_height_m").GetDouble()],1,-12000,100000000);
        }
        string[] scopes=["read_context","preview","create_owned_place","inspect_receipt","undo_owned_place"];
        ContentCatalog.Require(m.GetProperty("required_permissions").EnumerateArray().Select(s=>s.GetString()).SequenceEqual(scopes),"Unsupported world privileges.");
        var budget=m.GetProperty("budget");
        Fields(budget,"max_objects","max_triangles","max_asset_bytes","max_lights","scripts_allowed");
        int maxObjects=budget.GetProperty("max_objects").GetInt32(),maxTriangles=budget.GetProperty("max_triangles").GetInt32(),maxLights=budget.GetProperty("max_lights").GetInt32();
        long maxBytes=budget.GetProperty("max_asset_bytes").GetInt64();
        ContentCatalog.Require(maxObjects is >0 and <=256&&maxTriangles is >0 and <=65536&&maxLights is >=0 and <=12&&maxBytes is >0 and <=16777216&&!budget.GetProperty("scripts_allowed").GetBoolean(),"World budget or script permission refused.");
        var objects=m.GetProperty("objects").EnumerateArray().ToArray();ContentCatalog.Require(objects.Length is >0 and <=24,"Experience object budget.");
        int parts=0,triangles=0,lights=0;long bytes=0;var assets=new HashSet<string>();
        var placements=objects.Select(o=>{
            Fields(o,"local_id","asset","offset_m","yaw_degrees","semantic_type","description","source_record_ids","generated_geometry");
            var a=o.GetProperty("asset");Fields(a,"package_id","version","manifest_sha256","license");var packageId=Text(a,"package_id",120);var version=Text(a,"version",40);var compiled=catalog.Get(packageId,version);var p=compiled.package;
            ContentCatalog.Require(p.manifest_sha256==Text(a,"manifest_sha256",64)&&a.GetProperty("license").GetString()==p.license,"Unapproved or changed asset pin.");
            ContentCatalog.Require(o.GetProperty("generated_geometry").GetBoolean(),"Exhibition geometry must be identified as generated.");
            var sourceIds=o.GetProperty("source_record_ids").EnumerateArray().Select(s=>s.GetString()!).ToArray();
            ContentCatalog.Require(sourceIds.Length<=16&&sourceIds.All(records.Contains),"Fabricated source link refused.");
            parts+=p.parts.Length;triangles+=p.parts.Sum(x=>x.triangles);lights+=p.parts.Count(x=>x.light!=null);
            if(assets.Add(p.manifest_sha256))bytes+=compiled.meshes.Sum(x=>(long)x.Length)+compiled.materials.Sum(x=>(long)x.Length);
            var offset=o.GetProperty("offset_m").Deserialize<double[]>()!;ContentCatalog.Vector(offset,3,-28,28);ContentCatalog.Require(offset[2] is >=-2 and <=8,"Vertical extent refused.");
            double yaw=o.GetProperty("yaw_degrees").GetDouble();ContentCatalog.Vector([yaw],1,-360,360);
            Text(o,"description",512);
            return new PlacePlacement(Text(o,"local_id",80),packageId,version,p.manifest_sha256,offset,yaw,Text(o,"semantic_type",80));
        }).ToArray();
        ContentCatalog.Require(parts<=maxObjects&&triangles<=maxTriangles&&lights<=maxLights&&bytes<=maxBytes,"Actual compiled resource cost exceeds approved budget.");
        var draft=new PlaceTemplate(1,"place","user.fsip."+experience.ToString("N"),"0.1.0",Text(m,"title",120),
            "FSIP "+Text(t,"description",512),"CC0-1.0",[13d,13d],["fsip",layout!],["inspect","sit","walk","speak"],placements,[new PlaceEntrance("arrival",layout=="virtual"?[0d,-1d,4d]:[0d,-8d,0.5],0)]);
        PlaceCatalog.Validate(draft,catalog);return draft;
    }
}
