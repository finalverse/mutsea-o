// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Private isolated-grid acceptance probe: real capability + real UDP + local model.
using System.Text.Json;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length!=3||args[2] is not ("apply" or "capture" or "verify" or "undo" or "verify_undo" or "actions")){Console.Error.WriteLine("Usage: WorldSmoke private-account.json receipt.json apply|capture|verify|undo|verify_undo|actions");return 2;}
var account=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var client=new GridClient();
client.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();
client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.SEND_AGENT_UPDATES=true;
client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(110)};
string cap="";
async Task<JsonElement> World(object body)
{
    await Task.Delay(150);
    var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json"));
    var root=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    if(!response.IsSuccessStatusCode||!root.GetProperty("ok").GetBoolean())throw new Exception(root.GetProperty("error").GetString());
    return root.GetProperty("result").Clone();
}
async Task<JsonElement> Ask(string request,JsonElement context)
{
    var response=await http.PostAsync("http://127.0.0.1:18765/v1/world/plan",new StringContent(JsonSerializer.Serialize(new {request,world_context=context}),Encoding.UTF8,"application/json"));
    response.EnsureSuccessStatusCode();string value=await response.Content.ReadAsStringAsync();File.AppendAllText(args[1]+".model-trace",value+"\n");return JsonDocument.Parse(value).RootElement.Clone();
}
void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
async Task Teleport(float x,float y)
{
    var packet=new OpenMetaverse.Packets.TeleportLocationRequestPacket();
    packet.AgentData.AgentID=client.Self.AgentID;packet.AgentData.SessionID=client.Self.SessionID;
    packet.Info.RegionHandle=client.Network.CurrentSim.Handle;packet.Info.Position=new Vector3(x,y,27);packet.Info.LookAt=Vector3.UnitY;
    client.Network.SendPacket(packet);
    var deadline=DateTime.UtcNow.AddSeconds(15);
    do
    {
        await Task.Delay(300);var context=await World(new{op="inspect"});
        var pos=context.GetProperty("avatar").GetProperty("position");
        if(Math.Abs(pos[0].GetDouble()-x)<1&&Math.Abs(pos[1].GetDouble()-y)<1)return;
    }while(DateTime.UtcNow<deadline);
    throw new Exception("Simulator did not confirm avatar relocation");
}
async Task Observe(JsonElement steps,bool exists=true)
{
    foreach(var step in steps.EnumerateArray())
    {
        UUID id=new(step.GetProperty("id").GetString()); var deadline=DateTime.UtcNow.AddSeconds(20); Primitive? found;
        do {found=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==id);if((found!=null)==exists)break;await Task.Delay(150);}while(DateTime.UtcNow<deadline);
        Assert((found!=null)==exists,"UDP existence mismatch for "+id);
        if(exists)
        {
            var expected=step.GetProperty("after");var pos=expected.GetProperty("position").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var size=expected.GetProperty("scale").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var rotation=expected.GetProperty("rotation").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            bool Matches(Primitive p)
            {
                var actual=new[]{p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W};
                // q and -q describe the same rotation; compare either representation.
                double difference=Math.Min(actual.Zip(rotation,(a,b)=>(a-b)*(a-b)).Sum(),actual.Zip(rotation,(a,b)=>(a+b)*(a+b)).Sum());
                return Vector3.Distance(p.Position,new(pos[0],pos[1],pos[2]))<.02f&&Vector3.Distance(p.Scale,new(size[0],size[1],size[2]))<.02f&&difference<.0001;
            }
            // Wait for existing-object transform updates as well as creation.
            do {found=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==id);if(Matches(found))break;await Task.Delay(150);}while(DateTime.UtcNow<deadline);
            Assert(Matches(found!),"UDP transform mismatch");
        }
    }
}
try
{
    Assert(client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"Finalverse.WorldSmoke","home","1.0"),"Login failed");
    Console.WriteLine("SEED_ORIGIN "+new Uri(client.Network.CurrentSim.Caps.SeedCapsURI).GetLeftPart(UriPartial.Authority));
    var ready=DateTime.UtcNow.AddSeconds(30);
    while(client.Network.CurrentSim.ObjectsPrimitives.Copy().Count<40&&DateTime.UtcNow<ready)await Task.Delay(250);
    await Task.Delay(1500);
    var seed=new OSDArray {OSD.FromString("FinalverseWorld")};
    var seedContent=new StringContent(OSDParser.SerializeLLSDXmlString(seed),Encoding.UTF8);seedContent.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
    var response=await http.PostAsync(client.Network.CurrentSim.Caps.SeedCapsURI,seedContent);
    string seedBody=await response.Content.ReadAsStringAsync();
    if(!response.IsSuccessStatusCode){File.WriteAllText(args[1]+".seed-error",seedBody);throw new Exception("Seed response status "+(int)response.StatusCode);}
    var parsed=OSDParser.DeserializeLLSDXml(seedBody);
    if(parsed is not OSDMap){File.WriteAllText(args[1]+".seed-error",seedBody);throw new Exception("Seed did not return LLSD map; content type "+response.Content.Headers.ContentType);}
    var caps=(OSDMap)parsed;cap=caps["FinalverseWorld"].AsString();
    Assert(!string.IsNullOrEmpty(cap),"World capability missing");
    if(args[2]=="capture")
    {
        // Capture an existing UI-created home without proposing or executing changes.
        Assert(!File.Exists(args[1]),"Capture receipt already exists; choose a new private path");
        await Teleport(172,154);
        var lumi=await World(new{op="lumi"});string home=lumi.GetProperty("home_entity_id").GetString() ?? throw new Exception("Lumi has no home to capture");
        var journal=await World(new{op="journal"});
        var undone=journal.EnumerateArray().Where(e=>e.GetProperty("status").GetString()=="undone").Select(e=>e.GetProperty("plan_id").GetString()).ToHashSet();
        var committed=journal.EnumerateArray().Last(e=>e.GetProperty("status").GetString()=="committed"&&!undone.Contains(e.GetProperty("plan_id").GetString())&&e.GetProperty("steps")[0].GetProperty("id").GetString()==home);
        Assert(committed.GetProperty("steps").GetArrayLength()==17,"Expected a complete 17-component home");
        await Observe(committed.GetProperty("steps"));
        var semantic=await World(new{op="semantic",entity_id=home});
        Assert(semantic.GetProperty("owner_agent_id").GetString()==lumi.GetProperty("agent_id").GetString(),"Lumi reciprocal ownership missing");
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{source="existing-home-read-only-capture",udp_home_verified=true,committed,lumi,semantic,journal}));
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);
        Console.WriteLine("WORLD_SMOKE capture passed: existing 17 components, semantics, Lumi memory and WorldLine; no world mutation requested.");
    }
    else if(args[2]=="actions")
    {
        await Teleport(110,145);
        async Task<JsonElement> Execute(string type,string? id,object parameters)
        {
            var plan=new{schema_version=1,request="Deterministic API integration test: "+type,provider="integration-test",model="none",actions=new[]{new{type,target_id=id,parameters}}};
            var prepared=await World(new{op="prepare",plan});var result=await World(new{op="commit",plan_id=prepared.GetProperty("plan_id").GetString()});
            Assert(result.GetProperty("status").GetString()=="committed","Action did not commit: "+type);return result;
        }
        var created=await Execute("create_primitive",null,new{position=new[]{110,153,26},scale=new[]{1,1,1},color=new[]{.4,.26,.68,1},shape="box",name="API Test Cube"});
        await Observe(created.GetProperty("steps"));string id=created.GetProperty("steps")[0].GetProperty("id").GetString()!;
        foreach(var item in new (string,object)[]{("rotate_entity",new{rotation=new[]{0,0,1,0}}),("scale_entity",new{scale=new[]{2,1,1}}),("set_name",new{name="API Renamed Cube"}),("set_description",new{description="API verified semantic primitive"}),("set_color",new{color=new[]{.2,.5,.7,1}})})
        {var changed=await Execute(item.Item1,id,item.Item2);await Observe(changed.GetProperty("steps"));}
        var clone=await Execute("clone_entity",id,new{position=new[]{113,153,26}});await Observe(clone.GetProperty("steps"));
        var cloneUndo=await World(new{op="undo",plan_id=clone.GetProperty("plan_id").GetString()});Assert(cloneUndo.GetProperty("status").GetString()=="undone","Clone undo failed");await Observe(clone.GetProperty("steps"),false);
        var deleted=await Execute("delete_entity",id,new{});await Observe(created.GetProperty("steps"),false);
        var deleteUndo=await World(new{op="undo",plan_id=deleted.GetProperty("plan_id").GetString()});Assert(deleteUndo.GetProperty("status").GetString()=="undone","Delete undo failed");await Observe(deleteUndo.GetProperty("steps"));
        await Execute("delete_entity",id,new{});await Observe(created.GetProperty("steps"),false);
        Console.WriteLine("WORLD_SMOKE actions passed: create/rotate/scale/name/description/color/clone/delete and guarded restoration, real capability and UDP.");
    }
    else if(args[2]=="apply")
    {
        await Teleport(110,145);
        var context=await World(new {op="inspect"});
        var cube=context.GetProperty("nearby_entities").EnumerateArray().Select(x=>x.GetProperty("entity")).Single(x=>x.GetProperty("name").GetString()=="Practice Cube");
        context=await World(new {op="inspect",selected_id=cube.GetProperty("id").GetString()});
        var read=await Ask("What objects are around me?",context);
        Assert(read.GetProperty("plan").ValueKind==JsonValueKind.Null,"Read query unexpectedly proposed mutation");
        string answer=read.GetProperty("answer").GetString()!;
        Assert(context.GetProperty("nearby_entities").EnumerateArray().Count(e=>answer.Contains(e.GetProperty("entity").GetProperty("name").GetString()!,StringComparison.OrdinalIgnoreCase))>=2,"Read answer did not name actual inspected entities");
        var move=await Ask("Move this two meters toward the Harbor Pavilion Floor.",context);
        Assert(move.GetProperty("plan").ValueKind==JsonValueKind.Object,"Model did not propose a move");
        var prepared=await World(new{op="prepare",plan=move.GetProperty("plan")});
        var destination=prepared.GetProperty("steps")[0].GetProperty("after").GetProperty("position");var original=cube.GetProperty("position");
        double dx=destination[0].GetDouble()-original[0].GetDouble(),dy=destination[1].GetDouble()-original[1].GetDouble();
        Assert(Math.Abs(Math.Sqrt(dx*dx+dy*dy)-2)<.03&&dx>0&&dy>0,"Model did not express the requested two-meter move toward the pavilion");
        var moved=await World(new{op="commit",plan_id=prepared.GetProperty("plan_id").GetString()});
        Assert(moved.GetProperty("status").GetString()=="committed","Move failed");await Observe(moved.GetProperty("steps"));
        var undo=await World(new{op="undo",plan_id=prepared.GetProperty("plan_id").GetString()});
        Assert(undo.GetProperty("status").GetString()=="undone","Move undo failed");await Observe(undo.GetProperty("steps"));
        await Teleport(172,154);
        context=await World(new {op="inspect"});
        var generated=await Ask("Create a small lakeside home for Lumi here.",context);
        prepared=await World(new{op="prepare",plan=generated.GetProperty("plan")});
        var committed=await World(new{op="commit",plan_id=prepared.GetProperty("plan_id").GetString()});
        Assert(committed.GetProperty("status").GetString()=="committed","Home transaction failed");
        Assert(committed.GetProperty("steps").GetArrayLength()==17,"Expected the complete 17-component home");
        await Observe(committed.GetProperty("steps"));
        var repeat=await World(new{op="commit",plan_id=prepared.GetProperty("plan_id").GetString()});
        Assert(repeat.GetProperty("operation_id").GetString()==committed.GetProperty("operation_id").GetString(),"Idempotent retry failed");
        var lumi=await World(new{op="lumi"});string home=lumi.GetProperty("home_entity_id").GetString()!;
        var semantic=await World(new{op="semantic",entity_id=home});
        Assert(semantic.GetProperty("owner_agent_id").GetString()==lumi.GetProperty("agent_id").GetString(),"Lumi reciprocal ownership missing");
        var journal=await World(new{op="journal"});
        File.WriteAllText(args[1],JsonSerializer.Serialize(new {provider="ollama",model="llama3.2:3b",read_answer=read.GetProperty("answer"),move_verified=true,move_undo_verified=true,udp_home_verified=true,committed,lumi,semantic,journal}));
        Console.WriteLine("WORLD_SMOKE apply passed: live model, selected move/undo, 17 home objects observed over UDP, Lumi and WorldLine.");
    }
    else if(args[2]=="verify_undo")
    {
        var saved=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;
        var committed=saved.GetProperty("committed");await Teleport(172,154);
        await Observe(committed.GetProperty("steps"),false);
        var context=await World(new{op="inspect"});
        var ids=committed.GetProperty("steps").EnumerateArray().Select(s=>s.GetProperty("id").GetString()).ToHashSet();
        Assert(!context.GetProperty("nearby_entities").EnumerateArray().Any(e=>ids.Contains(e.GetProperty("entity").GetProperty("id").GetString())),"Undone home returned after restart");
        var lumi=await World(new{op="lumi"});Assert(lumi.GetProperty("home_entity_id").ValueKind==JsonValueKind.Null,"Undone home still bound to Lumi");
        var semantic=await World(new{op="semantic",entity_id=saved.GetProperty("lumi").GetProperty("home_entity_id").GetString()});Assert(semantic.TryGetProperty("found",out var found)&&!found.GetBoolean(),"Undone semantic entity remained");
        var journal=await World(new{op="journal"});Assert(journal.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==committed.GetProperty("plan_id").GetString()&&e.GetProperty("status").GetString()=="undone"),"Undo history missing after restart");
        Console.WriteLine("WORLD_SMOKE verify_undo passed: absence, Lumi/semantic removal and undo history survived restart.");
    }
    else
    {
        var saved=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;
        var committed=saved.GetProperty("committed");await Teleport(172,154);
        await Observe(committed.GetProperty("steps"));
        var lumi=await World(new{op="lumi"});Assert(lumi.GetProperty("home_entity_id").GetString()==saved.GetProperty("lumi").GetProperty("home_entity_id").GetString(),"Home identity changed after restart");
        Assert(lumi.GetProperty("agent_id").GetString()==saved.GetProperty("lumi").GetProperty("agent_id").GetString()&&lumi.GetProperty("memory").EnumerateArray().Select(m=>m.GetString()).SequenceEqual(saved.GetProperty("lumi").GetProperty("memory").EnumerateArray().Select(m=>m.GetString())),"Lumi identity or memory changed after restart");
        var semantic=await World(new{op="semantic",entity_id=lumi.GetProperty("home_entity_id").GetString()});Assert(semantic.GetProperty("owner_agent_id").GetString()==lumi.GetProperty("agent_id").GetString(),"Semantic ownership lost");
        // Compare decoded scalar values: the receipt serializer may escape '+' in a timestamp.
        foreach(var property in saved.GetProperty("semantic").EnumerateObject())
        {
            var current=semantic.GetProperty(property.Name);
            Assert(current.ValueKind==property.Value.ValueKind&&current.ToString()==property.Value.ToString(),"Semantic metadata changed after restart: "+property.Name);
        }
        var journal=await World(new{op="journal"});Assert(journal.EnumerateArray().Any(e=>e.GetProperty("operation_id").GetString()==committed.GetProperty("operation_id").GetString()),"WorldLine lost");
        if(args[2]=="undo")
        {var result=await World(new{op="undo",plan_id=committed.GetProperty("plan_id").GetString()});Assert(result.GetProperty("status").GetString()=="undone","Home undo rejected");await Observe(committed.GetProperty("steps"),false);}
        Console.WriteLine("WORLD_SMOKE "+args[2]+" passed: persistence, transforms, semantics, Lumi memory and WorldLine.");
    }
    client.Network.Logout();var logoutDeadline=DateTime.UtcNow.AddSeconds(5);
    while(client.Network.Connected&&DateTime.UtcNow<logoutDeadline)await Task.Delay(50);
    Assert(!client.Network.Connected,"Logout failed");return 0;
}
catch(Exception error){Console.Error.WriteLine("WORLD_SMOKE_FAILED "+error.Message);return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
