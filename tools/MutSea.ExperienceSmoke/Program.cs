// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Bounded local native discovery probe. No appearance, model call or mutation.
using System.Text.Json;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length!=2){Console.Error.WriteLine("Usage: MutSea.ExperienceSmoke <private-local-case> <new-receipt>");return 2;}
void Assert(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
var input=new FileInfo(args[0]);Assert(input.Exists&&input.LinkTarget==null&&input.Length is >0 and <=16384,"Private bounded case required");
if(!OperatingSystem.IsWindows())Assert((File.GetUnixFileMode(args[0])&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))==0,"Case must be owner-only");
using var doc=JsonDocument.Parse(File.ReadAllBytes(args[0]));var a=doc.RootElement;
var uri=new Uri(a.GetProperty("grid_url").GetString()!);
var targets=new Dictionary<int,(string region,string name)>{[18094]=("a9d44538-26ca-4586-a8a9-e551722ffa15","MutSea Harbor"),[18095]=("e1908a3c-f5be-4503-8135-d194cefe005c","MutSea Harbor"),[18098]=("8a8e9d93-9cca-4e6d-826d-fdf724534e04","Auren Island")};
Assert(uri.Scheme=="http"&&uri.Host=="127.0.0.1"&&uri.AbsolutePath=="/"&&uri.Query==""&&uri.UserInfo==""&&targets.ContainsKey(uri.Port),"Known loopback grid only");
var target=targets[uri.Port];Assert(a.GetProperty("region_id").GetString()==target.region,"Exact local region required");
var output=Path.GetFullPath(args[1]);Assert(!File.Exists(output)&&!Directory.Exists(output),"Receipt must be new");
var client=new GridClient();client.Settings.LOGIN_SERVER=uri.AbsoluteUri;
client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.MULTIPLE_SIMS=false;
string stage="login";
try
{
    Assert(client.Network.Login(a.GetProperty("first_name").GetString(),a.GetProperty("last_name").GetString(),a.GetProperty("password").GetString(),"MutSea.ExperienceSmoke","uri:"+target.name+"&128&124&26","0.1.0"),"Native login failed");
    Assert(client.Network.LoginResponseData.AgentID.ToString()==a.GetProperty("user_id").GetString(),"Authenticated identity mismatch");
    stage="region";var deadline=DateTime.UtcNow.AddSeconds(12);
    do{if(client.Network.CurrentSim?.RegionID.ToString()==target.region&&client.Network.CurrentSim.Caps!=null)break;await Task.Delay(100);}while(DateTime.UtcNow<deadline);
    Assert(client.Network.CurrentSim?.RegionID.ToString()==target.region&&client.Network.CurrentSim.Caps!=null,"Unexpected native region");
    bool LocalOrigin(Uri u)=>u.Scheme=="http"&&u.Host=="127.0.0.1"&&u.Port==uri.Port&&u.UserInfo==""&&u.Query==""&&u.Fragment=="";
    using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(8)};
    async Task<(int status,byte[] bytes)> Bounded(HttpRequestMessage request,int limit)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);var bytes=new byte[limit+1];int count=0,n;
        while(count<bytes.Length&&(n=await stream.ReadAsync(bytes.AsMemory(count),timeout.Token))>0)count+=n;
        Assert(count<=limit,"Bounded response required");return ((int)response.StatusCode,bytes[..count]);
    }
    // This SDK predates FinalverseWorld; request the extension through native
    // LLSD seed negotiation rather than relying on its built-in capability list.
    stage="capabilities";var seedUri=new Uri(client.Network.CurrentSim!.Caps.SeedCapsURI);Assert(LocalOrigin(seedUri),"Unexpected seed origin");
    using var seed=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDArray{OSD.FromString("FinalverseWorld")}),Encoding.UTF8);
    seed.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
    using var seedRequest=new HttpRequestMessage(HttpMethod.Post,seedUri){Content=seed};
    var seedResponse=await Bounded(seedRequest,65536);Assert(seedResponse.status==200,"Seed negotiation failed");
    var map=(OSDMap)OSDParser.DeserializeLLSDXml(Encoding.UTF8.GetString(seedResponse.bytes));
    var cap=new Uri(map["FinalverseWorld"].AsString());Assert(LocalOrigin(cap),"Unexpected capability origin");
    async Task<(int status,JsonElement value)> Post(object body)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,cap);request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        var response=await Bounded(request,1048576);using var result=JsonDocument.Parse(response.bytes);return (response.status,result.RootElement.Clone());
    }
    stage="discovery";var first=await Post(new{op="experience"});Assert(first.status==200,"Discovery failed");
    var result=first.value.GetProperty("result");
    Assert(result.GetProperty("schema_version").GetInt32()==1&&result.GetProperty("region_id").GetString()==target.region&&result.GetProperty("actor_id").GetString()==a.GetProperty("user_id").GetString(),"Discovery scope mismatch");
    Assert(result.GetProperty("compose_places").GetBoolean()==a.GetProperty("expect_compose").GetBoolean(),"Composer availability mismatch");
    var citizen=result.GetProperty("citizen");Assert(citizen.GetProperty("present").GetBoolean()==a.GetProperty("expect_citizen_present").GetBoolean()&&citizen.GetProperty("can_interact").GetBoolean()==a.GetProperty("expect_citizen_interactive").GetBoolean(),"Citizen eligibility mismatch");
    await Task.Delay(120);var spoof=await Post(new{op="experience",actor_id=UUID.Random().ToString()});
    Assert(spoof.status==200&&spoof.value.GetProperty("result").GetProperty("actor_id").GetString()==a.GetProperty("user_id").GetString(),"Request body changed authenticated identity");
    // Discovery has a separate throttle: immediate normal inspection still works.
    stage="inspection";var inspected=await Post(new{op="inspect"});Assert(inspected.status==200&&inspected.value.GetProperty("ok").GetBoolean(),"Discovery consumed action throttle");
    bool denied=false;
    if(!citizen.GetProperty("can_interact").GetBoolean())
    {
        await Task.Delay(120);var refusal=await Post(new{op="citizen",command="inspect"});
        Assert(refusal.status==400&&refusal.value.GetProperty("error").GetString()=="This citizen is not controlled by your account.","Citizen policy did not deny unavailable/observer access");denied=true;
    }
    stage="logout";client.Network.Logout();deadline=DateTime.UtcNow.AddSeconds(5);while(client.Network.Connected&&DateTime.UtcNow<deadline)await Task.Delay(50);Assert(!client.Network.Connected,"Native logout not confirmed");
    using var receipt=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(output,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    JsonSerializer.Serialize(receipt,new{schema_version=1,discovery=result,authenticated_scope_verified=true,identity_spoof_ignored=true,immediate_inspection=true,citizen_access_denied=denied,logout=true,appearance_sent=false,world_mutation_requested=false},new JsonSerializerOptions{WriteIndented=true});
    Console.WriteLine("EXPERIENCE_SMOKE PASS: authenticated discovery, unchanged authority, normal inspection/logout.");return 0;
}
catch(Exception e){Console.Error.WriteLine("EXPERIENCE_SMOKE_FAILED stage="+stage+" type="+e.GetType().Name);return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
