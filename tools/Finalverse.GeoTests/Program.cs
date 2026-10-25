// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Geographic binding conformance. Usage: GeoTests [<candidate prefix> <publication dir> <oracle-verify.json>]
// With a candidate the real Old Montréal binding is also verified; without one those tests are NOT RUN.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Finalverse.World;

int passed=0,notRun=0;
void Test(string name,Action run){run();passed++;Console.WriteLine("PASS "+name);}
void Check(bool value,string why="Assertion failed"){if(!value)throw new Exception(why);}
void Reject(Action run,string? contains=null){try{run();}catch(InvalidDataException ex){if(contains!=null&&!ex.Message.Contains(contains))throw new Exception("Rejected for another reason: "+ex.Message);return;}throw new Exception("Expected policy rejection");}
string Hex(char c)=>new(c,64);
const string W="2d9b7e3a-41c6-4f0e-9a51-6c7f3b2e8d14",R="b5e1c0a7-3f2d-4e8b-9c6a-7d1f0e2b4a39",G="c4a1e6f2-7b3d-4a9e-8f1c-2e5d6b7a8c90",E="e7f2a1b4-6c3d-4e8f-9a0b-1c2d3e4f5a6b";

// ---- geodesy: SpatialDB golden vectors and PROJ 9.5 (pyproj 3.7.2, EPSG:4979 -> 4978) vectors
Test("equator, pole and south-pole golden vectors (SpatialDB frames.rs)",()=>{
    var e=GeoFrames.Ecef(new(0,0,0));Check(Math.Abs(e[0]-GeoFrames.A)<1e-9&&Math.Abs(e[1])<1e-9&&Math.Abs(e[2])<1e-9);
    var p=GeoFrames.Ecef(new(0,90,0));Check(Math.Abs(p[2]-GeoFrames.A*(1-GeoFrames.F))<1e-6);
    var s=GeoFrames.FromEcef([0,0,-6356752.314245]);Check(s.latitude_degrees==-90&&Math.Abs(s.height_metres)<1e-3);
});
Test("PROJ cross-implementation vectors to 1 µm",()=>{
    double[][] v=[[-73.55350350168884,45.507201203216475,-12.43081833422184,1267682.6607227703,-4294346.638810455,4527021.27767027],[0,0,0,6378137.0,0.0,0.0],
        [179.9999,-0.5,100,-6377995.761973645,11.131702571957252,-55287.322933296266],[-179.9999,0.5,-100,-6377795.769589033,-11.131353519398228,55285.57762619659],
        [120,-89.9,8848,-5592.417417993776,9686.35110509838,-6365590.553633043],[-73.5673,45.5017,35786000,8362188.920218063,-28352528.29030602,30051726.416127473],
        [10,60,-500,3148287.1824553437,555127.9732855109,5500044.121236746],[0,90,0,3.9186209248144716e-10,0.0,6356752.314245179]];
    foreach(var x in v){var e=GeoFrames.Ecef(new(x[0],x[1],x[2]));Check(GeoFrames.Distance(e,[x[3],x[4],x[5]])<1e-6,"PROJ mismatch at "+x[1]+","+x[0]);
        var g=GeoFrames.FromEcef([x[3],x[4],x[5]]);Check(Math.Abs(g.height_metres-x[2])<1e-6&&Math.Abs(g.latitude_degrees-x[1])<1e-11&&(x[1]==90||Math.Abs(g.longitude_degrees-x[0])<1e-11));}
});
Test("geodetic round trips below a millimetre everywhere that matters (SpatialDB grid)",()=>{
    foreach(var lat in new[]{-89.9,-60,-45.5017,0,45.5017,60,89.9})foreach(var lon in new[]{-179.9,-73.5673,0,120,179.9})foreach(var h in new[]{-500,0,28,8848,3578600d})
    {var p=new Geodetic(lon,lat,h);var back=GeoFrames.FromEcef(GeoFrames.Ecef(p));Check(GeoFrames.Distance(GeoFrames.Ecef(back),GeoFrames.Ecef(p))<1e-3&&Math.Abs(back.height_metres-h)<1e-3);}
});
Test("tangent frame origin and axes on the ellipsoid (SpatialDB east-up-south)",()=>{
    var o=new Geodetic(-73.554,45.5088,28);var m=GeoBindings.PublicationToEcef(o);
    Check(GeoFrames.Distance(GeoFrames.Apply(m,[0,0,0]),GeoFrames.Ecef(o))<1e-6);
    Check(Math.Abs(GeoFrames.FromEcef(GeoFrames.Apply(m,[0,100,0])).height_metres-128)<1e-6);
    Check(GeoFrames.FromEcef(GeoFrames.Apply(m,[0,0,1000])).latitude_degrees<o.latitude_degrees&&GeoFrames.FromEcef(GeoFrames.Apply(m,[1000,0,0])).longitude_degrees>o.longitude_degrees);
});

