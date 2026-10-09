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
 Console.WriteLine($"{passed} content tests passed");return 0;
}finally{Directory.Delete(root,true);}
sealed class Fake:IContentWorld{public bool Deny,DenyDelete,FailCreate,PartialCreate,FailFingerprint;public string? Value;public Action? BeforeCreate;public void Check(ContentPlan plan){if(Deny)throw new InvalidDataException("Native policy denied.");}public void CheckDelete(ContentPlan plan){if(DenyDelete)throw new InvalidDataException("Native deletion denied.");}public string Create(ContentPlan plan,byte[][] meshes,byte[][] materials){BeforeCreate?.Invoke();if(FailCreate){if(PartialCreate)Value="partial";throw new InvalidDataException("Simulated insertion failure.");}return Value="original";}public string? Fingerprint(ContentPlan plan)=>FailFingerprint?throw new InvalidDataException("State observation failed."):Value;public void Delete(ContentPlan plan){Value=null;}}
