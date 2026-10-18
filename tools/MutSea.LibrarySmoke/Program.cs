// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Native authenticated discovery/teleport proof. Optional authoring only in the isolated loopback fixture.
using System.Text.Json;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
if(args.Length is not (2 or 3))throw new ArgumentException("Expected private account, new receipt and optionally isolated creator recipe");
byte[] Read(string path,int max){var f=new FileInfo(path);if(!f.Exists||f.LinkTarget!=null||f.Length>max)throw new IOException("Invalid probe input");return File.ReadAllBytes(path);}
if(!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(args[0])&(UnixFileMode.GroupRead|UnixFileMode.OtherRead))!=0)throw new IOException("Account must be private");
var account=JsonDocument.Parse(Read(args[0],16384)).RootElement;string loginUrl=account.GetProperty("grid_url").GetString()!;
if(loginUrl is not ("http://127.0.0.1:18095/" or "https://grid.mutsea.com/"))throw new IOException("Only the named staging or public MutSea grid may be probed");
bool authoring=args.Length==3;if(authoring&&loginUrl!="http://127.0.0.1:18095/")throw new IOException("Creator mutations are restricted to the isolated fixture");
if(File.Exists(args[1]))throw new IOException("Preserve previous receipt");
var client=new GridClient();Settings.LOG_LEVEL=Helpers.LogLevel.Error;
client.Settings.LOGIN_SERVER=loginUrl;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.MULTIPLE_SIMS=false;client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Self.Movement.Camera.Far=512;
client.Self.TeleportProgress+=(_,e)=>Console.WriteLine("TELEPORT "+e.Status+" "+e.Message);
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(55)};
var observations=new List<object>();string cap="",stage="login";bool logout=false;
async Task Discover()
{
    var until=DateTime.UtcNow.AddSeconds(10);
    while(client.Network.CurrentSim?.Caps==null&&DateTime.UtcNow<until)await Task.Delay(100);
    var caps=client.Network.CurrentSim?.Caps??throw new IOException("Missing region capabilities");
    using var body=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDArray{OSD.FromString("FinalverseWorld")}),Encoding.UTF8,"application/llsd+xml");
    body.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
    var response=await http.PostAsync(caps.SeedCapsURI,body);response.EnsureSuccessStatusCode();
    var map=(OSDMap)OSDParser.DeserializeLLSDXml(await response.Content.ReadAsStringAsync());cap=map["FinalverseWorld"].AsString();
    var uri=new Uri(cap);var expected=new Uri(loginUrl);if(uri.Scheme!=expected.Scheme||uri.Authority!=expected.Authority)throw new IOException("Unexpected capability origin");
}
async Task<JsonElement> World(object value)
{
    await Task.Delay(150);using var body=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json");
    var response=await http.PostAsync(cap,body);var root=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    if(!response.IsSuccessStatusCode||!root.GetProperty("ok").GetBoolean()){var operation=JsonSerializer.SerializeToElement(value);throw new IOException("World API refused "+operation.GetProperty("op").GetString()+"/"+(operation.TryGetProperty("command",out var command)?command.GetString():"")+": "+(root.TryGetProperty("error",out var error)?error.GetString():"unconfirmed response"));}return root.GetProperty("result").Clone();
}
try
{
    if(!client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"MutSea.LibrarySmoke","home","0.1.0"))throw new IOException("Native login failed");
    var start=client.Network.CurrentSim??throw new IOException("No native simulator");await Discover();
    var directory=await World(new{op="destinations"});
    if(directory.GetProperty("actor_id").GetString()!=client.Self.AgentID.ToString()||directory.GetProperty("region_id").GetString()!=start.RegionID.ToString())throw new IOException("Directory session mismatch");
    var destinations=directory.GetProperty("destinations").EnumerateArray().Where(d=>d.GetProperty("online").GetBoolean()).Take(8).Select(d=>d.Clone()).ToArray();
    if(destinations.Length<4)throw new IOException("Expected at least four admitted online regions");
    if(account.TryGetProperty("destination_ids",out var selectedIds))
    {
        var ids=selectedIds.EnumerateArray().Select(x=>x.GetString()).ToHashSet();
        destinations=destinations.Where(d=>ids.Contains(d.GetProperty("id").GetString())).ToArray();
        if(destinations.Length!=ids.Count||destinations.Length==0)throw new IOException("Requested diagnostic destinations are unavailable");
    }
    foreach(var d in destinations)
    {
        stage="teleport "+d.GetProperty("id").GetString();var origin=d.GetProperty("global_origin");var spawn=d.GetProperty("spawn");
        ulong handle=((ulong)origin[0].GetUInt32()<<32)|origin[1].GetUInt32();var position=new Vector3(spawn[0].GetSingle(),spawn[1].GetSingle(),spawn[2].GetSingle());
        var clock=System.Diagnostics.Stopwatch.StartNew();
        if(client.Network.CurrentSim?.RegionID.ToString()!=d.GetProperty("region_id").GetString())
        {
            bool reached=client.Self.Teleport(handle,position);
            var handshakeDeadline=DateTime.UtcNow.AddSeconds(10);
            while(reached&&client.Network.CurrentSim?.RegionID.ToString()!=d.GetProperty("region_id").GetString()&&DateTime.UtcNow<handshakeDeadline)await Task.Delay(100);
            if(!reached||client.Network.CurrentSim?.RegionID.ToString()!=d.GetProperty("region_id").GetString())throw new IOException("Native teleport did not reach the advertised region");
        }
        await Discover();var inspection=await World(new{op="inspect"});
        if(inspection.GetProperty("recovery_required").GetBoolean())throw new IOException("Destination requires recovery");
        int expected=account.TryGetProperty("expected_region_parts",out var expectations)&&expectations.TryGetProperty(d.GetProperty("region_id").GetString()!,out var minimum)?minimum.GetInt32():1;
        if(expected is <1 or >10000)throw new IOException("Bounded expected part count required");
        var streamDeadline=DateTime.UtcNow.AddSeconds(45);
        while(client.Network.CurrentSim!.ObjectsPrimitives.Count<expected&&DateTime.UtcNow<streamDeadline)await Task.Delay(200);
        int count=client.Network.CurrentSim!.ObjectsPrimitives.Count;
        if(count<expected)throw new IOException("Native stream did not reach the audited minimum part count");
        if(count<1)throw new IOException("Destination streamed no objects");
        observations.Add(new{destination=d.GetProperty("id").GetString(),region_id=client.Network.CurrentSim.RegionID.ToString(),streamed_parts=count,expected_minimum_parts=expected,elapsed_ms=clock.ElapsedMilliseconds,
            workspace=inspection.TryGetProperty("workspace",out var workspace)?workspace.Clone():(JsonElement?)null});
        if(d.GetProperty("workspace").GetBoolean()&&account.TryGetProperty("verify_workspace_planning",out var live)&&live.GetBoolean())
        {
            var answer=await World(new{op="plan",request="Describe this AI workspace and the roles of its spaces."});
            string text=answer.GetProperty("answer").GetString()??"";
            Console.WriteLine("WORKSPACE_ANSWER "+text);
            if(!text.Contains("Conversation",StringComparison.OrdinalIgnoreCase)||!text.Contains("Review",StringComparison.OrdinalIgnoreCase))throw new IOException("Workspace answer was not grounded in its supplied zones");
            if(answer.TryGetProperty("proposal",out var suggested)&&suggested.ValueKind==JsonValueKind.Object&&suggested.TryGetProperty("actions",out var actions)&&actions.GetArrayLength()!=0)throw new IOException("Read-only workspace request proposed a mutation");
            observations.Add(new{hosted_workspace_answer=text,read_only=true});
        }
    }
    stage="return teleport";
    if(client.Network.CurrentSim?.RegionID!=start.RegionID)
    {
        if(!client.Self.Teleport(start.Handle,new Vector3(160,160,24)))throw new IOException("Return teleport failed");
        var returnDeadline=DateTime.UtcNow.AddSeconds(10);
        while(client.Network.CurrentSim?.RegionID!=start.RegionID&&DateTime.UtcNow<returnDeadline)await Task.Delay(100);
        if(client.Network.CurrentSim?.RegionID!=start.RegionID)throw new IOException("Return region handshake was not confirmed");
    }
    await Discover();
    if(authoring)
    {
        stage="creator roundtrip";var recipe=JsonDocument.Parse(Read(args[2],65536)).RootElement;
        var proposal=new{schema_version=1,request="Isolated creator release roundtrip",provider="native-smoke",model="recipe-v1",actions=new[]{new{type="compose_place",target_id=(string?)null,parameters=new{draft=recipe,site="near_actor"}}}};
        var plan=await World(new{op="prepare",plan=proposal});string id=plan.GetProperty("plan_id").GetString()!;
        var committed=await World(new{op="commit",plan_id=id});if(committed.GetProperty("status").GetString()!="committed")throw new IOException("Creator place commit failed");
        var saved=await World(new{op="creator",command="save",plan_id=id,title="Confluence creator acceptance"});string release=saved.GetProperty("release_id").GetString()!;
        var published=await World(new{op="creator",command="publish",release_id=release,price_minor=0,currency="USD"});if(!published.GetProperty("published").GetBoolean()||published.GetProperty("checkout_enabled").GetBoolean())throw new IOException("Free listing state differs");
        var remix=await World(new{op="creator",command="remix",release_id=release});string remixId=remix.GetProperty("plan_id").GetString()!;
        await World(new{op="commit",plan_id=remixId});var child=await World(new{op="creator",command="save",plan_id=remixId,title="Confluence remix acceptance"});
        if(child.GetProperty("content").GetProperty("parent_release").GetString()!=release)throw new IOException("Remix lineage was lost");
        var unpublish=await World(new{op="creator",command="unpublish",release_id=release});if(unpublish.GetProperty("published").GetBoolean())throw new IOException("Unpublish failed");
        await World(new{op="undo",plan_id=remixId});await World(new{op="undo",plan_id=id});
        observations.Add(new{creator_release=release,remix_release=child.GetProperty("release_id").GetString(),published_then_unpublished=true,checkout_enabled=false,world_changes_undone=true});
    }
    stage="logout";
    bool acknowledged=false;client.Network.LoggedOut+=(_,_)=>acknowledged=true;
    client.Network.RegisterCallback(OpenMetaverse.Packets.PacketType.LogoutReply,(_,_)=>acknowledged=true);
    client.Network.Logout();
    var logoutDeadline=DateTime.UtcNow.AddSeconds(5);while(!acknowledged&&DateTime.UtcNow<logoutDeadline)await Task.Delay(100);
    using var staleBody=new StringContent("{\"op\":\"inspect\"}",Encoding.UTF8,"application/json");
    var stale=await http.PostAsync(cap,staleBody);
    byte[] staleBytes=await stale.Content.ReadAsByteArrayAsync();
    if(staleBytes.Length<=65536){string diagnostic=args[1]+".stale-response";using(var f=new FileStream(diagnostic,FileMode.CreateNew,FileAccess.Write))f.Write(staleBytes);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(diagnostic,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
    string? staleRoot=null;bool? staleOk=null;string? faultCode=null;
    if(staleBytes.Length<=65536)
    {
        try{using var doc=JsonDocument.Parse(staleBytes);if(doc.RootElement.TryGetProperty("ok",out var ok))staleOk=ok.GetBoolean();}catch(JsonException){}
        try{using var reader=System.Xml.XmlReader.Create(new MemoryStream(staleBytes),new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,MaxCharactersInDocument=65536,XmlResolver=null});var doc=System.Xml.Linq.XDocument.Load(reader);staleRoot=doc.Root?.Name.LocalName;faultCode=doc.Descendants("member").FirstOrDefault(m=>(string?)m.Element("name")=="faultCode")?.Element("value")?.Value;}catch(System.Xml.XmlException){}
    }
    bool revoked=MutSea.LibrarySmoke.CapabilityResponse.Unavailable((int)stale.StatusCode,staleBytes);
    // LogoutReply is UDP and may be lost during teardown. Keep its observation
    // separate; require both transport closure and actual API unavailability.
    logout=!client.Network.Connected&&revoked;
    observations.Add(new{logout_acknowledged=acknowledged,stale_capability_rejected=revoked,stale_http_status=(int)stale.StatusCode,stale_body_bytes=staleBytes.Length,stale_content_type=stale.Content.Headers.ContentType?.MediaType,stale_world_ok=staleOk,stale_xml_root=staleRoot,stale_xmlrpc_fault=faultCode,client_transport_disconnected=!client.Network.Connected});
    if(!logout)throw new IOException("Transport closure and capability revocation were not confirmed");
    using var output=new FileStream(args[1],FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(output,new{passed=true,observations,logout,appearance_sent=false},new JsonSerializerOptions{WriteIndented=true});
    Console.WriteLine($"LIBRARY_SMOKE destinations={destinations.Length} creator_roundtrip={authoring} logout={logout}");return 0;
}
catch(Exception ex){using var failure=new FileStream(args[1],FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(failure,new{passed=false,stage,observations,logout,appearance_sent=false},new JsonSerializerOptions{WriteIndented=true});Console.Error.WriteLine($"LIBRARY_SMOKE_FAILED stage={stage} error={ex.Message}");return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