// ---- binding transforms
var montreal=new Geodetic(-73.5577,45.5058,-31.5);
Test("heading 0: +Y is north, +X is east; heading 90: +Y is east, +X is south",()=>{
    var t0=Transformer(montreal,0,1);var o=t0.Geographic([0,0,0]);
    var y=t0.Geographic([0,100,0]);var x=t0.Geographic([100,0,0]);
    Check(y.latitude_degrees>o.latitude_degrees&&Math.Abs(y.longitude_degrees-o.longitude_degrees)<1e-9&&x.longitude_degrees>o.longitude_degrees&&Math.Abs(x.latitude_degrees-o.latitude_degrees)<1e-6);
    var t90=Transformer(montreal,90,1);y=t90.Geographic([0,100,0]);x=t90.Geographic([100,0,0]);
    Check(y.longitude_degrees>o.longitude_degrees&&Math.Abs(y.latitude_degrees-o.latitude_degrees)<1e-6&&x.latitude_degrees<o.latitude_degrees&&Math.Abs(x.longitude_degrees-o.longitude_degrees)<1e-9);
});
Test("heading is the clockwise bearing of +Y measured at the anchor, for any heading",()=>{
    foreach(var h in new[]{0,19.315532505681574,45,90,135,180,225,270,359.999})
    {
        var t=Transformer(montreal,h,1);var a=GeoFrames.Ecef(montreal);var e=GeoFrames.Enu(montreal.longitude_degrees,montreal.latitude_degrees);
        var d=Sub(t.Ecef([0,1000,0]),a);double east=Dot(d,e[0]),north=Dot(d,e[1]);
        double bearing=(GeoFrames.Deg(Math.Atan2(east,north))+360)%360;Check(Math.Min(Math.Abs(bearing-h),360-Math.Abs(bearing-h))<1e-8,"bearing "+bearing+" for heading "+h);
        var dx=Sub(t.Ecef([1000,0,0]),a);double xb=(GeoFrames.Deg(Math.Atan2(Dot(dx,e[0]),Dot(dx,e[1])))-h-90+720)%360;Check(Math.Min(xb,360-xb)<1e-8,"+X must be 90 degrees clockwise of +Y");
    }
});
Test("scale: one region metre is `scale` Earth metres in every direction",()=>{
    foreach(var s in new[]{.5,1,2.5}){var t=Transformer(montreal,33,s);var p=new[]{10.0,20,5};var q=new[]{310.0,-40,55};
        Check(Math.Abs(GeoFrames.Distance(t.Ecef(p),t.Ecef(q))-s*GeoFrames.Distance(p,q))<1e-7);}
});
Test("region <-> WGS84 round trips at hard places: antimeridian, near pole, south, high and low",()=>{
    var rng=new Random(7);
    foreach(var anchor in new[]{montreal,new Geodetic(179.999,-0.3,0),new Geodetic(-179.9995,65,12),new Geodetic(12,89.95,2800),new Geodetic(150,-45,-430),new Geodetic(0,0,0)})
    foreach(var heading in new[]{0,19.3,181.5,359.5})
    {
        var t=Transformer(anchor,heading,1);
        for(int i=0;i<200;i++){double[] p=[rng.NextDouble()*512,rng.NextDouble()*512,rng.NextDouble()*400-50];Check(GeoFrames.Distance(t.Local(t.Geographic(p)),p)<1e-6,"round trip at "+anchor);}
    }
});
Test("inverse similarity is exact and the publication composition commutes",()=>{
    var m=GeoBindings.RegionToEcef(montreal,19.3,1.7);var i=GeoFrames.Multiply(GeoFrames.InverseSimilarity(m),m);
    for(int k=0;k<16;k++)Check(Math.Abs(i[k]-(k%5==0?1:0))<1e-9);
    var t=Transformer(montreal,19.3,1);double[] p=[123,456,7];
    Check(GeoFrames.Distance(GeoFrames.Apply(GeoBindings.PublicationToEcef(t.Binding.publication.frame_origin),t.Publication(p)),t.Ecef(p))<1e-6);
    Check(GeoFrames.Distance(t.LocalFromPublication(t.Publication(p)),p)<1e-9);
});
Test("live ground resolution follows the vertical through the geographic point",()=>{
    var t=Transformer(montreal,19.3,1);var target=t.Geographic([300,200,0]);
    var local=t.OnGround(target.latitude_degrees,target.longitude_degrees,(x,y)=>10+.05*x);
    var g=t.Geographic(local);Check(Math.Abs(local[2]-(10+.05*local[0]))<1e-5);
    Check(Math.Abs(g.latitude_degrees-target.latitude_degrees)<1e-12&&Math.Abs(g.longitude_degrees-target.longitude_degrees)<1e-12);
});

