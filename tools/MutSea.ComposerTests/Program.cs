// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
int passed=0;void Test(string name,System.Action check){check();passed++;Console.WriteLine("PASS "+name);}
void Assert(bool value){if(!value)throw new Exception("Assertion failed.");}
var w=new Terrain();
var place=new PlaceTemplate(1,"place","user.test.garden","0.1.0","Garden","Original garden","CC0-1.0",[3,3],[],["walk"],[],[new("entry",[0,-4,0],0)]);
Test("legacy recipe retains its one exact forward site",()=>{var sites=ComposerSiting.Candidates(w,place,"actor","in_front_of_actor").ToArray();Assert(sites.Length==1&&sites[0].anchor.SequenceEqual(new[]{69d,64d,25d}));});
Test("nearby search is deterministic and bounded",()=>{var a=ComposerSiting.Candidates(w,place,"actor","near_actor").ToArray();var b=ComposerSiting.Candidates(w,place,"actor","near_actor").ToArray();Assert(a.Length==ComposerSiting.MaxCandidates&&a.Select(s=>string.Join(",",s.anchor)).SequenceEqual(b.Select(s=>string.Join(",",s.anchor))));});
Test("shoreline comes from actual wet terrain",()=>{w.Sample=(x,y)=>x>=83?19:25;var sites=ComposerSiting.Candidates(w,place,"actor","near_water").ToArray();Assert(sites.Length>0&&sites.All(s=>s.anchor[0]<80&&s.water_distance is <=12));});
Test("dry world cannot fake a lake",()=>{w.Sample=(x,y)=>25;Assert(!ComposerSiting.Candidates(w,place,"actor","near_water").Any());});
Test("underwater and steep places refused",()=>{w.Sample=(x,y)=>19;Assert(!ComposerSiting.Candidates(w,place,"actor","near_actor").Any());w.Sample=(x,y)=>25+x;Assert(!ComposerSiting.Candidates(w,place,"actor","near_actor").Any());});
Test("region edges and nonfinite terrain refused",()=>{w.Sample=(x,y)=>25;w.Position=[2,2,25];Assert(ComposerSiting.Candidates(w,place,"actor","near_actor").All(s=>ComposerSiting.Fits(w,place,s.anchor,s.yaw_degrees)));w.Sample=(x,y)=>double.NaN;Assert(!ComposerSiting.Candidates(w,place,"actor","near_actor").Any());});
Test("changed approved terrain fails revalidation",()=>{w.Position=[64,64,25];w.Sample=(x,y)=>25;var s=ComposerSiting.Candidates(w,place,"actor","near_actor").First();Assert(ComposerSiting.Fits(w,place,s.anchor,s.yaw_degrees));w.Sample=(x,y)=>19;Assert(!ComposerSiting.Fits(w,place,s.anchor,s.yaw_degrees));});
Test("unbounded site rules refused",()=>{try{ComposerSiting.Candidates(w,place,"actor","arbitrary_coordinates").ToArray();}catch(InvalidDataException){return;}throw new Exception("Expected refusal");});
var now=DateTimeOffset.UtcNow;
var plan=new PlacePlan("mine","actor",place,[64,64,25],0,[],[]);
var committed=new PlaceEntry("commit","committed",now,plan,[],null);
var other=new PlaceEntry("other","committed",now.AddSeconds(1),plan with {plan_id="other",actor_id="stranger"},[],null);
Test("Undo recovery selects only active owned newer places",()=>{
 Assert(ComposerAudit.LatestPlace([committed,other],"actor",now.AddSeconds(-1))=="mine");
 Assert(ComposerAudit.LatestPlace([committed],"actor",now)==null);
 Assert(ComposerAudit.LatestPlace([committed,committed with {status="rolled_back",timestamp=now.AddSeconds(1)}],"actor",DateTimeOffset.MinValue)==null);
});
Test("human history joins journals without leaking another actor",()=>{
 var action=new Entry("action-op","action","actor",now.AddSeconds(-1),"committed",new(1,"Move bench","test","test",[]),[],null,null);
 var history=ComposerAudit.History([action],[committed,other,committed with {status="rolled_back",timestamp=now.AddSeconds(2)}],"actor");
 Assert(history.Length==3&&history[0].GetProperty("plan_id").GetString()=="action"&&history[2].GetProperty("status").GetString()=="undone");
 Assert(history[1].GetProperty("proposal").GetProperty("request").GetString()=="Garden");
});
Test("disabled or absent citizens never offer a relay",()=>{
 Assert(!ExperienceAvailability.Citizen(false,true,true,true,false).can_interact);
 Assert(ExperienceAvailability.Citizen(true,false,true,true,false) is {present:false,can_interact:false,state:"not_in_world"});
});
Test("seeing Lumi does not grant controller privileges",()=>{
 Assert(ExperienceAvailability.Citizen(true,true,false,true,false) is {present:true,can_interact:false,state:"not_controller"});
 Assert(ExperienceAvailability.Citizen(true,true,true,true,false) is {present:true,can_interact:true,state:"enabled"});
});
Test("starting and failed representations keep citizen controls closed",()=>{
 Assert(ExperienceAvailability.Citizen(true,true,true,false,false).state=="starting");
 Assert(ExperienceAvailability.Citizen(true,true,true,true,true) is {can_interact:false,state:"representation_recovery"});
});
Console.WriteLine($"{passed} composer/discovery checks passed; no world mutation API called.");
sealed class Terrain:IWorld
{
 public Func<double,double,double> Sample=(x,y)=>25;public double[] Position=[64,64,25];
 public string RegionId=>"test";public string RegionName=>"Test";public double WaterHeight=>20;public double SizeX=>256;public double SizeY=>256;
 public double[] Avatar(string actor)=>Position;public double[] Orientation(string actor)=>[0,0,0,1];
 public double Height(double x,double y)=>Sample(x,y);public bool CanCreate(string actor,int count,double[] at)=>false;
 public Entity? Find(string id,string actor)=>null;public IEnumerable<Entity> Entities(string actor)=>[];
 public void Apply(Step step,string actor)=>throw new Exception("Site search cannot mutate world state");
}
