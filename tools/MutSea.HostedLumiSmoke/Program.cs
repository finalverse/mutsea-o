// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Real session + native policy + UDP + WorldLine proof on the named private pilot.
using System.Text;
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length!=3||args[2] is not ("spatial" or "citizen" or "verify" or "undo"))throw new ArgumentException("Expected private account, receipt and spatial|citizen|verify|undo.");
if(OperatingSystem.IsWindows())throw new PlatformNotSupportedException("This private pilot probe requires Unix file permissions.");
if((File.GetUnixFileMode(args[0])&(UnixFileMode.GroupRead|UnixFileMode.OtherRead))!=0)throw new IOException("Private account file required.");
var account=JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
if(account.GetProperty("grid_url").GetString()!="https://grid.mutsea.com/"||account.GetProperty("fixture").GetString()!="maple-early-access-20261015"||account.GetProperty("first_name").GetString()!="Pilot"||account.GetProperty("last_name").GetString()!="Builder")throw new InvalidDataException("Only the named private pilot fixture is allowed.");
if(args[2] is "spatial" or "citizen"&&File.Exists(args[1]))throw new IOException("Preserve prior receipt.");
void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
void Save(object receipt){File.WriteAllText(args[1],JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);}
System.Net.WebRequest.DefaultWebProxy=null; // Only this private probe bypasses the host proxy.
var client=new GridClient();Settings.LOG_LEVEL=Helpers.LogLevel.Error;
client.Settings.LOGIN_SERVER="https://grid.mutsea.com/";client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.SEND_AGENT_UPDATES=true;
client.Self.Movement.Camera.Far=512;
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=TimeSpan.FromSeconds(65)};
string cap="";string? applied=null;bool undone=false;string stage="login";
async Task<JsonElement> World(object body,bool reject=false){
    await Task.Delay(150);
    using var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json"));
    if(reject){
        var bodyText=await response.Content.ReadAsStringAsync();
        // The inherited server removes the capability route on logout and
        // answers an unmatched POST with an empty 200. It exposes no operation.
        if(response.IsSuccessStatusCode&&bodyText.Length==0)return JsonSerializer.SerializeToElement(new{rejected=true,route_removed=true,status=(int)response.StatusCode});
        if((int)response.StatusCode is not (400 or 403 or 404 or 410)){
            File.WriteAllText(args[1]+".rejected-response.json",JsonSerializer.Serialize(new{status=(int)response.StatusCode,content_type=response.Content.Headers.ContentType?.ToString(),body=bodyText}));
            File.SetUnixFileMode(args[1]+".rejected-response.json",UnixFileMode.UserRead|UnixFileMode.UserWrite);
            throw new InvalidDataException("Unexpected native rejection format");
        }
        return JsonSerializer.SerializeToElement(new{rejected=true,status=(int)response.StatusCode});
    }
    var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    if(!response.IsSuccessStatusCode||!result.GetProperty("ok").GetBoolean()){
        File.WriteAllText(args[1]+".failure.json",result.GetRawText());File.SetUnixFileMode(args[1]+".failure.json",UnixFileMode.UserRead|UnixFileMode.UserWrite);
        throw new InvalidDataException("Native operation rejected during "+stage);
    }
    return result.GetProperty("result").Clone();
}
async Task<JsonElement> Citizen(string command,string text="")=>await World(new{op="citizen",command,text});
void Patch(object value,string suffix){File.WriteAllText(args[1]+suffix,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));File.SetUnixFileMode(args[1]+suffix,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
async Task Present(UUID[] ids,bool expected){var until=DateTime.UtcNow.AddSeconds(20);do{var live=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Select(p=>p.ID).ToHashSet();if(ids.All(id=>live.Contains(id)==expected))return;await Task.Delay(200);}while(DateTime.UtcNow<until);throw new InvalidDataException("Native UDP identity mismatch");}
try{
 bool Login()=>client.Network.Login("Pilot","Builder",account.GetProperty("password").GetString(),"MutSea.HostedLumiSmoke","home","0.1.0");
 bool connected=Login();
 // Inherited login clears an abandoned grid-user entry and asks for one retry.
 if(!connected&&client.Network.LoginMessage.Contains("already logged in",StringComparison.OrdinalIgnoreCase)){await Task.Delay(1200);connected=Login();}
 Require(connected,"Native login failed");
 Require(client.Self.AgentID.ToString()==account.GetProperty("user_id").GetString(),"Wrong actor");
 var deadline=DateTime.UtcNow.AddSeconds(30);while(client.Network.CurrentSim.ObjectsPrimitives.Copy().Count<423&&DateTime.UtcNow<deadline)await Task.Delay(200);
 stage="seed capability";
 using var seed=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDArray{OSD.FromString("FinalverseWorld")}),Encoding.UTF8);seed.Headers.ContentType=new("application/llsd+xml");
 var response=await http.PostAsync(client.Network.CurrentSim.Caps.SeedCapsURI,seed);response.EnsureSuccessStatusCode();var capabilities=(OSDMap)OSDParser.DeserializeLLSDXml(await response.Content.ReadAsStringAsync());
 Require(capabilities.ContainsKey("FinalverseWorld"),"FinalverseWorld missing; capability keys: "+string.Join(",",capabilities.Keys));cap=capabilities["FinalverseWorld"].AsString();
 Require(new Uri(cap).GetLeftPart(UriPartial.Authority)=="https://grid.mutsea.com","Unexpected capability origin");
 var discovery=await World(new{op="experience"});Require(discovery.GetProperty("server_planning").GetBoolean(),"Server planner missing");
 if(args[2]=="spatial"){
  stage="relative planning";var plan=await World(new{op="plan",request="Create a small blue box named Grounded Right Marker four meters to my right."});
  var action=plan.GetProperty("plan").GetProperty("actions")[0];Require(action.GetProperty("parameters").GetProperty("placement").GetProperty("direction").GetString()=="right","Expected relative intent");
  var inspection=await World(new{op="inspect"});var avatar=inspection.GetProperty("avatar");var at=avatar.GetProperty("position");var q=avatar.GetProperty("orientation");
  double yaw=Math.Atan2(2*(q[3].GetDouble()*q[2].GetDouble()+q[0].GetDouble()*q[1].GetDouble()),1-2*(q[1].GetDouble()*q[1].GetDouble()+q[2].GetDouble()*q[2].GetDouble()));
  stage="native relative preview";var preview=await World(new{op="prepare",plan=plan.GetProperty("plan")});var step=preview.GetProperty("steps")[0];var position=step.GetProperty("after").GetProperty("position");
  double dx=position[0].GetDouble()-at[0].GetDouble(),dy=position[1].GetDouble()-at[1].GetDouble();
  Require(Math.Abs(dx-4*Math.Sin(yaw))<.03&&Math.Abs(dy+4*Math.Cos(yaw))<.03,"Native actor-relative geometry mismatch");
  var id=new UUID(step.GetProperty("id").GetString());await Present([id],false);applied=preview.GetProperty("plan_id").GetString();
  stage="commit";await World(new{op="commit",plan_id=applied});await Present([id],true);var live=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Single(p=>p.ID==id);
  Require(Vector3.Distance(live.Position,new(position[0].GetSingle(),position[1].GetSingle(),position[2].GetSingle()))<.03,"UDP transform mismatch");
  stage="undo";await World(new{op="undo",plan_id=applied});undone=true;await Present([id],false);Save(new{relative_right_meters=4,dx,dy,inspection,preview,udp_transform_verified=true,undo_verified=true});
 }else{
  Require(discovery.GetProperty("citizen").GetProperty("can_interact").GetBoolean(),"Hosted citizen unavailable");await Present([new UUID("b151468b-cbcc-4af2-ae2d-6967e8938bb4")],true);
  if(args[2]=="citizen"){
   stage="citizen identity";var identity=await Citizen("say","Lumi, who are you?");
   var before=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Select(p=>p.ID).ToHashSet();
   stage="co-building preview";var proposed=await Citizen("say","Lumi, create a small quiet reading nook nearby.");var preview=proposed.GetProperty("preview");Require(preview.ValueKind==JsonValueKind.Object&&proposed.GetProperty("world_spec").GetProperty("kind").GetString()=="world_spec","No reusable co-building preview");
   Require(client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.All(p=>before.Contains(p.ID)),"Proposal mutated the scene");
   var expectedParts=preview.GetProperty("part_count").GetInt32();Require(expectedParts is >0 and <=192,"Part budget exceeded");
   Patch(new{identity,proposed,expectedParts},".preview.json");
   stage="co-building approval";var approved=await Citizen("approve");
   var goal=approved.GetProperty("citizen").GetProperty("goals").EnumerateArray().Last(g=>g.GetProperty("kind").GetString()=="compose_place");Require(goal.GetProperty("status").GetString()=="completed","Co-building did not complete");
   applied=goal.GetProperty("plan_id").GetString();
   deadline=DateTime.UtcNow.AddSeconds(25);UUID[] created=[];do{await Task.Delay(300);created=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Select(p=>p.ID).Where(id=>!before.Contains(id)).ToArray();if(created.Length>=expectedParts)break;}while(DateTime.UtcNow<deadline);
   Require(created.Length==expectedParts,"UDP part count mismatch");
   var journal=await World(new{op="journal"});Require(journal.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==applied&&e.GetProperty("status").GetString()=="committed"),"Co-building WorldLine missing");
   Save(new{identity,proposed,approved,plan_id=applied,goal_id=goal.GetProperty("goal_id").GetString(),created_ids=created.Select(id=>id.ToString()).ToArray(),expectedParts,udp_parts_verified=true});
   // Deliberately retain only this approved private fixture for the restart proof.
   applied=null;
  }else{
   var receipt=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;var ids=receipt.GetProperty("created_ids").EnumerateArray().Select(v=>new UUID(v.GetString())).ToArray();
   await Present(ids,true);var state=await Citizen("inspect");
   var goal=state.GetProperty("citizen").GetProperty("goals").EnumerateArray().Single(g=>g.GetProperty("goal_id").GetString()==receipt.GetProperty("goal_id").GetString());
   Require(goal.GetProperty("status").GetString()=="completed"&&goal.GetProperty("plan_id").GetString()==receipt.GetProperty("plan_id").GetString(),"PostgreSQL lost completed goal");
   Require(state.GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("subject").GetString()=="co_building"),"Co-building memory missing after restart");
   if(args[2]=="undo"){stage="co-building undo";var result=await Citizen("undo");await Present(ids,false);Patch(new{restart_verified=true,undo_verified=true,result},".undo.json");}
   else Patch(new{restart_verified=true,state},".restart.json");
  }
 }
 Console.WriteLine("PASS hosted native session, deterministic placement or Lumi co-building, UDP and durable state.");
}catch(Exception error){
 Patch(new{stage,exception_type=error.GetType().Name,error=error.Message,login_message=client.Network.LoginMessage},".diagnostic.json");
 if(applied!=null&&!undone){try{if(args[2]=="spatial")await World(new{op="undo",plan_id=applied});else await Citizen("undo");}catch{Console.Error.WriteLine("Owned test operation needs Undo review.");}}
 throw new InvalidDataException("Hosted proof failed at "+stage+"; private diagnostic retained.");
}finally{if(client.Network.Connected)client.Network.Logout();}