// ---- canonical identity and versions
Test("RFC 8785 number serialization vectors",()=>{
    (double,string)[] v=[(0,"0"),(-0.0,"0"),(1,"1"),(-1.5,"-1.5"),(4.5,"4.5"),(0.002,"0.002"),(1e-7,"1e-7"),(0.000001,"0.000001"),(1e21,"1e+21"),(1e20,"100000000000000000000"),
        (5e-324,"5e-324"),(1.7976931348623157e308,"1.7976931348623157e+308"),(9007199254740992,"9007199254740992"),(295147905179352830000d,"295147905179352830000"),
        (333333333.3333333,"333333333.3333333"),(1e-27,"1e-27"),(123e-20,"1.23e-18"),(19.315532505681574,"19.315532505681574")];
    foreach(var (d,s) in v)Check(CanonicalJson.Number(d)==s,d+" -> "+CanonicalJson.Number(d));
});
Test("canonical JSON sorts names, escapes controls and keeps non-ASCII",()=>{
    var e=JsonDocument.Parse("{\"b\":1,\"a\":[true,null,\"é\\u0001\\n\"],\"aa\":{\"z\":2.50,\"y\":-0}}").RootElement;
    Check(Encoding.UTF8.GetString(CanonicalJson.Bytes(e))=="{\"a\":[true,null,\"é\\u0001\\n\"],\"aa\":{\"y\":0,\"z\":2.5},\"b\":1}");
    Reject(()=>CanonicalJson.Bytes(JsonDocument.Parse("{\"a\":1,\"a\":2}").RootElement));
});
Test("binding version is deterministic, order-independent and content-bound",()=>{
    var b=Synthetic(montreal,19.3,1).binding;Check(GeoBindings.ComputeVersion(b)==b.version&&GeoBindings.Seal(b).version==b.version);
    var node=JsonSerializer.SerializeToNode(b)!.AsObject();var reordered=new JsonObject();foreach(var kv in node.Reverse().ToArray()){node.Remove(kv.Key);reordered[kv.Key]=kv.Value;}
    var parsed=JsonSerializer.Deserialize<GeographicBinding>(reordered.ToJsonString(),ContentCatalog.Json)!;Check(GeoBindings.ComputeVersion(parsed)==b.version);
    Check(GeoBindings.ComputeVersion(b with{transform=b.transform with{heading_degrees=19.3000001}})!=b.version);
    Check(GeoBindings.ComputeVersion(b with{entry=b.entry with{position=[256,256.001,20]}})!=b.version);
});
Test("binding identity is stable per world and region and differs otherwise",()=>{
    Check(GeoBindings.BindingId(W,R)==GeoBindings.BindingId(W.ToUpperInvariant(),R)&&GeoBindings.BindingId(W,R)!=GeoBindings.BindingId(W,E)&&Guid.TryParse(GeoBindings.BindingId(W,R),out var g)&&g.ToString()[14]=='5');
});

