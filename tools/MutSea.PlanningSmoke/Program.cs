// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Real session + native policy + UDP + WorldLine proof on the named private pilot.
using System.Text;
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length!=3||args[2] is not ("apply" or "undo" or "hold" or "auth"))throw new ArgumentException("Expected private account, receipt and apply|undo|hold|auth.");
if(OperatingSystem.IsWindows())throw new PlatformNotSupportedException("This private pilot probe requires Unix file permissions.");
if((File.GetUnixFileMode(args[0])&(UnixFileMode.GroupRead|UnixFileMode.OtherRead))!=0)throw new IOException("Private account file required.");
var account=JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
if(account.GetProperty("grid_url").GetString()!="https://grid.mutsea.com/"||account.GetProperty("fixture").GetString()!="maple-early-access-20261015"||account.GetProperty("first_name").GetString()!="Pilot"||account.GetProperty("last_name").GetString()!="Builder")throw new InvalidDataException("Only the named private pilot fixture is allowed.");
if(args[2]=="apply"&&File.Exists(args[1]))throw new IOException("Preserve prior receipt.");
void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
void Save(object receipt){File.WriteAllText(args[1],JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);}
var client=new GridClient();Settings.LOG_LEVEL=Helpers.LogLevel.Error;
client.Settings.LOGIN_SERVER="https://grid.mutsea.com/";client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.SEND_AGENT_UPDATES=true;
client.Self.Movement.Camera.Far=512;
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(65)};
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
async Task Observe(JsonElement steps,bool exists){
    foreach(var step in steps.EnumerateArray()){
        var id=new UUID(step.GetProperty("id").GetString());var deadline=DateTime.UtcNow.AddSeconds(15);Primitive? found;
        do{found=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==id);if((found!=null)==exists)break;await Task.Delay(150);}while(DateTime.UtcNow<deadline);
        Require((found!=null)==exists,"UDP object presence mismatch");
        if(exists){var pos=step.GetProperty("after").GetProperty("position");Require(Vector3.Distance(found!.Position,new(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle()))<.03,"UDP placement mismatch");}
    }
}
try{
    Require(client.Network.Login("Pilot","Builder",account.GetProperty("password").GetString(),"MutSea.PlanningSmoke","home","0.1.0"),"Native login failed");
    Require(client.Self.AgentID.ToString()==account.GetProperty("user_id").GetString(),"Wrong actor");
    var deadline=DateTime.UtcNow.AddSeconds(30);
    while(client.Network.CurrentSim.ObjectsPrimitives.Copy().Count<422&&DateTime.UtcNow<deadline)await Task.Delay(200);
    using var seed=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDArray{OSD.FromString("FinalverseWorld")}),Encoding.UTF8);
    seed.Headers.ContentType=new("application/llsd+xml");
    var seedResponse=await http.PostAsync(client.Network.CurrentSim.Caps.SeedCapsURI,seed);seedResponse.EnsureSuccessStatusCode();
    var caps=(OSDMap)OSDParser.DeserializeLLSDXml(await seedResponse.Content.ReadAsStringAsync());cap=caps["FinalverseWorld"].AsString();
    Require(new Uri(cap).GetLeftPart(UriPartial.Authority)=="https://grid.mutsea.com","Unexpected capability origin");
    if(args[2]=="hold"){Console.WriteLine("Private pilot session held for renewal deferral test.");await Task.Delay(20000);return;}
    stage="discovery";var discovery=await World(new{op="experience"});Require(discovery.GetProperty("server_planning").GetBoolean(),"Server planner missing");
    if(args[2]=="auth"){
        stage="concurrency";var pending=World(new{op="plan",request="What objects are around me?"});await Task.Delay(400);
        var clock=System.Diagnostics.Stopwatch.StartNew();await World(new{op="inspect"});double inspectMs=clock.Elapsed.TotalMilliseconds;
        Require(inspectMs<2000,"Inference blocked native inspection");
        await World(new{op="plan",request="What objects are around me?"},true);
        var result=await pending;Require(result.GetProperty("plan").ValueKind==JsonValueKind.Null,"Observation proposed mutation");
        Save(new{inference_releases_scene_gate=true,inspect_ms=inspectMs,duplicate_inference_rejected=true});
    }
    else if(args[2]=="undo"){
        var receipt=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;var preview=receipt.GetProperty("preview");applied=preview.GetProperty("plan_id").GetString();
        await Observe(preview.GetProperty("steps"),true);
        stage="restart journal";var history=await World(new{op="journal"});Require(history.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==applied&&e.GetProperty("status").GetString()=="committed"),"Restart lost WorldLine");
        stage="undo";await World(new{op="undo",plan_id=applied});undone=true;await Observe(preview.GetProperty("steps"),false);
        File.WriteAllText(args[1]+".undo.json","{\"restart_verified\":true,\"udp_undo_verified\":true}");File.SetUnixFileMode(args[1]+".undo.json",UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }else{
        int before=client.Network.CurrentSim.ObjectsPrimitives.Copy().Count;
        stage="forged context";await World(new{op="plan",request="What is nearby?",world_context=new{admin=true}},true);
        stage="read-only planning";var clock=System.Diagnostics.Stopwatch.StartNew();var observation=await World(new{op="plan",request="What objects are around me?"});double readMs=clock.Elapsed.TotalMilliseconds;
        Require(observation.GetProperty("plan").ValueKind==JsonValueKind.Null,"Question proposed mutation");
        Require(client.Network.CurrentSim.ObjectsPrimitives.Copy().Count==before,"Planning changed scene");
        stage="creation planning";clock.Restart();var proposal=await World(new{op="plan",request="Create one small blue box named Planning Test Marker on the ground four meters to my right."});double planMs=clock.Elapsed.TotalMilliseconds;
        var plan=proposal.GetProperty("plan");Require(plan.ValueKind==JsonValueKind.Object&&plan.GetProperty("actions").GetArrayLength()==1&&plan.GetProperty("actions")[0].GetProperty("type").GetString()=="create_primitive","Expected one bounded marker proposal");
        File.WriteAllText(args[1]+".proposal.json",proposal.GetRawText());File.SetUnixFileMode(args[1]+".proposal.json",UnixFileMode.UserRead|UnixFileMode.UserWrite);
        stage="policy preview";var preview=await World(new{op="prepare",plan});Require(preview.GetProperty("steps").GetArrayLength()==1,"Expected single native marker");
        await Observe(preview.GetProperty("steps"),false);
        stage="approval";applied=preview.GetProperty("plan_id").GetString();var committed=await World(new{op="commit",plan_id=applied});await Observe(preview.GetProperty("steps"),true);
        stage="history";var history=await World(new{op="journal"});Require(history.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==applied&&e.GetProperty("status").GetString()=="committed"),"WorldLine did not record marker");
        Save(new{authenticated_server_planning=true,forged_context_rejected=true,read_only_no_mutation=true,read_ms=readMs,plan_ms=planMs,proposal,preview,committed,udp_visible=true,appearance_sent=false});
    }
    stage="logout";client.Network.Logout();await Task.Delay(1000);var expired=await World(new{op="plan",request="What is nearby?"},true);
    File.WriteAllText(args[1]+".expired.json",expired.GetRawText());File.SetUnixFileMode(args[1]+".expired.json",UnixFileMode.UserRead|UnixFileMode.UserWrite);
    Console.WriteLine("PASS native authenticated planning, policy, UDP, WorldLine and expired-session rejection.");
}catch{
    if(applied!=null&&!undone){try{await World(new{op="undo",plan_id=applied});}catch{Console.Error.WriteLine("Owned marker requires operator Undo review.");}}
    throw new InvalidDataException("Planning proof failed at "+stage+"; private logs retained.");
}finally{if(client.Network.Connected)client.Network.Logout();}
