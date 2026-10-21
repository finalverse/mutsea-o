// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
using System.Text;
using System.Text.Json;
int passed=0;void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
void Assert(bool ok){if(!ok)throw new Exception("Assertion failed.");}
void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected rejection.");}
Test("native AABB protects a long narrow path without inflated circles",()=>{Assert(!ContentKernel.Overlaps(168,136,.75,141,183,125.5,130.5));Assert(ContentKernel.Overlaps(168,128,.75,141,183,125.5,130.5));Assert(ContentKernel.Overlaps(141.5,131,.75,141,183,125.5,130.5));});
var root=Path.Combine(Path.GetTempPath(),"mutsea-content-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
try{
 var directory=Path.Combine(root,"catalog","lantern");Directory.CreateDirectory(directory);
 byte[] mesh=[1,2,3];File.WriteAllBytes(Path.Combine(directory,"mesh.llmesh"),mesh);
 var manifest=JsonSerializer.SerializeToUtf8Bytes(new{kind="asset",package_id="mutsea.test.lantern",version="0.1.0",license=new{spdx="CC0-1.0"},budget=new{triangles=2},entities=new[]{new{local_id="base",name="Lantern",semantic_type="lighting",description="Original test lamp",tags=new[]{"starter"},affordances=new[]{"inspect"},relationships=Array.Empty<ContentRelationship>()}}});
 File.WriteAllBytes(Path.Combine(directory,"package.json"),manifest);
 var package=new ContentPackage(1,"mutsea.test.lantern","0.1.0","Lantern",ContentCatalog.Hash(manifest),"CC0-1.0",[new("base","Lantern","lighting","Original test lamp",[0,0,.5],[.5,.5,1],[0,0,0,1],[new([1,.7,.2,1],true)],"mesh.llmesh",ContentCatalog.Hash(mesh),2)],"full","convex");
 void Save(ContentPackage p)=>File.WriteAllText(Path.Combine(directory,"compiled.json"),JsonSerializer.Serialize(p));Save(package);
 var catalog=new ContentCatalog(Path.Combine(root,"catalog"));var world=new Fake();var actor=Guid.NewGuid().ToString();var journal=Path.Combine(root,"journal");var region=Guid.NewGuid().ToString();var kernel=new ContentKernel(world,catalog,journal,region);
 var grid=Guid.NewGuid().ToString();
 var fsip=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new{
   protocol_version="0.1",manifest_version=1,experience_id=Guid.NewGuid(),owner=new{earth_subject=new string('a',64),mutsea_agent_id=(string?)null},title="Synthetic FSIP fixture",source_workspace=(object?)null,world_template="mutsea.test.virtual",
   transform=new{layout="virtual",source_context=(object?)null,target=new{grid_id=grid,region_id=region},frame=new{frame_id=Guid.NewGuid(),axes="east_north_up",metres_per_unit=1,origin=new[]{0d,0,0},geo_anchor=(object?)null},description="Entirely virtual test frame"},
   objects=new[]{new{local_id="test-lamp",asset=new{package_id=package.package_id,version=package.version,manifest_sha256=package.manifest_sha256,license="CC0-1.0"},offset_m=new[]{0d,0,0},yaw_degrees=0,semantic_type="lighting",description="Synthetic test asset",source_record_ids=Array.Empty<string>(),generated_geometry=true}},
   semantic_zones=new[]{new{zone_id="review",title="Test",affordances=new[]{"inspect"}}},required_permissions=new[]{"read_context","preview","create_owned_place","inspect_receipt","undo_owned_place"},
   budget=new{max_objects=1,max_triangles=2,max_asset_bytes=4,max_lights=0,scripts_allowed=false},provenance=new[]{new{source="synthetic-test",record_ids=Array.Empty<string>(),license="CC0-1.0",attribution="Test fixture only",sha256=new string('b',64),generated=true}},ai_narrative="Unit fixture, not production evidence",created_at=DateTimeOffset.UtcNow
 }))!;
 void FsipReject(Action<System.Text.Json.Nodes.JsonNode> change){var bad=fsip.DeepClone();change(bad);Reject(()=>EarthBridgeContract.Draft(JsonSerializer.SerializeToElement(bad),grid,region,catalog));}
 Test("FSIP compiles a bounded virtual draft using approved assets",()=>Assert(EarthBridgeContract.Draft(JsonSerializer.SerializeToElement(fsip),grid,region,catalog).placements.Length==1));
 Test("FSIP denies another audience, implicit geography and unknown fields",()=>{FsipReject(m=>m["transform"]!["target"]!["region_id"]=Guid.NewGuid().ToString());FsipReject(m=>m["transform"]!["frame"]!["geo_anchor"]=new System.Text.Json.Nodes.JsonObject());FsipReject(m=>m["exec"]="bad");});
 Test("FSIP rechecks actual parts, triangles, asset bytes and scripts",()=>{FsipReject(m=>m["budget"]!["max_objects"]=0);FsipReject(m=>m["budget"]!["max_triangles"]=1);FsipReject(m=>m["budget"]!["max_asset_bytes"]=2);FsipReject(m=>m["budget"]!["scripts_allowed"]=true);});
 Test("FSIP denies changed pins, invented records and unlabelled geometry",()=>{FsipReject(m=>m["objects"]![0]!["asset"]!["manifest_sha256"]=new string('c',64));FsipReject(m=>m["objects"]![0]!["source_record_ids"]=new System.Text.Json.Nodes.JsonArray("fabricated"));FsipReject(m=>m["objects"]![0]!["generated_geometry"]=false);FsipReject(m=>m["provenance"]![0]!["generated"]=false);});
 Test("v1 metadata hydrated from pinned manifest",()=>{var p=catalog.Get(package.package_id,package.version).package;Assert(p.parts[0].semantics!.affordances.SequenceEqual(new[]{"inspect"}));});
 Test("v2 metadata omission fails closed",()=>Reject(()=>ContentCatalog.Validate(package with{schema_version=2})));
 var material=new ContentMaterialFactors("wood",[.3,.12,.04,1],0,.7,[0,0,0],"OPAQUE",.5,false);
 var pbr=package with{schema_version=3,materials=[new("wood","wood.llmaterial",ContentCatalog.Hash(mesh),material)],parts=[package.parts[0] with{faces=[new([1,1,1,1],false,"wood")],semantics=new([], ["inspect"],[])}]};
 Test("bounded PBR contract rejects unknown bindings and fullbright",()=>{
  ContentCatalog.Validate(pbr);
  Reject(()=>ContentCatalog.Validate(pbr with{parts=[pbr.parts[0] with{faces=[new([1,1,1,1],true,"wood")]}]}));
  Reject(()=>ContentCatalog.Validate(pbr with{parts=[pbr.parts[0] with{faces=[new([1,1,1,1],false,"missing")]}]}));
  Reject(()=>ContentCatalog.Validate(pbr with{schema_version=2}));
 });
 Test("linear PBR factors reject nonfinite or out of range values",()=>{
  Reject(()=>ContentCatalog.ValidateMaterial(material with{metallic_factor=double.NaN}));
  Reject(()=>ContentCatalog.ValidateMaterial(material with{base_color_linear=[1,1,1,2]}));
  Reject(()=>ContentCatalog.ValidateMaterial(material with{emissive_linear=[-1,0,0]}));
 });
 Test("single native seat requires sit semantics and a bounded target",()=>{
  var seating=pbr with{parts=[pbr.parts[0] with{semantic_type="seating",semantics=new([], ["sit"],[]),seat=new(1,[0,0,.1],[0,0,0,1])}]};
  ContentCatalog.Validate(seating);
  Reject(()=>ContentCatalog.Validate(seating with{parts=[seating.parts[0] with{seat=new(2,[0,0,.1],[0,0,0,1])}]}));
  Reject(()=>ContentCatalog.Validate(seating with{parts=[seating.parts[0] with{semantics=new([], ["inspect"],[])}]}));
  Reject(()=>ContentCatalog.Validate(seating with{parts=[seating.parts[0] with{seat=new(1,[0,0,0],[0,0,0,1])}]}));
 });
 Test("native lights require bounded factors, explicit semantics and v3",()=>{
  var lit=pbr with{parts=[pbr.parts[0] with{light=new([1,.7,.4],.8,3.5,1)}]};ContentCatalog.Validate(lit);
  Reject(()=>ContentCatalog.Validate(lit with{parts=[lit.parts[0] with{semantic_type="component"}]}));
  Reject(()=>ContentCatalog.ValidateLight(new([1,.7,.4],.8,4.1,1)));
  Reject(()=>ContentCatalog.ValidateLight(new([1,.7,.4],double.NaN,3,1)));
  Reject(()=>ContentCatalog.ValidateLight(new([0,0,0],.8,3,1)));
  Reject(()=>ContentCatalog.Validate(lit with{parts=Enumerable.Range(0,3).Select(i=>lit.parts[0] with{local_id="light"+i}).ToArray()}));
 });
 Test("reported LOD totals must reduce and match high geometry",()=>{
  Reject(()=>ContentCatalog.Validate(pbr with{lod_triangles=[2,2,1,1]}));
  Reject(()=>ContentCatalog.Validate(pbr with{lod_triangles=[10,5,3,1]}));
 });
 Test("explicit small-part collision rejects unsafe hulls and noncolliding roots",()=>{
  var solid=pbr with{parts=[pbr.parts[0] with{collision=new("box",[-.1,-.1,-.5],[.1,.1,.5])}]};
  ContentCatalog.Validate(solid);
  Reject(()=>ContentCatalog.Validate(solid with{parts=[solid.parts[0] with{collision=new("box",[-.6,-.1,-.5],[.1,.1,.5])}]}));
  Reject(()=>ContentCatalog.Validate(solid with{parts=[solid.parts[0] with{collision=new("box",[0,0,0],[0,.1,.1])}]}));
  Reject(()=>ContentCatalog.Validate(solid with{parts=[solid.parts[0] with{collision=new("none")}]}));
  Reject(()=>ContentCatalog.ValidateCollision(new("none",[0,0,0],[.1,.1,.1])));
  Reject(()=>ContentCatalog.ValidateCollision(new("box",[double.NaN,0,0],[.1,.1,.1])));
  var child=solid.parts[0] with{local_id="canopy",collision=new("none")};
  ContentCatalog.Validate(solid with{parts=[solid.parts[0],child]});
 });
 Test("unsupported affordance and dangling relationship rejected",()=>{Reject(()=>ContentCatalog.Validate(package with{parts=[package.parts[0] with{semantics=new(["starter"],["execute_code"],[])}]}));Reject(()=>ContentCatalog.Validate(package with{parts=[package.parts[0] with{semantics=new([], ["inspect"],[new("part_of","missing")])}]}));});
 Test("compiled metadata cannot contradict source manifest",()=>{Save(package with{parts=[package.parts[0] with{semantics=new(["starter"],["sit"],[])}]});Reject(()=>new ContentCatalog(Path.Combine(root,"catalog")));Save(package);});
 Test("v2 relationships map to canonical IDs and survive restart",()=>{
  var folder=Path.Combine(root,"seating","bench");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"mesh.llmesh"),mesh);
  var metadata=new ContentMetadata(["coastal"],["sit","inspect"],[]);
  var seat=package.parts[0] with{local_id="seat",name="Bench",semantic_type="seating",semantics=metadata};
  var leg=seat with{local_id="leg",name="Leg",semantic_type="component",semantics=new(["coastal"],["inspect"],[new("part_of","seat")])};
  var bytes=JsonSerializer.SerializeToUtf8Bytes(new{kind="asset",package_id="mutsea.test.bench",version="0.1.1",license=new{spdx="CC0-1.0"},budget=new{triangles=4},entities=new[]{seat,leg}.Select(p=>new{p.local_id,p.name,p.semantic_type,p.description,p.semantics!.tags,p.semantics.affordances,p.semantics.relationships})});
  File.WriteAllBytes(Path.Combine(folder,"package.json"),bytes);
  var p=package with{schema_version=2,package_id="mutsea.test.bench",version="0.1.1",parts=[seat,leg],manifest_sha256=ContentCatalog.Hash(bytes)};File.WriteAllText(Path.Combine(folder,"compiled.json"),JsonSerializer.Serialize(p));
  var c=new ContentCatalog(Path.Combine(root,"seating"));var w=new Fake();var k=new ContentKernel(w,c,Path.Combine(root,"seat-journal"),region);var plan=k.Preview(p.package_id,p.version,[1,1,1],actor);k.Commit(plan.plan_id,actor);
  k=new ContentKernel(w,c,Path.Combine(root,"seat-journal"),region);
  var semantic=JsonSerializer.SerializeToElement(k.Semantic(plan.entity_ids[1],actor));Assert(semantic.GetProperty("relationships")[0].GetProperty("target").GetString()==plan.entity_ids[0]);
  Assert(JsonSerializer.SerializeToElement(k.Semantic(plan.entity_ids[0],actor)).GetProperty("affordances")[0].GetString()=="sit");
  k.Undo(plan.plan_id,actor);Assert(k.Semantic(plan.entity_ids[0],actor)==null);
 });
 Test("reject unknown package and nonfinite anchor",()=>{Reject(()=>kernel.Preview("missing","1",[1,1,1],actor));Reject(()=>kernel.Preview(package.package_id,package.version,[double.NaN,1,1],actor));});
 Test("native policy enforced before approval",()=>{world.Deny=true;Reject(()=>kernel.Preview(package.package_id,package.version,[1,1,1],actor));world.Deny=false;});
 Test("one pending approval per actor",()=>{var first=kernel.Preview(package.package_id,package.version,[1,1,1],actor);kernel.Preview(package.package_id,package.version,[2,2,2],actor);Reject(()=>kernel.Commit(first.plan_id,actor));});
 var plan=kernel.Preview(package.package_id,package.version,[1,1,1],actor);
 Test("actor-bound commit and stale session",()=>{Reject(()=>kernel.Commit(plan.plan_id,Guid.NewGuid().ToString()));kernel.Invalidate(actor);Reject(()=>kernel.Commit(plan.plan_id,actor));});
 plan=kernel.Preview(package.package_id,package.version,[1,1,1],actor);
 Test("policy revalidated at commit",()=>{world.Deny=true;Reject(()=>kernel.Commit(plan.plan_id,actor));world.Deny=false;});
 Test("intent precedes mutation and no replay",()=>{world.BeforeCreate=()=>Assert(File.ReadAllText(Path.Combine(journal,region+".content.worldline.jsonl")).Contains("\"status\":\"intent\""));var result=kernel.Commit(plan.plan_id,actor);Assert(result.status=="committed");Reject(()=>kernel.Commit(plan.plan_id,actor));});
 Test("semantic mapping and restart projection",()=>{Assert(JsonSerializer.Serialize(kernel.Semantic(plan.entity_ids[0],actor)).Contains("lighting"));kernel=new ContentKernel(world,catalog,journal,region);Assert(kernel.History(actor).Last().status=="committed"&&!kernel.RecoveryRequired);Assert(kernel.Semantic(plan.entity_ids[0],actor)!=null);});
 Test("undo protects later edits and owner",()=>{world.Value="edited";Reject(()=>kernel.Undo(plan.plan_id,actor));world.Value="original";Reject(()=>kernel.Undo(plan.plan_id,Guid.NewGuid().ToString()));});
 Test("native deletion denial precedes intent and leaves writes available",()=>{var count=kernel.History(actor).Length;world.DenyDelete=true;Reject(()=>kernel.Undo(plan.plan_id,actor));Assert(!kernel.RecoveryRequired&&kernel.History(actor).Length==count);world.DenyDelete=false;});
 Test("undo durable and idempotent",()=>{Assert(kernel.Undo(plan.plan_id,actor).status=="undone");Assert(kernel.Semantic(plan.entity_ids[0],actor)==null);Reject(()=>kernel.Undo(plan.plan_id,actor));});
 Test("failed import without scene insertion is recorded and retryable",()=>{var k=new ContentKernel(world,catalog,Path.Combine(root,"failure"),region);world.FailCreate=true;var p=k.Preview(package.package_id,package.version,[1,1,1],actor);Reject(()=>k.Commit(p.plan_id,actor));Assert(!k.RecoveryRequired&&k.History(actor).Last().status=="failed");world.FailCreate=false;});
 Test("partial insertion blocks writes and preserves evidence",()=>{var k=new ContentKernel(world,catalog,Path.Combine(root,"partial"),region);world.FailCreate=true;world.PartialCreate=true;var p=k.Preview(package.package_id,package.version,[1,1,1],actor);Reject(()=>k.Commit(p.plan_id,actor));Assert(k.RecoveryRequired&&world.Value=="partial"&&k.History(actor).Last().status=="recovery_required");Reject(()=>k.Preview(package.package_id,package.version,[1,1,1],actor));k=new ContentKernel(world,catalog,Path.Combine(root,"partial"),region);Assert(k.RecoveryRequired);world.FailCreate=false;world.PartialCreate=false;world.Value=null;});
 Test("failed state observation blocks writes immediately and after restart",()=>{var k=new ContentKernel(world,catalog,Path.Combine(root,"unobservable"),region);world.FailCreate=true;world.FailFingerprint=true;var p=k.Preview(package.package_id,package.version,[1,1,1],actor);Reject(()=>k.Commit(p.plan_id,actor));Assert(k.RecoveryRequired&&k.History(actor).Last().status=="recovery_required");Reject(()=>k.Preview(package.package_id,package.version,[1,1,1],actor));Assert(new ContentKernel(world,catalog,Path.Combine(root,"unobservable"),region).RecoveryRequired);world.FailCreate=false;world.FailFingerprint=false;});
 Test("incomplete intent blocks restart writes",()=>{var incomplete=kernel.Preview(package.package_id,package.version,[1,1,1],actor);File.AppendAllText(Path.Combine(journal,region+".content.worldline.jsonl"),JsonSerializer.Serialize(new ContentEntry(Guid.NewGuid().ToString(),"intent",DateTimeOffset.UtcNow,incomplete,null,null))+"\n");kernel=new ContentKernel(world,catalog,journal,region);Assert(kernel.RecoveryRequired);Reject(()=>kernel.Preview(package.package_id,package.version,[1,1,1],actor));});
 Test("reject hash mismatch",()=>{File.WriteAllBytes(Path.Combine(directory,"mesh.llmesh"),[9]);Reject(()=>new ContentCatalog(Path.Combine(root,"catalog")));File.WriteAllBytes(Path.Combine(directory,"mesh.llmesh"),mesh);});
 if(!OperatingSystem.IsWindows())Test("reject linked catalog payload",()=>{string file=Path.Combine(directory,"mesh.llmesh"),outside=Path.Combine(root,"outside.llmesh");File.WriteAllBytes(outside,mesh);File.Delete(file);File.CreateSymbolicLink(file,outside);Reject(()=>new ContentCatalog(Path.Combine(root,"catalog")));File.Delete(file);File.WriteAllBytes(file,mesh);});
 Test("reject traversal and nonfinite transform",()=>{Reject(()=>ContentCatalog.Validate(package with{parts=[package.parts[0] with{mesh_path="../mesh"}]}));Reject(()=>ContentCatalog.Validate(package with{parts=[package.parts[0] with{position=[0,double.NaN,0]}]}));});
 Test("reject semantic omission and overbudget content",()=>{Reject(()=>ContentCatalog.Validate(package with{parts=[package.parts[0] with{semantic_type=""}]}));Reject(()=>ContentCatalog.Validate(package with{parts=[package.parts[0] with{triangles=4097}]}));});
 Test("reject unsigned external license and unknown schema fields",()=>{Reject(()=>ContentCatalog.Validate(package with{license="unknown"}));File.WriteAllText(Path.Combine(directory,"compiled.json"),JsonSerializer.Serialize(package).TrimEnd('}')+",\"exec\":\"bad\"}");try{new ContentCatalog(Path.Combine(root,"catalog"));throw new Exception("Accepted unknown field");}catch(JsonException){}Save(package);});
 // ---- PlaceTemplate: composition of approved small assets in one journaled operation.
 var placeRoot=Path.Combine(root,"places");var pinned=catalog.Get(package.package_id,package.version).package.manifest_sha256;
 PlaceTemplate Place(int n,double spread=3)=>new(1,"place","mutsea.test.pavilion","0.1.0","Test Pavilion","An original test gathering place","CC0-1.0",[8,8],["social"],["walk","gather","sit"],
  Enumerable.Range(0,n).Select(i=>new PlacePlacement("lamp"+i,package.package_id,package.version,pinned,[spread*Math.Cos(i),spread*Math.Sin(i),0],i*45,"lighting")).ToArray(),[new("north",[0,9,0],180)]);
 void SavePlace(PlaceTemplate t){var d=Path.Combine(placeRoot,"pavilion");Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"place.json"),JsonSerializer.Serialize(t));}
 Test("place catalog pins assets, bounds footprints and budgets",()=>{
  SavePlace(Place(4));var pc=new PlaceCatalog(placeRoot,catalog);Assert(pc.Get("mutsea.test.pavilion","0.1.0").placements.Length==4);
  Reject(()=>PlaceCatalog.Validate(Place(4) with{placements=[Place(1).placements[0] with{manifest_sha256=new string('0',64)}]},catalog));
  Reject(()=>PlaceCatalog.Validate(Place(1,8.2),catalog));PlaceCatalog.Validate(Place(1,7.8),catalog);
  Reject(()=>PlaceCatalog.Validate(Place(1) with{placements=[Place(1).placements[0] with{package_id="unknown"}]},catalog));
  Reject(()=>PlaceCatalog.Validate(Place(1) with{half_extent=[40,8]},catalog));
  Reject(()=>PlaceCatalog.Validate(Place(1) with{affordances=["execute"]},catalog));
  Reject(()=>PlaceCatalog.Validate(Place(1) with{entrances=[new("far",[0,20,0],0)]},catalog));
  Reject(()=>PlaceCatalog.Validate(Place(2) with{placements=[Place(1).placements[0],Place(1).placements[0]]},catalog));
 });
 Test("yaw rotates offsets about the anchor",()=>{var r=PlaceKernel.Rotate([1,0,2],90);Assert(Math.Abs(r[0])<1e-9&&Math.Abs(r[1]-1)<1e-9&&r[2]==2);});
 var placeCatalog=new PlaceCatalog(placeRoot,catalog);var pw=new FakePlace();var operatorId=Guid.NewGuid().ToString();var retiredId=Guid.NewGuid().ToString();
 Test("provision creates every placement, retires replaced objects and answers place semantics",()=>{
  pw.Existing.Add(retiredId);var k=new PlaceKernel(pw,catalog,placeCatalog,Path.Combine(root,"place-ok"),region);
  var plan=k.Plan("mutsea.test.pavilion","0.1.0",[100,100,25],90,operatorId,[retiredId]);var done=k.Provision(plan);
  Assert(done.status=="committed"&&done.fingerprints!.Length==4&&pw.Live.Count==4&&!pw.Existing.Contains(retiredId)&&k.Active("mutsea.test.pavilion","0.1.0")!=null);
  Assert(plan.placements.All(p=>p.yaw_degrees==90+p.yaw_degrees-90)&&Math.Abs(plan.placements[0].anchor[1]-103)<1e-9);
  var semantic=JsonSerializer.Serialize(k.Semantic(plan.placements[1].entity_ids[0],operatorId));Assert(semantic.Contains("\"role\":\"lighting\"")&&semantic.Contains("Test Pavilion")&&semantic.Contains("north"));
  Assert(JsonDocument.Parse(semantic).RootElement.GetProperty("semantic_metadata_available").GetBoolean());
  k=new PlaceKernel(pw,catalog,placeCatalog,Path.Combine(root,"place-ok"),region);
  Assert(JsonSerializer.SerializeToElement(k.Semantic(plan.placements[1].entity_ids[0],operatorId)).GetProperty("semantic_metadata_available").GetBoolean());
  Reject(()=>k.Rollback(Guid.NewGuid().ToString()));
  pw.Live[plan.placements[0].entity_ids[0]]="edited";Reject(()=>k.Rollback(plan.plan_id));pw.Live[plan.placements[0].entity_ids[0]]="fp";
  Assert(k.Rollback(plan.plan_id).status=="rolled_back"&&pw.Live.Count==0&&pw.Existing.Contains(retiredId)&&k.Active("mutsea.test.pavilion","0.1.0")==null);
  Assert(k.Semantic(plan.placements[1].entity_ids[0],operatorId)==null);
  Assert(!new PlaceKernel(pw,catalog,placeCatalog,Path.Combine(root,"place-ok"),region).RecoveryRequired);
 });
 Test("a failed placement compensates the whole place and restores retirements",()=>{
  var k=new PlaceKernel(pw,catalog,placeCatalog,Path.Combine(root,"place-fail"),region);pw.FailAt=2;
  var plan=k.Plan("mutsea.test.pavilion","0.1.0",[100,100,25],0,operatorId,[retiredId]);Reject(()=>k.Provision(plan));
  Assert(!k.RecoveryRequired&&pw.Live.Count==0&&pw.Existing.Contains(retiredId)&&k.Entries.Last().status=="compensated");pw.FailAt=-1;
 });
 Test("unconfirmed compensation fences writes, also after restart",()=>{
  var k=new PlaceKernel(pw,catalog,placeCatalog,Path.Combine(root,"place-stuck"),region);pw.FailAt=2;pw.StuckDelete=true;
  var plan=k.Plan("mutsea.test.pavilion","0.1.0",[100,100,25],0,operatorId);Reject(()=>k.Provision(plan));
  Assert(k.RecoveryRequired&&k.Entries.Last().status=="recovery_required");Reject(()=>k.Plan("mutsea.test.pavilion","0.1.0",[100,100,25],0,operatorId));
  Assert(new PlaceKernel(pw,catalog,placeCatalog,Path.Combine(root,"place-stuck"),region).RecoveryRequired);pw.FailAt=-1;pw.StuckDelete=false;pw.Live.Clear();
 });
 Test("an interrupted place intent fences writes after restart",()=>{
  // A process loss leaves the intent as the operation's last record.
  var dir=Path.Combine(root,"place-cut");var k=new PlaceKernel(pw,catalog,placeCatalog,dir,region);Assert(!k.RecoveryRequired);
  var plan=k.Plan("mutsea.test.pavilion","0.1.0",[100,100,25],0,operatorId);
  File.AppendAllText(Path.Combine(dir,region+".places.worldline.jsonl"),JsonSerializer.Serialize(new PlaceEntry(Guid.NewGuid().ToString(),"intent",DateTimeOffset.UtcNow,plan,null,null),ContentCatalog.Json)+"\n");
  var reloaded=new PlaceKernel(pw,catalog,placeCatalog,dir,region);Assert(reloaded.RecoveryRequired);Reject(()=>reloaded.Plan("mutsea.test.pavilion","0.1.0",[100,100,25],0,operatorId));
 });
 Test("a composed draft is validated like an approved place, provisioned as its actor and rolled back",()=>{
  var actor=Guid.NewGuid().ToString();var k=new PlaceKernel(pw,catalog,new PlaceCatalog("",catalog),Path.Combine(root,"place-draft"),region);
  var draft=Place(3) with{place_id="user.0712bdf9.reading-nook",version="0.1.20261012120000"};
  var plan=k.PlanDraft(draft,[100,100,25],30,actor);
  Assert(plan.actor_id==actor&&plan.retired.Length==0&&plan.place.manifest_sha256!.Length==64&&plan.placements.All(p=>p.actor_id==actor));
  Assert(k.PlanDraft(draft,[100,100,25],30,actor).place.manifest_sha256==plan.place.manifest_sha256);  // canonical, deterministic pin
  var done=k.Provision(plan);Assert(done.status=="committed"&&pw.Live.Count==3);
  Assert(k.Rollback(plan.plan_id).status=="rolled_back"&&pw.Live.Count==0);
  Reject(()=>k.PlanDraft(Place(1),[100,100,25],0,actor));  // not a user identity
  Reject(()=>k.PlanDraft(draft with{placements=[draft.placements[0] with{manifest_sha256=new string('0',64)}]},[100,100,25],0,actor));
  Reject(()=>k.PlanDraft(draft with{half_extent=[40,8]},[100,100,25],0,actor));
  try{JsonSerializer.Deserialize<PlaceTemplate>("{\"schema_version\":1,\"kind\":\"place\",\"script\":\"x\"}",ContentCatalog.Json);throw new Exception("Expected an unknown field to be refused.");}
  catch(JsonException){}  // the relay answers 400; unknown draft fields never reach the kernel
 });
 Test("place checks run before any write",()=>{var k=new PlaceKernel(pw,catalog,placeCatalog,Path.Combine(root,"place-deny"),region);pw.Deny=true;Reject(()=>k.Plan("mutsea.test.pavilion","0.1.0",[100,100,25],0,operatorId));pw.Deny=false;Assert(k.Entries.Count==0);});
 // ---- WorldTemplate: places + terrain + water + spawn into an empty region, one journaled operation.
 var worldRoot=Path.Combine(root,"worlds");var placePin=placeCatalog.Get("mutsea.test.pavilion","0.1.0").manifest_sha256!;
 byte[] Flat(float h){var b=new byte[256*256*4];for(int i=0;i<b.Length;i+=4)BitConverter.GetBytes(h).CopyTo(b,i);return b;}
 var terrain=Flat(25f);
 WorldTemplateManifest World(params (string id,double x,double y)[] at)=>new(1,"world","mutsea.test.world","0.1.0","Test World","An original test starter world","CC0-1.0",[256,256],20,ContentCatalog.Hash(terrain),
  "mutsea.starter.harbor-environments@0.1.0#day",new([128,128,25.3],[128,140,25]),at.Select(a=>new WorldPlace(a.id,"mutsea.test.pavilion","0.1.0",placePin,[a.x,a.y,0],0,true)).ToArray());
 void SaveWorld(WorldTemplateManifest w){var d=Path.Combine(worldRoot,"test");Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"world.json"),JsonSerializer.Serialize(w));File.WriteAllBytes(Path.Combine(d,"terrain.r32"),terrain);}
 Test("world catalog pins places and terrain, keeps places apart and inside the region",()=>{
  SaveWorld(World(("a",60,60),("b",120,60)));var wc=new WorldTemplateCatalog(worldRoot,placeCatalog);Assert(wc.Get("mutsea.test.world","0.1.0").world.places.Length==2);
  WorldTemplateCatalog.Validate(World(("a",60,60),("b",70,60)),terrain,placeCatalog);// margins may overlap; live place checks guard parts
  Reject(()=>WorldTemplateCatalog.Validate(World(("a",60,60),("a",120,60)),terrain,placeCatalog));
  Reject(()=>WorldTemplateCatalog.Validate(World(("a",5,60)),terrain,placeCatalog));
  Reject(()=>WorldTemplateCatalog.Validate(World(("a",60,60)) with{places=[World(("a",60,60)).places[0] with{manifest_sha256=new string('0',64)}]},terrain,placeCatalog));
  Reject(()=>WorldTemplateCatalog.Validate(World(("a",60,60)),Flat(float.NaN),placeCatalog));
  Reject(()=>WorldTemplateCatalog.Validate(World(("a",60,60)) with{spawn=new([128,128,25.3],[128,128,80])},terrain,placeCatalog));
  Reject(()=>WorldTemplateCatalog.Validate(World(("a",60,60)),new byte[4],placeCatalog));
  File.WriteAllBytes(Path.Combine(worldRoot,"test","terrain.r32"),Flat(26f));Reject(()=>new WorldTemplateCatalog(worldRoot,placeCatalog));SaveWorld(World(("a",60,60),("b",120,60)));
 });
 var worldCatalog=new WorldTemplateCatalog(worldRoot,placeCatalog);
 FakeRegion Region(string dir){var fp=new FakePlace();return new FakeRegion(new PlaceKernel(fp,catalog,placeCatalog,Path.Combine(root,dir,"places"),region),fp);}
 Test("world provisions terrain, water, places and landing in one operation, then rolls back",()=>{
  var r=Region("world-ok");var k=new WorldTemplateKernel(r,worldCatalog,Path.Combine(root,"world-ok"));var done=k.Provision("mutsea.test.world","0.1.0");
  Assert(done.status=="committed"&&done.place_plans.Length==2&&r.Terrain.SequenceEqual(terrain)&&r.WaterHeight==20&&r.Landing!=null&&r.Fake.Live.Count==8&&k.Active("mutsea.test.world","0.1.0")!=null);
  Assert(r.Places.Entries.Where(e=>e.status=="committed").All(e=>Math.Abs(e.plan.anchor[2]-25)<1e-6));
  var restart=new WorldTemplateKernel(r,worldCatalog,Path.Combine(root,"world-ok"));
  var entity=r.Places.Entries.First(e=>e.status=="committed").plan.placements[0].entity_ids[0];
  Assert(restart.EntityContext(entity) is {} context&&context.operation_id==done.operation_id&&context.manifest_sha256==done.manifest_sha256&&context.world_id=="mutsea.test.world");
  Assert(restart.Rollback(done.operation_id).status=="rolled_back"&&r.Fake.Live.Count==0&&r.Terrain.SequenceEqual(Flat(21f))&&r.WaterHeight==10&&r.Landing==null);
  Assert(restart.EntityContext(entity)==null&&restart.EntityContext(Guid.NewGuid().ToString())==null);
  Assert(!new WorldTemplateKernel(r,worldCatalog,Path.Combine(root,"world-ok")).RecoveryRequired);
 });
 Test("a world only provisions into an empty region, before any write",()=>{var r=Region("world-busy");r.Empty=false;var k=new WorldTemplateKernel(r,worldCatalog,Path.Combine(root,"world-busy"));Reject(()=>k.Provision("mutsea.test.world","0.1.0"));Assert(k.Entries.Count==0&&r.Terrain.SequenceEqual(Flat(21f)));});
 Test("rollback restores an existing parcel's landing position, facing and policy",()=>{
  var r=Region("world-landing");r.SetLanding([40,50,22],[0,-1,0]);var before=r.CaptureLanding([128,128,25]);
  var k=new WorldTemplateKernel(r,worldCatalog,Path.Combine(root,"world-landing"));var done=k.Provision("mutsea.test.world","0.1.0");
  Assert(!r.MatchesLanding(before));k.Rollback(done.operation_id);Assert(r.MatchesLanding(before));
 });
 Test("a failed place compensates the whole world",()=>{
  var r=Region("world-fail");r.Fake.FailAt=6;var k=new WorldTemplateKernel(r,worldCatalog,Path.Combine(root,"world-fail"));
  try{k.Provision("mutsea.test.world","0.1.0");throw new Exception("Expected failure.");}catch(InvalidDataException){}
  Assert(!k.RecoveryRequired&&k.Entries.Last().status=="compensated"&&r.Fake.Live.Count==0&&r.Terrain.SequenceEqual(Flat(21f))&&r.WaterHeight==10);
 });
 Test("world rollback preserves later terrain, water, landing and unrelated objects before any intent",()=>{
  foreach(var change in new[]{"terrain","water","landing","object","early-place"}){
   var r=Region("world-edited-"+change);var k=new WorldTemplateKernel(r,worldCatalog,Path.Combine(root,"world-edited-"+change));var done=k.Provision("mutsea.test.world","0.1.0");
   if(change=="terrain")r.Terrain=Flat(26f);
   if(change=="water")r.SetWaterHeight(19);
   if(change=="landing")r.SetLanding([150,150,26],[160,150,26]);
   if(change=="object")r.Fake.Existing.Add(Guid.NewGuid().ToString());
   // The first place would be rolled back last. Preflight must prevent partial
   // removal of the later place before discovering this earlier edit.
   if(change=="early-place")r.Fake.Live[r.Places.Entries.First(e=>e.status=="committed").plan.placements[0].entity_ids[0]]="edited";
   int records=k.Entries.Count,placeRecords=r.Places.Entries.Count,parts=r.Fake.Live.Count;
   Reject(()=>k.Rollback(done.operation_id));
   Assert(k.Entries.Count==records&&r.Places.Entries.Count==placeRecords&&r.Fake.Live.Count==parts&&!k.RecoveryRequired);
  }
 });
 Test("legacy world journals remain readable but unsafe automatic rollback is refused",()=>{
  var r=Region("world-legacy");var dir=Path.Combine(root,"world-legacy");var k=new WorldTemplateKernel(r,worldCatalog,dir);var done=k.Provision("mutsea.test.world","0.1.0");
  var legacy=done with{landing_before=null,landing_after=null,terrain_after_sha256=null,water_after=null,terrain_hash_format=null};
  File.WriteAllText(Path.Combine(dir,r.RegionId+".worlds.worldline.jsonl"),JsonSerializer.Serialize(legacy)+"\n");
  var reload=new WorldTemplateKernel(r,worldCatalog,dir);Assert(reload.Active("mutsea.test.world","0.1.0")!=null);
  Reject(()=>reload.Rollback(done.operation_id));Assert(r.Fake.Live.Count==8&&reload.Entries.Count==1&&!reload.RecoveryRequired);
 });
 Test("arrival refuses wet, steep and invalid terrain and lifts a valid newcomer above ground",()=>{
  Assert(WorldArrival.StandingPosition(128,128,(_,_)=>25,20)[2]==26.1);
  Reject(()=>WorldArrival.StandingPosition(128,128,(_,_)=>19,20));
  Reject(()=>WorldArrival.StandingPosition(128,128,(x,_)=>x<128?25:26,20));
  Reject(()=>WorldArrival.StandingPosition(128,128,(_,_)=>double.NaN,20));
  Reject(()=>WorldArrival.StandingPosition(0,128,(_,_)=>25,20));
 });
 Test("a world with a submerged arrival compensates without creating places",()=>{
  var wet=World(("a",60,60)) with{water_height=26};SaveWorld(wet);var wc=new WorldTemplateCatalog(worldRoot,placeCatalog);
  var r=Region("world-wet");var k=new WorldTemplateKernel(r,wc,Path.Combine(root,"world-wet"));Reject(()=>k.Provision(wet.world_id,wet.version));
  Assert(k.Entries.Last().status=="compensated"&&r.Fake.Live.Count==0&&r.Terrain.SequenceEqual(Flat(21f))&&r.WaterHeight==10&&r.Landing==null);
 });
 Test("newcomer file: valid accounts and Genesis choices parse",()=>{
  var ok=NewcomerFile.Parse(Encoding.UTF8.GetBytes("{\"accounts\":[{\"first_name\":\"Harbor\",\"last_name\":\"Newcomer\",\"password\":\"correct-horse-1\",\"genesis\":\"/config/genesis/kai-female-v0.1.0\",\"genesis_sha256\":\""+new string('a',64)+"\"},{\"first_name\":\"Second\",\"last_name\":\"Visitor2\",\"password\":\"correct-horse-2\"}]}"));
  Assert(ok.accounts.Length==2&&ok.accounts[1].genesis==null);
 });
 Test("newcomer file: bad names, weak passwords, half choices and duplicates are refused without echoing content",()=>{
  string One(string first,string last,string password,string extra="")=>"{\"accounts\":[{\"first_name\":\""+first+"\",\"last_name\":\""+last+"\",\"password\":\""+password+"\""+extra+"}]}";
  foreach(var bad in new[]{One("1Harbor","Newcomer","correct-horse-1"),One("Harbor","New comer","correct-horse-1"),One("Harbor","Newcomer","short"),
   One("Harbor","Newcomer","correct-horse-1",",\"genesis\":\"/config/genesis/x\""),One("Harbor","Newcomer","correct-horse-1",",\"genesis\":\"relative\",\"genesis_sha256\":\""+new string('a',64)+"\""),
   "{\"accounts\":[]}","{\"accounts\":[{\"first_name\":\"A\",\"last_name\":\"B\",\"password\":\"correct-horse-1\"},{\"first_name\":\"a\",\"last_name\":\"b\",\"password\":\"correct-horse-2\"}]}"})
   Reject(()=>NewcomerFile.Parse(Encoding.UTF8.GetBytes(bad)));
  try{NewcomerFile.Parse(Encoding.UTF8.GetBytes("{\"accounts\":[{\"password\":\"secret-value-xyz\""));throw new Exception("Expected rejection.");}
  catch(InvalidDataException ex){Assert(!ex.Message.Contains("secret-value-xyz"));}
 });
 Console.WriteLine($"{passed} content tests passed");return 0;
}finally{Directory.Delete(root,true);}
sealed class Fake:IContentWorld{public bool Deny,DenyDelete,FailCreate,PartialCreate,FailFingerprint;public string? Value;public Action? BeforeCreate;public void Check(ContentPlan plan){if(Deny)throw new InvalidDataException("Native policy denied.");}public void CheckDelete(ContentPlan plan){if(DenyDelete)throw new InvalidDataException("Native deletion denied.");}public string Create(ContentPlan plan,byte[][] meshes,byte[][] materials){BeforeCreate?.Invoke();if(FailCreate){if(PartialCreate)Value="partial";throw new InvalidDataException("Simulated insertion failure.");}return Value="original";}public string? Fingerprint(ContentPlan plan)=>FailFingerprint?throw new InvalidDataException("State observation failed."):Value;public void Delete(ContentPlan plan){Value=null;}}