// ---- validation rejects every inconsistent or forged binding
Test("validation accepts a sealed binding and rejects each tampering",()=>{
    var b=Synthetic(montreal,19.3,1).binding;GeoBindings.Validate(b,R,512,512);
    Reject(()=>GeoBindings.Validate(b with{transform=b.transform with{heading_degrees=20}},R,512,512),"matrices disagree");
    Reject(()=>GeoBindings.Validate(GeoBindings.Seal(b with{transform=b.transform with{heading_degrees=20}}),R,512,512),"matrices disagree");
    Reject(()=>GeoBindings.Validate(b with{revision=2},R,512,512),"revision");
    Reject(()=>GeoBindings.Validate(b,E,512,512),"identity");
    Reject(()=>GeoBindings.Validate(b,R,256,256),"size");
    Reject(()=>GeoBindings.Validate(b with{binding_id=GeoBindings.BindingId(W,E)},R,512,512),"identity");
    Reject(()=>GeoBindings.Validate(b with{version=Hex('f')},R,512,512),"version");
    Reject(()=>GeoBindings.Validate(b with{publication=b.publication with{address="usap://spatialdb/entity/"+G}},R,512,512),"publication");
    Reject(()=>GeoBindings.Validate(b with{publication=b.publication with{frame_axes=["east","north","up"]}},R,512,512),"publication");
    var moved=b.calibration.control_points.Select((p,i)=>i==1?p with{region_local=[p.region_local[0]+.002,p.region_local[1],p.region_local[2]]}:p).ToArray();
    Reject(()=>GeoBindings.Validate(GeoBindings.Seal(b with{calibration=b.calibration with{control_points=moved}}),R,512,512),"1 mm");
    Reject(()=>GeoBindings.Validate(GeoBindings.Seal(b with{calibration=b.calibration with{control_points=b.calibration.control_points.Where(p=>p.role!="region_centre").ToArray()}}),R,512,512));
    Reject(()=>GeoBindings.Validate(GeoBindings.Seal(b with{terrain=b.terrain with{water_height_metres=50}}),R,512,512),"Terrain");
    Reject(()=>GeoBindings.Validate(GeoBindings.Seal(b with{attribution=[]}),R,512,512),"attribution");
    Reject(()=>GeoBindings.Validate(GeoBindings.Seal(b with{entry=b.entry with{position=[600,10,20]}}),R,512,512));
});
Test("heading 360 and non-positive scale are outside the contract",()=>{
    Reject(()=>Synthetic(montreal,360,1));Reject(()=>Synthetic(montreal,10,0));Reject(()=>Synthetic(montreal,-1,1));
});
Test("Earth Core projections carry exactly the validated members",()=>{
    var b=Synthetic(montreal,19.3,1).binding;
    var core=JsonSerializer.SerializeToElement(GeoBindings.CoreBinding(b));
    Check(string.Join(",",core.EnumerateObject().Select(p=>p.Name).Order(StringComparer.Ordinal))=="address,anchor,authority,frame,heading_degrees,region_id,scale,schema_version,version,world_id");
    Check(core.GetProperty("address").GetString()=="usap://mutsea/entity/"+R&&core.GetProperty("version").GetString()==b.version);
    var entry=JsonSerializer.SerializeToElement(GeoBindings.CoreEntry(b,montreal));
    Check(string.Join(",",entry.EnumerateObject().Select(p=>p.Name).Order(StringComparer.Ordinal))=="address,anchor,authority,authorization,binding_relation,region_id,schema_version,world_id"&&entry.GetProperty("authorization").GetString()=="required");
});

