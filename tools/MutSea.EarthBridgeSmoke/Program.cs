// Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Explicit local-grid acceptance: passwords and capabilities never leave memory.
using System.Text;
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
if(args.Length!=3)throw new ArgumentException("Expected private account, command, and new evidence file paths.");
if(File.Exists(args[2]))throw new IOException("Evidence path already exists.");
foreach(var path in args.Take(2)) {
    var info=new FileInfo(path);
    if(!info.Exists||info.LinkTarget!=null||info.Length is <1 or >65536||(!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(path)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))!=0))throw new IOException("Private bounded fixture files required.");
}
using var account=JsonDocument.Parse(File.ReadAllBytes(args[0]));var a=account.RootElement;
if(a.GetProperty("grid_url").GetString()!="http://127.0.0.1:19060/")throw new IOException("This diagnostic is restricted to the isolated loopback grid.");
using var command=JsonDocument.Parse(File.ReadAllBytes(args[1]));var input=command.RootElement;
Settings.LOG_LEVEL=Helpers.LogLevel.Error;var client=new GridClient();
client.Settings.LOGIN_SERVER=a.GetProperty("grid_url").GetString();client.Settings.SEND_AGENT_APPEARANCE=false;
client.Settings.SEND_AGENT_UPDATES=true;client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.MULTIPLE_SIMS=false;
using var http=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=TimeSpan.FromSeconds(40)};
string cap="",stage="login";var timer=System.Diagnostics.Stopwatch.StartNew();
void LocalCapability(string address) {
    var uri=new Uri(address);
    if(uri.Scheme!="http"||uri.Host!="127.0.0.1"||uri.Port!=19060||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!="")throw new IOException("Capability must belong to the isolated loopback authority.");
}
async Task<(int status,JsonElement? root)> Call(object body){
    await Task.Delay(160);using var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json"));
    using var doc=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());return((int)response.StatusCode,doc.RootElement.Clone());
}
async Task<JsonElement> World(object body){var result=await Call(body);if(result.status!=200||result.root?.GetProperty("ok").GetBoolean()!=true)throw new InvalidDataException(result.root?.GetProperty("error").GetString()??"World request failed");return result.root.Value.GetProperty("result").Clone();}
try {
    string start=a.TryGetProperty("region_name",out var named)?"uri:"+named.GetString()+"&128&128&27":"home";
    if(!client.Network.Login(a.GetProperty("first_name").GetString(),a.GetProperty("last_name").GetString(),a.GetProperty("password").GetString(),"MutSea.EarthBridgeSmoke",start,"0.1"))throw new IOException("Native login refused");
    stage="capability";await Task.Delay(1500);
    var sim=client.Network.CurrentSim??throw new IOException("No simulator");
    LocalCapability(sim.Caps.SeedCapsURI);
    using var request=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDArray{OSD.FromString("FinalverseWorld"),OSD.FromString("GetMesh"),OSD.FromString("ViewerAsset")}),Encoding.UTF8,"application/llsd+xml");
    request.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
    using var seed=await http.PostAsync(sim.Caps.SeedCapsURI,request);seed.EnsureSuccessStatusCode();var caps=(OSDMap)OSDParser.DeserializeLLSDXml(await seed.Content.ReadAsStringAsync());cap=caps["FinalverseWorld"].AsString();
    if(cap.Length==0)throw new IOException("No world capability");
    LocalCapability(cap);
    if(input.TryGetProperty("position",out var position)) {
        var xyz=position.EnumerateArray().Select(v=>v.GetSingle()).ToArray();if(xyz.Length!=3||xyz.Any(x=>!float.IsFinite(x)||x<0||x>256))throw new IOException("Invalid diagnostic site");
        if(!client.Self.Teleport(sim.Handle,new Vector3(xyz[0],xyz[1],xyz[2])))throw new IOException("Native site teleport failed");await Task.Delay(500);
    }
    stage=input.GetProperty("action").GetString()!;JsonElement result;
    if(stage is "preview" or "apply") {
        using var handoff=JsonDocument.Parse(File.ReadAllBytes(input.GetProperty("handoff_file").GetString()!));
        result=await World(new{op="earth_bridge",action="preview",handoff=handoff.RootElement.GetProperty("token").GetString()});
        // Preview must not silently execute. A generic Apply cannot bypass FSIP.
        var bypass=await Call(new{op="commit",plan_id=result.GetProperty("plan_id").GetString()});
        if(bypass.status==200&&bypass.root?.GetProperty("ok").GetBoolean()==true)throw new IOException("Generic commit bypassed FSIP authorization");
        if(stage=="apply") {
            string plan=result.GetProperty("plan_id").GetString()!;
            result=await World(new{op="earth_bridge",action="commit",plan_id=plan});
            if(result.GetProperty("status").GetString()!="committed"||result.GetProperty("receipt_sync").GetString()!="recorded")throw new IOException("Creation or Earth receipt confirmation failed");
            var again=await Call(new{op="earth_bridge",action="commit",plan_id=plan});
            if(again.status==200&&again.root?.GetProperty("ok").GetBoolean()==true)throw new IOException("Native commit was replayed");
            var first=result.GetProperty("receipt").GetProperty("bindings")[0].GetProperty("entity_ids")[0].GetString();
            var semantic=await World(new{op="semantic",entity_id=first});
            if(semantic.GetProperty("earth_experience").GetProperty("manifest_sha256").GetString()!=result.GetProperty("receipt").GetProperty("manifest_sha256").GetString())throw new IOException("Native source semantics were not bound to the receipt");
        }
    } else if(stage is "commit" or "inspect" or "undo" or "reject") {
        using var saved=JsonDocument.Parse(File.ReadAllBytes(input.GetProperty("evidence_file").GetString()!));
        result=await World(new{op="earth_bridge",action=stage,plan_id=saved.RootElement.GetProperty("result").GetProperty("plan_id").GetString()});
    } else if(stage=="observe") {
        using var saved=JsonDocument.Parse(File.ReadAllBytes(input.GetProperty("evidence_file").GetString()!));var receipt=saved.RootElement.GetProperty("result").GetProperty("receipt");
        bool expected=!input.TryGetProperty("absent",out var absent)||!absent.GetBoolean();
        var ids=receipt.GetProperty("bindings").EnumerateArray().SelectMany(b=>b.GetProperty("entity_ids").EnumerateArray().Select(id=>new UUID(id.GetString()))).ToArray();
        if(ids.Length is <1 or >256)throw new IOException("Bounded authoritative object IDs required");
        var until=DateTime.UtcNow.AddSeconds(25);int count;
        do{var objects=sim.ObjectsPrimitives.Copy().Values.Select(p=>p.ID).ToHashSet();count=ids.Count(objects.Contains);if(expected?count==ids.Length:count==0)break;await Task.Delay(200);}while(DateTime.UtcNow<until);
        if(expected?count!=ids.Length:count!=0)throw new IOException("Actual UDP object existence differs from receipt");
        double? meshLatency=null;int? meshBytes=null;
        if(expected&&input.TryGetProperty("mesh_manifest_file",out var expectedManifest)) {
            using var compiled=JsonDocument.Parse(File.ReadAllBytes(expectedManifest.GetString()!));
            var primitive=sim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==ids[0]);
            string assetCap=caps["GetMesh"].AsString();var assetUri=new Uri(assetCap);
            if(assetUri.Host!="127.0.0.1"||assetUri.Port!=19060||assetUri.Scheme!="http")throw new IOException("Unexpected private asset authority");
            var delivery=System.Diagnostics.Stopwatch.StartNew();byte[] bytes=await http.GetByteArrayAsync(assetCap+"?mesh_id="+primitive.Sculpt.SculptTexture);
            meshLatency=delivery.Elapsed.TotalMilliseconds;meshBytes=bytes.Length;
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            if(bytes.Length>2097152||hash!=compiled.RootElement.GetProperty("parts")[0].GetProperty("mesh_sha256").GetString())throw new IOException("Native mesh delivery differs from the approved asset pin");
        }
        int materialCount=0;double? materialLatency=null;
        if(expected&&input.TryGetProperty("material_assets_file",out var expectedMaterials)) {
            using var pinned=JsonDocument.Parse(File.ReadAllBytes(expectedMaterials.GetString()!));
            var materials=pinned.RootElement.EnumerateArray().ToArray();
            if(materials.Length is <1 or >256)throw new IOException("Bounded material pins required");
            string assetCap=caps["ViewerAsset"].AsString();LocalCapability(assetCap);
            var delivery=System.Diagnostics.Stopwatch.StartNew();
            foreach(var material in materials) {
                if(!UUID.TryParse(material.GetProperty("id").GetString(),out var id)||id==UUID.Zero)throw new IOException("Invalid material pin");
                using var response=await http.GetAsync(assetCap+"?material_id="+id);
                if(!response.IsSuccessStatusCode)throw new IOException("Native material delivery returned HTTP "+(int)response.StatusCode);
                byte[] bytes=await response.Content.ReadAsByteArrayAsync();
                var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                if(bytes.Length is <1 or >1048576||hash!=material.GetProperty("sha256").GetString())throw new IOException("Native material delivery differs from the persisted pin");
                var wrapper=OSDParser.DeserializeLLSDBinary(bytes) as OSDMap;
                if(wrapper==null||wrapper["type"].AsString()!="GLTF 2.0"||wrapper["version"].AsString()!="1.1")throw new IOException("Invalid native glTF material wrapper");
                using var gltf=JsonDocument.Parse(wrapper["data"].AsString());
                if(gltf.RootElement.GetProperty("asset").GetProperty("version").GetString()!="2.0")throw new IOException("Invalid glTF material version");
                materialCount++;
            }
            materialLatency=delivery.Elapsed.TotalMilliseconds;
        }
        result=JsonSerializer.SerializeToElement(new{observed=true,independent_agent_id=client.Self.AgentID.ToString(),region_id=sim.RegionID.ToString(),expected_present=expected,matched_objects=count,receipt_objects=ids.Length,verified_mesh_bytes=meshBytes,mesh_delivery_ms=meshLatency,verified_materials=materialCount,material_delivery_ms=materialLatency});
    } else throw new InvalidDataException("Unsupported diagnostic action");
    var evidence=JsonSerializer.SerializeToUtf8Bytes(new{stage,elapsed_ms=timer.Elapsed.TotalMilliseconds,agent_id=client.Self.AgentID.ToString(),result});
    File.WriteAllBytes(args[2],evidence);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[2],UnixFileMode.UserRead|UnixFileMode.UserWrite);
    Console.WriteLine(JsonSerializer.Serialize(new{stage,passed=true,elapsed_ms=timer.Elapsed.TotalMilliseconds}));return 0;
}catch(Exception e){Console.Error.WriteLine(JsonSerializer.Serialize(new{stage,passed=false,error_type=e.GetType().Name,error=e.Message}));return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
