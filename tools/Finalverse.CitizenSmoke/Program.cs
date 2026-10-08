// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Live private sandbox probe. Does not print passwords, service tokens or capability URLs.
using System.Text;
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length!=3||args[2] is not ("observe" or "questions" or "navigate" or "verify" or "garden_verify" or "garden_undo_verify")){Console.Error.WriteLine("Usage: CitizenSmoke private-account.json receipt.json observe|questions|navigate|verify|garden_verify|garden_undo_verify");return 2;}
var account=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;var client=new GridClient();
client.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
using var http=new HttpClient {Timeout=TimeSpan.FromSeconds(110)};string cap="";UUID lumi=new("b151468b-cbcc-4af2-ae2d-6967e8938bb4");
void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
async Task<JsonElement> Citizen(string command,string text="")
{
 return await World(new{op="citizen",command,text});
}
async Task<JsonElement> World(object request)
{
 await Task.Delay(160);var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(request),Encoding.UTF8,"application/json"));var root=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
 Assert(response.IsSuccessStatusCode&&root.GetProperty("ok").GetBoolean(),"Authenticated citizen relay failed");return root.GetProperty("result").Clone();
}
void Save(object result){File.WriteAllText(args[1],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);}
try
{
 Assert(client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"Finalverse.CitizenSmoke","home","0.1"),"Sandbox login failed");
 var seed=new OSDArray{OSD.FromString("FinalverseWorld")};var content=new StringContent(OSDParser.SerializeLLSDXmlString(seed),Encoding.UTF8);content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
 var seedReply=await http.PostAsync(client.Network.CurrentSim.Caps.SeedCapsURI,content);var caps=(OSDMap)OSDParser.DeserializeLLSDXml(await seedReply.Content.ReadAsStringAsync());cap=caps["FinalverseWorld"].AsString();Assert(cap.Length>0,"World capability missing");
 var deadline=DateTime.UtcNow.AddSeconds(30);Primitive? rootBody=null;
 do{rootBody=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==lumi);if(rootBody!=null)break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
 Assert(rootBody!=null,"No Lumi embodiment in real UDP stream");await Task.Delay(500);
 int components=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Count(p=>p.ID==lumi||p.ParentID==rootBody!.LocalID);Assert(components==7,"Lumi's seven linked components are missing from UDP");
 var state=await Citizen("inspect");Assert(state.GetProperty("citizen").GetProperty("identity").GetProperty("agent_id").GetString()==lumi.ToString(),"Persistent identity mismatch");
 if(args[2]=="questions")
 {
  var results=new List<object>();foreach(string text in new[]{"Lumi, who are you?","Where is your home?","What do you remember about your home?"}){var watch=System.Diagnostics.Stopwatch.StartNew();var reply=await Citizen("say",text);Assert(!reply.GetProperty("answer").GetString()!.Contains("unavailable",StringComparison.OrdinalIgnoreCase),"Grounded question unavailable");results.Add(new{text,latency_ms=watch.ElapsedMilliseconds,reply});}
  Save(new{udp_components=components,state,questions=results});Console.WriteLine("CITIZEN_SMOKE questions PASS: authenticated viewer capability relay, actual UDP embodiment, persistent identity/home/memory answers.");
 }
 else if(args[2]=="navigate")
 {
  var resumed=await Citizen("resume");var goals=resumed.GetProperty("citizen").GetProperty("goals");if(goals.GetArrayLength()==0||goals[goals.GetArrayLength()-1].GetProperty("status").GetString()!="active")await Citizen("say","Lumi, go home.");
  var initial=rootBody!.Position;bool moved=false;deadline=DateTime.UtcNow.AddSeconds(90);
  do
  {
   await Task.Delay(700);state=await Citizen("inspect");var body=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi);if(Vector3.Distance(body.Position,initial)>1)moved=true;
   var goal=state.GetProperty("citizen").GetProperty("goals");string status=goal[goal.GetArrayLength()-1].GetProperty("status").GetString()!;if(status=="completed")break;if(status is "blocked" or "failed")throw new Exception("Navigation blocked: "+goal[goal.GetArrayLength()-1].GetProperty("reason").GetString());
  }while(DateTime.UtcNow<deadline);
  var last=state.GetProperty("citizen").GetProperty("goals");Assert(last[last.GetArrayLength()-1].GetProperty("status").GetString()=="completed","Arrival timeout");Assert(moved,"Lumi did not move in real UDP updates");
  Save(new{udp_components=components,udp_motion_verified=moved,state});Console.WriteLine("CITIZEN_SMOKE navigation PASS: semantic home, bounded goal, real UDP movement, arrival and durable event memory.");
 }
 else if(args[2] is "garden_verify" or "garden_undo_verify")
 {
  // Read only: never propose, approve or undo a garden on the user's behalf.
  var journal=await World(new{op="journal"});JsonElement committed;
  if(args[2]=="garden_undo_verify")
  {
   committed=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("committed").Clone();
   Assert(journal.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==committed.GetProperty("plan_id").GetString()&&e.GetProperty("status").GetString()=="undone"),"Garden Undo receipt is missing");
   Assert(state.GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("subject").GetString()=="garden_undo"),"Garden Undo memory is missing");
  }
  else
  {
   var undone=journal.EnumerateArray().Where(e=>e.GetProperty("status").GetString()=="undone").Select(e=>e.GetProperty("plan_id").GetString()).ToHashSet();
   committed=journal.EnumerateArray().Last(e=>e.GetProperty("status").GetString()=="committed"&&!undone.Contains(e.GetProperty("plan_id").GetString())&&e.GetProperty("proposal").GetProperty("model").GetString()=="garden-v1");
   Assert(state.GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("subject").GetString()=="garden"),"Garden episodic memory is missing");
   Assert(state.GetProperty("citizen").GetProperty("goals").EnumerateArray().Any(g=>g.GetProperty("kind").GetString()=="garden"&&g.GetProperty("status").GetString()=="completed"&&g.GetProperty("plan_id").GetString()==committed.GetProperty("plan_id").GetString()),"Completed garden goal is missing");
   if(File.Exists(args[1]))Assert(JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("committed").GetProperty("operation_id").GetString()==committed.GetProperty("operation_id").GetString(),"Garden changed after restart");
  }
  var steps=committed.GetProperty("steps");Assert(steps.GetArrayLength()==3,"Garden must contain exactly three shrubs");
  bool exists=args[2]=="garden_verify";
  foreach(var step in steps.EnumerateArray())
  {
   UUID id=new(step.GetProperty("id").GetString());deadline=DateTime.UtcNow.AddSeconds(20);Primitive? shrub;
   do{shrub=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==id);if((shrub!=null)==exists)break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
   Assert((shrub!=null)==exists,"Garden UDP existence mismatch");
   if(exists)
   {
    var after=step.GetProperty("after");var pos=after.GetProperty("position");var size=after.GetProperty("scale");
    Assert(Vector3.Distance(shrub!.Position,new(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle()))<.02f,"Garden UDP position mismatch");
    Assert(Vector3.Distance(shrub.Scale,new(size[0].GetSingle(),size[1].GetSingle(),size[2].GetSingle()))<.02f,"Garden UDP scale mismatch");
   }
  }
  var recall=await Citizen("say","What did you change here?");
  Assert(recall.GetProperty("answer").GetString()!.Contains("garden",StringComparison.OrdinalIgnoreCase),"Garden recall failed");
  if(exists)Save(new{udp_components=components,garden_udp_verified=true,committed,state,recall});
  else{File.WriteAllText(args[1]+".undo",JsonSerializer.Serialize(new{garden_undo_udp_verified=true,state,recall}));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1]+".undo",UnixFileMode.UserRead|UnixFileMode.UserWrite);}
  Console.WriteLine("CITIZEN_SMOKE "+args[2]+" PASS: real UDP garden state, goal/memory, kernel WorldLine and grounded recall.");
 }
 else if(args[2]=="verify")
 {
  var previous=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("state").GetProperty("citizen");var now=state.GetProperty("citizen");
  foreach(string field in new[]{"agent_id","controller_id","embodiment_id","home_entity_id","created_at","autonomy_level"})Assert(previous.GetProperty("identity").GetProperty(field).ToString()==now.GetProperty("identity").GetProperty(field).ToString(),"Restart identity mismatch: "+field);
  foreach(var goal in previous.GetProperty("goals").EnumerateArray())Assert(now.GetProperty("goals").EnumerateArray().Any(g=>g.GetProperty("goal_id").GetString()==goal.GetProperty("goal_id").GetString()),"Restart lost goal history");
  var home=now.GetProperty("identity").GetProperty("home_entity_id").GetString();Assert(now.GetProperty("relationships").EnumerateArray().Any(r=>r.GetProperty("relationship_type").GetString()=="home"&&r.GetProperty("target_id").GetString()==home),"Restart lost home relationship");
  Assert(state.GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("subject").GetString()=="home_creation"),"Restart lost home event memory");
  Console.WriteLine("CITIZEN_SMOKE restart PASS: identity, autonomy, embodiment UDP, semantic home relationship, memories and goal history.");
 }
 else{Save(new{udp_components=components,state});Console.WriteLine("CITIZEN_SMOKE observation PASS: authenticated capability relay and seven real UDP embodiment components.");}
 return 0;
}
catch(Exception ex){Console.Error.WriteLine("CITIZEN_SMOKE FAIL: "+ex.Message);return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