// ---- correspondence
Test("correspondence resolves the most specific published feature and refuses foreign indexes",()=>{
    var (b,index,_)=Synthetic(montreal,19.3,1);var c=new GeoCorrespondence(index,b);
    Check(c.At(110,110).Select(f=>f.kind).SequenceEqual(new[]{"building","terrain"}));
    Check(c.At(110,96.5).Select(f=>f.kind).SequenceEqual(new[]{"road","terrain"}));
    Check(c.At(300,300).Select(f=>f.kind).SequenceEqual(new[]{"terrain"}));
    var stand=c.StandingNear(c.Index.features.Single(f=>f.kind=="building"),512,512,(x,y)=>20,5)!;
    Check(!c.At(stand[0],stand[1]).Any(f=>f.kind=="building")&&c.At(stand[0],stand[1]).Any(f=>f.kind=="road"),"Standing point must be on the adjacent road, outside the building");
    Reject(()=>new GeoCorrespondence(index with{binding_transform_sha256=Hex('0')},b),"another binding");
    Reject(()=>new GeoCorrespondence(index with{features=index.features.Take(2).ToArray()},b),"count");
});

// ---- journaled issuance, revision, retirement and compensation
Test("issue applies pinned terrain and water, survives restart, and is idempotent",()=>{
    var dir=Temp();var fake=new FakeRegion(R,64);var (b,index,r32)=Synthetic(montreal,19.3,1,64);
    var k=new GeoBindingKernel(fake,dir,W);var issued=k.Issue(Bytes(b),Bytes(index),r32);
    Check(issued.kind=="issued"&&fake.TerrainR32().AsSpan().SequenceEqual(r32)&&fake.WaterHeight==b.terrain.water_height_metres&&k.TerrainMatchesIssued());
    var again=new GeoBindingKernel(fake,dir,W);Check(again.Active!.binding.version==b.version&&again.Issue(Bytes(b),Bytes(index),r32).operation_id==issued.operation_id);
    Check(again.Records.Count(r=>r.kind=="issued")==1);
});
Test("a revision must directly supersede the active version; retirement restores the predecessor",()=>{
    var dir=Temp();var fake=new FakeRegion(R,64);var (b1,i1,r1)=Synthetic(montreal,19.3,1,64);var k=new GeoBindingKernel(fake,dir,W);k.Issue(Bytes(b1),Bytes(i1),r1);
    var (b2,i2,r2)=Synthetic(montreal,19.3,1,64,revision:2,previous:b1.version,entry:[30,34,0]);
    var (bad,_,_)=Synthetic(montreal,19.3,1,64,revision:2,previous:Hex('e'));Reject(()=>k.Issue(Bytes(bad),Bytes(i2),r2),"supersede");
    k.Issue(Bytes(b2),Bytes(i2),r2);Check(k.Active!.binding.version==b2.version&&k.Active.binding.binding_id==b1.binding_id);
    k.Retire(b2.version);Check(k.Active!.binding.version==b1.version);
    Check(new GeoBindingKernel(fake,dir,W).Active!.binding.version==b1.version,"Retirement must survive restart");
    Reject(()=>k.Retire(b2.version),"active");
});
Test("terrain never replaces ground under live objects; retirement never overwrites later edits",()=>{
    var dir=Temp();var fake=new FakeRegion(R,64){Objects=true};var (b,index,r32)=Synthetic(montreal,19.3,1,64);var k=new GeoBindingKernel(fake,dir,W);
    Reject(()=>k.Issue(Bytes(b),Bytes(index),r32),"live objects");
    fake.Objects=false;k.Issue(Bytes(b),Bytes(index),r32);fake.Heights[5]+=1;
    Check(!k.TerrainMatchesIssued());Reject(()=>k.Retire(b.version),"Terrain changed");
});
Test("forged inputs are refused before any mutation",()=>{
    var dir=Temp();var fake=new FakeRegion(R,64);var (b,index,r32)=Synthetic(montreal,19.3,1,64);var k=new GeoBindingKernel(fake,dir,W);
    var tampered=r32.ToArray();tampered[9]^=1;Reject(()=>k.Issue(Bytes(b),Bytes(index),tampered),"pin");
    Reject(()=>k.Issue(Bytes(b),Bytes(index with{features=index.features.Reverse().ToArray()}),r32),"pin");
    Reject(()=>new GeoBindingKernel(fake,Temp(),G).Issue(Bytes(b),Bytes(index),r32),"another world");
    Check(fake.LoadCalls==0&&k.Records.Count==0);
});
Test("a failed application is compensated; a failed compensation fences further issuance",()=>{
    var fake=new FakeRegion(R,64){FailLoads=1};var before=fake.TerrainR32();var (b,index,r32)=Synthetic(montreal,19.3,1,64);var k=new GeoBindingKernel(fake,Temp(),W);
    try{k.Issue(Bytes(b),Bytes(index),r32);throw new Exception("expected failure");}catch(IOException){}
    Check(k.Records[^1].kind=="issue_rolled_back"&&fake.TerrainR32().AsSpan().SequenceEqual(before)&&!k.RecoveryRequired&&k.Active==null);
    var broken=new FakeRegion(R,64){FailLoads=99};var dir=Temp();var k2=new GeoBindingKernel(broken,dir,W);
    try{k2.Issue(Bytes(b),Bytes(index),r32);}catch(IOException){}
    Check(k2.RecoveryRequired&&k2.Records[^1].kind=="recovery_required");Reject(()=>k2.Issue(Bytes(b),Bytes(index),r32),"recovery");
    Check(new GeoBindingKernel(broken,dir,W).RecoveryRequired,"Recovery must persist across restart");
});
Test("entry evidence is per actor, bounded and bound to the active version",()=>{
    var dir=Temp();var fake=new FakeRegion(R,64);var (b,index,r32)=Synthetic(montreal,19.3,1,64);var k=new GeoBindingKernel(fake,dir,W);
    Reject(()=>k.RecordEntry(E,new{}),"No active");
    k.Issue(Bytes(b),Bytes(index),r32);for(int i=0;i<60;i++)k.RecordEntry(E,new{i});Reject(()=>k.RecordEntry(E,new{}),"Too many");k.RecordEntry(G,new{});
    Check(new GeoBindingKernel(fake,dir,W).Records.Count(r=>r.kind=="entry"&&r.version==b.version)==61);
});