sealed class FakePlace:IPlaceWorld{public bool Deny,StuckDelete;public int FailAt=-1;public Dictionary<string,string> Live=new();public HashSet<string> Existing=new();
 public void Check(ContentPlan plan){}
 public void CheckPlace(PlacePlan plan){if(Deny)throw new InvalidDataException("Native place policy denied.");}
 public string Create(ContentPlan plan,byte[][] meshes,byte[][] materials){if(FailAt>=0&&Live.Count==FailAt)throw new InvalidDataException("Simulated placement failure.");return Live[plan.entity_ids[0]]="fp";}
 public string? Fingerprint(ContentPlan plan)=>Live.TryGetValue(plan.entity_ids[0],out var v)?v:null;
 public void Delete(ContentPlan plan){if(!StuckDelete)Live.Remove(plan.entity_ids[0]);}
 public PlaceRetirement[] Retire(PlacePlan plan){foreach(var r in plan.retired)Existing.Remove(r.group_id);return plan.retired.Select(r=>r with{snapshot_sha256=new string('a',64)}).ToArray();}
 public void Restore(PlacePlan plan){foreach(var r in plan.retired)Existing.Add(r.group_id);}}

sealed class FakeRegion(PlaceKernel places,FakePlace fake):IWorldTemplateRegion{
 public string RegionId{get;}=Guid.NewGuid().ToString();public bool Empty=true;public byte[] Terrain=Flat(21f);public double WaterHeight{get;private set;}=10;public double[]? Landing;WorldLanding? landing;
 public FakePlace Fake=>fake;public PlaceKernel Places=>places;public string Operator{get;}=Guid.NewGuid().ToString();
 static byte[] Flat(float h){var b=new byte[256*256*4];for(int i=0;i<b.Length;i+=4)BitConverter.GetBytes(h).CopyTo(b,i);return b;}
 public bool IsEmpty()=>Empty;public string SnapshotTerrain(string path){File.WriteAllBytes(path,Terrain);return ContentCatalog.Hash(Terrain);}
 public bool IsEmptyExcept(string[] roots)=>fake.Existing.Count==0&&fake.Live.Keys.All(roots.Contains);
 public string TerrainHashFormat=>"test-r32-v1";
 public string TerrainHash()=>ContentCatalog.Hash(Terrain);
 public void LoadTerrain(byte[] r32)=>Terrain=r32.ToArray();public void SetWaterHeight(double h)=>WaterHeight=h;
 public void SetLanding(double[] p,double[] l){Landing=p;landing=new("test-parcel",p,l,1);}
 public WorldLanding CaptureLanding(double[] p)=>landing??new("test-parcel",[0,0,0],[0,0,0],0);
 public bool MatchesLanding(WorldLanding expected)=>JsonSerializer.Serialize(CaptureLanding(expected.position))==JsonSerializer.Serialize(expected);
 public void RestoreLanding(WorldLanding before){landing=before;Landing=before.landing_type==0?null:before.position;}
 public double Ground(double x,double y)=>BitConverter.ToSingle(Terrain,((int)y*256+(int)x)*4);}
