// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
using System.Text.Json;

int passed=0;
void Test(string name,Action run) { run();passed++;Console.WriteLine("PASS "+name); }
void Check(bool value) { if(!value)throw new Exception("Assertion failed"); }
void Reject(Action run) { try {run();}catch(InvalidDataException){return;}throw new Exception("Expected policy rejection"); }
Plan Plan(string type,object p,string? target=null)=>new(1,"test request","mock","deterministic-fixture",[new(type,target,JsonSerializer.SerializeToElement(p))]);
string actor=Guid.NewGuid().ToString(), cube=Guid.NewGuid().ToString();
var world=new Fake(actor);
Entity original=new(cube,"Cube","original",actor,"box",[170,151,26],[0,0,0,1],[1,1,1],[.4,.26,.68,1],true,true,true);
world.Objects[cube]=original;
string path=Path.Combine(Path.GetTempPath(),"finalverse-tests-"+Guid.NewGuid());
var kernel=new Kernel(world,path);
Test("inspect selected and radius",()=>Check(JsonSerializer.Serialize(kernel.Inspect(actor,cube)).Contains("Cube")));
Test("out of scope selection",()=>{world.Objects[cube]=original with {position=[10,10,26]};Reject(()=>kernel.Inspect(actor,cube));world.Objects[cube]=original;});
Test("unknown action",()=>Reject(()=>kernel.Prepare(Plan("shell",new{},cube),actor)));
Test("unknown parameter",()=>Reject(()=>kernel.Prepare(Plan("move_entity",new {position=new[]{170,152,26},admin=true},cube),actor)));
Test("non finite geometry",()=>Reject(()=>kernel.Prepare(Plan("move_entity",new {position=new[]{1e300,152,26}},cube),actor)));
Test("excessive scale",()=>Reject(()=>kernel.Prepare(Plan("scale_entity",new {scale=new[]{100,1,1}},cube),actor)));
Test("too many actions",()=>Reject(()=>kernel.Prepare(Plan("move_entity",new {position=new[]{170,152,26}},cube) with {actions=Enumerable.Repeat(Plan("move_entity",new {position=new[]{170,152,26}},cube).actions[0],9).ToArray()},actor)));
Test("unauthorized move",()=>{world.Objects[cube]=original with {owner_id=Guid.NewGuid().ToString()};Reject(()=>kernel.Prepare(Plan("move_entity",new {position=new[]{170,152,26}},cube),actor));world.Objects[cube]=original;});
Test("rejected delete inherited object",()=>Reject(()=>kernel.Prepare(Plan("delete_entity",new {},cube),actor)));
Test("rejected create permission",()=>{world.CanBuild=false;Reject(()=>kernel.Prepare(Plan("create_primitive",new {position=new[]{175,151,26},scale=new[]{1,1,1},color=new[]{1,1,1,1},shape="box",name="test"}),actor));world.CanBuild=true;});
string moved="";
Test("authorized move and idempotent commit",()=>{var p=kernel.Prepare(Plan("move_entity",new {position=new[]{170,153,26}},cube),actor);moved=p.plan_id;Check(kernel.Commit(moved,actor).status=="committed");Check(kernel.Commit(moved,actor).status=="committed");Check(world.Objects[cube].position[1]==153);});
Test("undo conflict",()=>{world.Objects[cube]=world.Objects[cube] with {position=[170,154,26]};Reject(()=>kernel.Undo(moved,actor));world.Objects[cube]=world.Objects[cube] with {position=[170,153,26]};});
Test("guarded undo",()=>{Check(kernel.Undo(moved,actor).status=="undone");Check(Kernel.Same(world.Objects[cube],original));});
Test("deterministic relative movement",()=>{string reference=Guid.NewGuid().ToString();world.Objects[reference]=original with {id=reference,position=[180,161,26]};var p=kernel.Prepare(Plan("move_entity",new{toward_id=reference,distance=2},cube),actor);Check(Math.Abs(p.steps[0].after!.position[0]-170-Math.Sqrt(2))<.001);Check(kernel.Commit(p.plan_id,actor).status=="committed");Check(kernel.Undo(p.plan_id,actor).status=="undone");world.Objects.Remove(reference);});
Test("missing spatial reference",()=>Reject(()=>kernel.Prepare(Plan("move_entity",new{toward_id=Guid.NewGuid().ToString(),distance=2},cube),actor)));
Test("stale preparation",()=>{var p=kernel.Prepare(Plan("set_name",new{name="changed"},cube),actor);world.Objects[cube]=original with {name="another edit"};Reject(()=>kernel.Commit(p.plan_id,actor));world.Objects[cube]=original;});
Test("session actor mismatch",()=>{var p=kernel.Prepare(Plan("set_name",new{name="changed"},cube),actor);Reject(()=>kernel.Commit(p.plan_id,Guid.NewGuid().ToString()));});
Test("session invalidates approval",()=>{var p=kernel.Prepare(Plan("set_name",new{name="changed"},cube),actor);kernel.InvalidateSession(actor);Reject(()=>kernel.Commit(p.plan_id,actor));});
Test("rotation name description and color",()=>{
 foreach(var plan in new[]{Plan("rotate_entity",new{rotation=new[]{0,0,1,0}},cube),Plan("set_name",new{name="Renamed"},cube),Plan("set_description",new{description="Semantic note"},cube),Plan("set_color",new{color=new[]{.1,.2,.3,1}},cube),Plan("scale_entity",new{scale=new[]{2,1,1}},cube)})
 {var p=kernel.Prepare(plan,actor);Check(kernel.Commit(p.plan_id,actor).status=="committed");Check(kernel.Undo(p.plan_id,actor).status=="undone");}
});
Test("boundary rejected",()=>Reject(()=>kernel.Prepare(Plan("create_primitive",new{position=new[]{255,255,26},scale=new[]{5,5,5},color=new[]{1,1,1,1},shape="box",name="bad"}),actor)));
Test("primitive create clone delete and undo",()=>{
 var p=kernel.Prepare(Plan("create_primitive",new{position=new[]{175,154,26},scale=new[]{1,1,1},color=new[]{.1,.2,.3,1},shape="box",name="AI Cube"}),actor);Check(kernel.Commit(p.plan_id,actor).status=="committed");string id=p.steps[0].id;
 var copy=kernel.Prepare(Plan("clone_entity",new{position=new[]{177,154,26}},id),actor);Check(kernel.Commit(copy.plan_id,actor).status=="committed");Check(kernel.Undo(copy.plan_id,actor).status=="undone");
 var delete=kernel.Prepare(Plan("delete_entity",new{},id),actor);Check(kernel.Commit(delete.plan_id,actor).status=="committed"&&world.Find(id,actor)==null);Check(kernel.Undo(delete.plan_id,actor).status=="undone");Check(kernel.Undo(p.plan_id,actor).status=="undone");
});
Test("manual primitive changes block destructive actions and undo",()=>{
 var p=kernel.Prepare(Plan("create_primitive",new{position=new[]{175,154,26},scale=new[]{1,1,1},color=new[]{.1,.2,.3,1},shape="box",name="AI Cube"}),actor);Check(kernel.Commit(p.plan_id,actor).status=="committed");string id=p.steps[0].id;var before=world.Objects[id];
 world.Objects[id]=before with {copyable=false};
 Reject(()=>kernel.Prepare(Plan("clone_entity",new{position=new[]{177,154,26}},id),actor));Reject(()=>kernel.Prepare(Plan("delete_entity",new{},id),actor));Reject(()=>kernel.Undo(p.plan_id,actor));
 world.Objects[id]=before;Check(kernel.Undo(p.plan_id,actor).status=="undone");
});
Test("volume budget",()=>Reject(()=>kernel.Prepare(Plan("create_primitive",new{position=new[]{175,154,26},scale=new[]{12,12,12},color=new[]{1,1,1,1},shape="box",name="too large"}),actor)));
Test("submerged home rejected",()=>{world.Terrain=17;Reject(()=>kernel.Prepare(Plan("create_structure",new{anchor=new[]{172,154,17},width=6,depth=5,height=3,yaw=0,owner_agent_id=Kernel.LumiId}),actor));world.Terrain=25;});
world.Objects.Clear();
Plan Home()=>Plan("create_structure",new {anchor=new[]{172,154,25},width=6,depth=5,height=3,yaw=0,owner_agent_id=Kernel.LumiId});
Test("slope rejected",()=>{world.Slope=true;Reject(()=>kernel.Prepare(Home(),actor));world.Slope=false;});
Test("overlap preserves paths",()=>{world.Objects[cube]=original with {position=[172,154,25]};Reject(()=>kernel.Prepare(Home(),actor));world.Objects.Clear();});
Test("clearance includes linked child extents",()=>{world.Objects[cube]=original with {position=[185,154,25],clearance_radius=12,editable=false};Reject(()=>kernel.Prepare(Home(),actor));world.Objects.Clear();});
Test("placement invalidated by intervening scene edit",()=>{var p=kernel.Prepare(Home(),actor);world.Objects[cube]=original;Reject(()=>kernel.Commit(p.plan_id,actor));world.Objects.Clear();});
string home="";
Test("deterministic generator bounded geometry",()=>{var p=kernel.Prepare(Home(),actor);home=p.plan_id;Check(p.steps.Length==17&&p.steps.Select(s=>s.id).Distinct().Count()==17);Check(p.steps.All(s=>s.after!.position.All(double.IsFinite)));Check(kernel.Commit(home,actor).status=="committed"&&world.Objects.Count==17);});
Test("semantic home owner and Lumi memory",()=>{Check(JsonSerializer.Serialize(kernel.Lumi(actor)).Contains(world.Objects.Keys.First()));Check(JsonSerializer.Serialize(kernel.Semantic(world.Objects.Keys.First(),actor)).Contains(Kernel.LumiId));});
Test("home component deletion is blocked",()=>Reject(()=>kernel.Prepare(Plan("delete_entity",new{},world.Objects.Keys.First()),actor)));
Test("journal and relationships survive restart",()=>{kernel=new Kernel(world,path);Check(!kernel.RecoveryRequired);Check(kernel.History(actor).Any(e=>e.plan_id==home));Check(JsonSerializer.Serialize(kernel.Lumi(actor)).Contains("test request"));});
Test("home undo",()=>{Check(kernel.Undo(home,actor).status=="undone");Check(world.Objects.Count==0);});
Test("partial failure compensates",()=>{var p=kernel.Prepare(Home(),actor);world.FailAt=3;var e=kernel.Commit(p.plan_id,actor);Check(e.status=="rolled_back"&&world.Objects.Count==0);});
Test("failed compensation locks writes",()=>{var p=kernel.Prepare(Home(),actor);world.FailAt=3;world.FailDeletes=true;Check(kernel.Commit(p.plan_id,actor).status=="recovery_required");Check(kernel.RecoveryRequired);Reject(()=>kernel.Prepare(Home(),actor));kernel=new Kernel(world,path);Check(kernel.RecoveryRequired);});
Test("compensation preserves a concurrent edit",()=>{var other=new Fake(actor){FailAt=3,EditOnFailure=true};var k=new Kernel(other,Path.Combine(path,"concurrent"));var p=k.Prepare(Home(),actor);Check(k.Commit(p.plan_id,actor).status=="recovery_required");Check(other.Objects.Values.Any(e=>e.name=="Concurrent edit"));});
Test("audit write failure blocks future mutations",()=>{string directory=Path.Combine(path,"audit-failure");var k=new Kernel(new Fake(actor),directory);var p=k.Prepare(Home(),actor);Directory.CreateDirectory(Path.Combine(directory,world.RegionId+".worldline.jsonl"));try{k.Commit(p.plan_id,actor);throw new Exception("Expected IO failure");}catch(UnauthorizedAccessException){}Check(k.RecoveryRequired);Reject(()=>k.Prepare(Home(),actor));});
Test("interrupted intent locks after restart",()=>{var other=Path.Combine(path,"interrupted");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,world.RegionId+".worldline.jsonl"),JsonSerializer.Serialize(new Entry(Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),actor,DateTimeOffset.UtcNow,"intent",Home(),[],null,null))+"\n");Check(new Kernel(world,other).RecoveryRequired);});
Console.WriteLine($"WORLD_TESTS {passed} passed");return 0;

sealed class Fake(string actor):IWorld
{
    public Dictionary<string,Entity> Objects=new();
    public bool CanBuild=true,FailDeletes,Slope,EditOnFailure;
    public int FailAt;
    public double Terrain=25;
    public string RegionId=>"test-region";public string RegionName=>"Test";public double WaterHeight=>20;public double SizeX=>256;public double SizeY=>256;
    public double[] Avatar(string a)=>[172,154,27];
    public double[] Orientation(string a)=>[0,0,0,1];
    public Entity? Find(string id,string a)=>Objects.GetValueOrDefault(id);
    public IEnumerable<Entity> Entities(string a)=>Objects.Values;
    public double Height(double x,double y)=>Terrain+(Slope?(x-172):0);
    public bool CanCreate(string a,int count,double[] p)=>CanBuild;
    public void Apply(Step s,string a)
    {if(FailAt>0&&--FailAt==0){if(EditOnFailure){var first=Objects.First();Objects[first.Key]=first.Value with {name="Concurrent edit"};}throw new IOException("injected failure");}if(s.after==null){if(FailDeletes)throw new IOException("injected compensation failure");Objects.Remove(s.id);}else Objects[s.id]=s.after;}
}