// ---- the real Old Montréal binding
if(args.Length==3)
{
    string prefix=args[0];var pubDir=args[1];var oracle=JsonDocument.Parse(File.ReadAllText(args[2])).RootElement;
    var binding=JsonSerializer.Deserialize<GeographicBinding>(File.ReadAllBytes(prefix+".binding.json"),ContentCatalog.Json)!;
    var corrBytes=File.ReadAllBytes(prefix+".correspondence.json");var r32=File.ReadAllBytes(prefix+".terrain.r32");
    GeoTransformer t=null!;GeoCorrespondence corr=null!;
    Test("real binding validates against its pinned publication and terrain",()=>{
        t=GeoBindings.Validate(binding,binding.region.region_id,512,512);
        Check(binding.publication.root_sha256=="bf7be87789437a6d7904974942456160ad381630a7d5f3af54184a0e73f76368"&&binding.publication.version=="ba323a8fe864ee719baf7a0a22c8293cad1db3adcf6b658bc87f5d584595aa4d");
        Check(ContentCatalog.Hash(r32)==binding.terrain.r32_sha256&&ContentCatalog.Hash(corrBytes)==binding.correspondence.sha256);
        var root=Directory.GetFiles(Path.Combine(pubDir,"scene-publications")).Single();Check(ContentCatalog.Hash(File.ReadAllBytes(root))==binding.publication.root_sha256);
        foreach(var a in binding.terrain.source_assets)Check(ContentCatalog.Hash(File.ReadAllBytes(Path.Combine(pubDir,"objects",a)))==a);
        corr=new GeoCorrespondence(JsonSerializer.Deserialize<GeoCorrespondenceIndex>(corrBytes,ContentCatalog.Json)!,binding);
    });
    Test("real alignment: publication origin at the region centre, z = CGVD2013 there, grid-aligned heading",()=>{
        var c=t.Local(binding.publication.frame_origin);Check(Math.Abs(c[0]-256)<1e-6&&Math.Abs(c[1]-256)<1e-6&&Math.Abs(c[2]-19.070165634155273)<1e-6);
        Check(Math.Abs(binding.transform.heading_degrees-19.3155)<.001&&binding.transform.scale==1);
        Check(oracle.GetProperty("control_point_max_error_metres").GetDouble()<1e-6&&oracle.GetProperty("origin_vs_region_centre_metres").GetDouble()<1e-6&&Math.Abs(oracle.GetProperty("grid_north_vs_region_y_degrees").GetDouble())<.001,"Independent oracle disagrees");
    });
    Test("real terrain: MutSea ground reproduces the published triangles; heights are persistence fixed points",()=>{
        Check(binding.terrain.rms_surface_residual_metres<=.01&&binding.terrain.max_surface_residual_metres<=.25&&binding.terrain.extended_samples<512*512*.03);
        for(int i=0;i<r32.Length;i+=4){var h=BitConverter.ToSingle(r32,i);Check(MathF.Round(h,3,MidpointRounding.AwayFromZero)==h);}
        Check(Math.Abs(oracle.GetProperty("z_minus_orthometric").GetProperty("max_abs").GetDouble())<=binding.vertical.max_deviation_metres+1e-6,"Vertical deviation exceeds its declaration");
    });
    Test("real correspondence: 42 buildings, 93 roads, 4 terrain tiles; every building centroid resolves to itself",()=>{
        Check(corr.Index.features.Count(f=>f.kind=="building")==42&&corr.Index.features.Count(f=>f.kind=="road")==93&&corr.Index.features.Count(f=>f.kind=="terrain")==4);
        foreach(var f in corr.Index.features.Where(f=>f.kind=="building")){var c=GeoCorrespondence.Centroid(f);if(corr.Covers(f,c[0],c[1]))Check(corr.At(c[0],c[1])[0].kind=="building");}
        Check(corr.Index.features.Where(f=>f.kind=="building").Count(f=>{var c=GeoCorrespondence.Centroid(f);return corr.Covers(f,c[0],c[1]);})>=35,"Most roof outlines contain their centroid");
        Check(corr.At(256,256).Any(f=>f.kind=="terrain")&&!corr.At(binding.entry.position[0],binding.entry.position[1]).Any(f=>f.kind=="building"),"The entry stands in the open");
    });
}
else{notRun=4;Console.WriteLine("NOT RUN real Old Montréal binding (pass <candidate prefix> <publication dir> <oracle-verify.json>)");}
Console.WriteLine($"{passed} passed, {notRun} not run");
return 0;

// ---- fixtures
GeoTransformer Transformer(Geodetic anchor,double heading,double scale)=>new(Synthetic(anchor,heading,scale).binding);
(GeographicBinding binding,GeoCorrespondenceIndex index,byte[] r32) Synthetic(Geodetic anchor,double heading,double scale,int size=512,int revision=1,string? previous=null,double[]? entry=null)
{
    var toEcef=GeoBindings.RegionToEcef(anchor,heading,scale);
    var origin=GeoFrames.FromEcef(GeoFrames.Apply(toEcef,[size/2.0,size/2.0,20]));
    var transform=new GeoTransform(GeoBindings.Model,"wgs84",anchor,GeoBindings.EnuAxes,"metre",heading,scale,toEcef,GeoBindings.RegionToPublication(anchor,heading,scale,origin));
    var publication=new GeoPublication("spatialdb",G,Hex('a'),Hex('b'),E,"usap://spatialdb/entity/"+E,"test.eus/0",origin,GeoBindings.PublicationAxes,Hex('c'),"CC0-1.0");
    var r32=new byte[size*size*4];for(int y=0;y<size;y++)for(int x=0;x<size;x++)BitConverter.GetBytes(MathF.Round(10+x*.01f,3,MidpointRounding.AwayFromZero)).CopyTo(r32,(y*size+x)*4);
    double f=size/512.0;
    GeoFeature Feature(string id,string kind,double[] square)=>new(id,"usap://spatialdb/entity/"+id,kind,kind+" fixture","fixture",id[..8],"estimated",null,
        [square[0],square[1],0],[square[2],square[3],10],[square[0],square[1],square[2],square[1],square[2],square[3],square[0],square[1],square[2],square[3],square[0],square[3]],G,Hex('d'));
    GeoFeature[] features=[Feature("10000000-0000-4000-8000-000000000001","building",[100*f,100*f,120*f,120*f]),Feature("10000000-0000-4000-8000-000000000002","road",[90*f,95*f,130*f,98*f]),
        Feature("10000000-0000-4000-8000-000000000003","terrain",[0,0,size,size])];
    var index=new GeoCorrespondenceIndex(GeoBindings.CorrespondenceSchema,R,GeoBindings.TransformSha(transform),publication.version,publication.root_sha256,features);
    var draft=new GeographicBinding(GeoBindings.Schema,GeoBindings.BindingId(W,R),revision,previous,"mutsea",W,new(R,"Fixture",[size,size]),transform,
        new("Fixture z.","CGVD2013 orthometric height",-31.5,"CGG2013a",Hex('9'),.04),publication,null!,
        new("fixture",[Hex('8')],ContentCatalog.Hash(r32),[size,size],1,10,10+.01*(size-1)+.001,5,0,0,.001,.0005,[[0,0],[size,0],[size,size],[0,size]]),
        new(GeoBindings.CorrespondenceSchema,ContentCatalog.Hash(Bytes(index)),features.Length,"fixture"),new(entry is null?[size/2.0,size/2.0,12]:[entry[0],entry[1],12],[size/2.0,size/2.0+4,13]),
        [new("fixture","Finalverse","CC0-1.0","Fixture data")],"");
    var tr=new GeoTransformer(draft);
    GeoControlPoint P(string id,string role,double[] l)=>new(id,role,tr.Geographic(l),tr.Publication(l),l);
    var cps=new[]{P("centre","region_centre",[size/2.0,size/2.0,20]),P("sw","region_corner",[0,0,20]),P("se","region_corner",[size,0,20]),P("ne","region_corner",[size,size,20]),P("nw","region_corner",[0,size,20])};
    var b=GeoBindings.Seal(draft with{calibration=new("fixture",.001,0,0,0,cps)});
    GeoBindings.Validate(b,R,size,size);
    return (b,index,r32);
}
static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value);
static string Temp(){var d=Path.Combine(Path.GetTempPath(),"finalverse-geo-tests-"+Guid.NewGuid());Directory.CreateDirectory(d);return d;}
static double[] Sub(double[] a,double[] b)=>[a[0]-b[0],a[1]-b[1],a[2]-b[2]];
static double Dot(double[] a,double[] b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];

sealed class FakeRegion(string id,int size):IGeoRegion
{
    public float[] Heights=new float[size*size];public double Water=20;public bool Objects;public int LoadCalls,FailLoads;
    public string RegionId=>id;public double SizeX=>size;public double SizeY=>size;
    public bool HasLiveObjects()=>Objects;
    public string TerrainHashFormat=>"fixture-r32-mm";
    public string TerrainHash()=>ContentCatalog.Hash(Encode(Heights.Select(h=>MathF.Round(h,3,MidpointRounding.AwayFromZero)).ToArray()));
    public string SnapshotTerrain(string path){var b=TerrainR32();File.WriteAllBytes(path,b);return ContentCatalog.Hash(b);}
    public byte[] TerrainR32()=>Encode(Heights);
    public void LoadTerrain(byte[] r32){if(FailLoads-->0)throw new IOException("injected terrain failure");LoadCalls++;for(int i=0;i<Heights.Length;i++)Heights[i]=BitConverter.ToSingle(r32,i*4);}
    public double WaterHeight=>Water;
    public void SetWaterHeight(double height)=>Water=height;
    static byte[] Encode(float[] h){var b=new byte[h.Length*4];for(int i=0;i<h.Length;i++)BitConverter.GetBytes(h[i]).CopyTo(b,i*4);return b;}
}
