// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Read-only native streaming probe; never restores archives or sends appearance.
using System.Text.Json;
using System.Security.Cryptography;
using OpenMetaverse;

if(args.Length != 3) { Console.Error.WriteLine("Usage: MutSea.OarSmoke <private-staging-account> <quarantine-report> <new-receipt>"); return 2; }
byte[] BoundedFile(string path,int limit,bool accountFile=false) {
    var info=new FileInfo(path);
    if(!info.Exists||info.LinkTarget!=null||info.Length<1||info.Length>limit)
        throw new InvalidDataException("Expected bounded regular input");
    if(accountFile && !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) &
        (UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))!=0)
        throw new IOException("Account must be private");
    using var file=File.OpenRead(path);
    var bytes=new byte[limit+1];int count=0,n;
    while(count<bytes.Length&&(n=file.Read(bytes,count,bytes.Length-count))>0)count+=n;
    if(count>limit)throw new InvalidDataException("Input grew beyond its bound");
    return bytes.AsSpan(0,count).ToArray();
}
var output=Path.GetFullPath(args[2]);
if(File.Exists(output)||Directory.Exists(output))throw new IOException("Receipt already exists");
using var account=JsonDocument.Parse(BoundedFile(args[0],16384,true));
var a=account.RootElement;
if(a.GetProperty("grid_url").GetString()!="http://127.0.0.1:18095/"||!a.GetProperty("registered").GetBoolean())
    throw new InvalidDataException("Expected registered isolated staging account");
using var source=JsonDocument.Parse(BoundedFile(args[1],16*1024*1024));
if(source.RootElement.GetProperty("instance_owner").GetString()!=a.GetProperty("user_id").GetString())
    throw new InvalidDataException("Use the reconciled runtime report for this instance owner");
var expected=source.RootElement.GetProperty("objects").EnumerateArray().SelectMany(o=>o.GetProperty("parts").EnumerateArray()).ToDictionary(p=>new UUID(p.GetProperty("id").GetString()!));
if(expected.Count is <1 or >10000)throw new InvalidDataException("Bounded staging scene required");
Vector3 Vector(JsonElement p,string key){var v=p.GetProperty(key).EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(v[0],v[1],v[2]);}
var client=new GridClient();
client.Settings.LOGIN_SERVER=a.GetProperty("grid_url").GetString();
client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;
client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
client.Self.Movement.Camera.Far=512;
bool login=false,logout=false;
string stage="login";
var mismatches=new List<string>();
var textures=new HashSet<UUID>();
Primitive[] prims=[];
int deliveredTextures=0;
try {
    if(!client.Network.Login(a.GetProperty("first_name").GetString(),a.GetProperty("last_name").GetString(),a.GetProperty("password").GetString(),"MutSea.OarSmoke","home","0.1.0"))
        throw new InvalidOperationException("Staging login failed");
    login=true;
    stage="region";
    var handshakeDeadline=DateTime.UtcNow.AddSeconds(10);
    while(client.Network.CurrentSim.RegionID==UUID.Zero && DateTime.UtcNow<handshakeDeadline)Thread.Sleep(100);
    if(client.Network.CurrentSim.RegionID!=new UUID(a.GetProperty("region_id").GetString()!))throw new InvalidDataException("Unexpected staging region");
    stage="capability_origin";
    var seed=new Uri(client.Network.CurrentSim.Caps.SeedCapsURI);
    if(seed.Scheme!="http"||seed.Host!="127.0.0.1"||seed.Port!=18095)throw new InvalidDataException("Unexpected capability origin");
    client.Self.Movement.SendUpdate(true);
    stage="stream";
    var deadline=DateTime.UtcNow.AddSeconds(90);
    do {
        prims=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.ToArray();
        var received=prims.Select(p=>p.ID).ToHashSet();
        if(expected.Keys.All(received.Contains))break;
        Thread.Sleep(250);
    }while(DateTime.UtcNow<deadline);
    var streamed=prims.ToDictionary(p=>p.ID);
    foreach(var (id,p) in expected) {
        if(!streamed.TryGetValue(id,out var prim)){mismatches.Add("missing:"+id);continue;}
        var position=Vector(p,prim.ParentID==0?"position":"offset");
        if(Vector3.Distance(position,prim.Position)>.05f||Vector3.Distance(Vector(p,"scale"),prim.Scale)>.02f)mismatches.Add("transform:"+id);
        if(prim.Textures?.DefaultTexture!=null)textures.Add(prim.Textures.DefaultTexture.TextureID);
        if(prim.Textures!=null)foreach(var face in prim.Textures.FaceTextures)if(face!=null)textures.Add(face.TextureID);
    }
    stage="texture_delivery";
    var capDeadline=DateTime.UtcNow.AddSeconds(10);
    Uri? textureCap;
    while((textureCap=client.Network.CurrentSim.Caps.CapabilityURI("GetTexture"))==null&&DateTime.UtcNow<capDeadline)Thread.Sleep(100);
    if(textureCap==null||textureCap.Scheme!="http"||textureCap.Host!="127.0.0.1"||textureCap.Port!=18095)
        throw new InvalidDataException("Unexpected texture capability");
    using(var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)}) {
        var samples=source.RootElement.GetProperty("members").EnumerateArray()
            .Where(m=>m.TryGetProperty("staging_path",out var path)&&path.GetString()!.StartsWith("assets/")&&path.GetString()!.EndsWith("_texture.jp2"))
            .Where(m=>textures.Contains(new UUID(m.GetProperty("staging_path").GetString()!.Substring(7,36))))
            .OrderByDescending(m=>m.GetProperty("bytes").GetInt32()).Take(5);
        foreach(var sample in samples) {
            int size=sample.GetProperty("bytes").GetInt32();
            if(size is <1 or >8*1024*1024)throw new InvalidDataException("Bounded texture sample required");
            var id=sample.GetProperty("staging_path").GetString()!.Substring(7,36);
            var uri=new UriBuilder(textureCap){Query="texture_id="+id}.Uri;
            using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream=await response.Content.ReadAsStreamAsync();
            var bytes=new byte[size+1];
            int count=0,n;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while(count<bytes.Length&&(n=await stream.ReadAsync(bytes.AsMemory(count),timeout.Token))>0)count+=n;
            if(count!=bytes.Length-1||Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0,count))).ToLowerInvariant()!=sample.GetProperty("staging_sha256").GetString())
                throw new InvalidDataException("Native texture payload mismatch");
            ++deliveredTextures;
        }
        if(deliveredTextures!=5)throw new InvalidDataException("Insufficient texture samples");
    }
    client.Network.Logout();
    var logoutDeadline=DateTime.UtcNow.AddSeconds(5);
    while(client.Network.Connected && DateTime.UtcNow<logoutDeadline)Thread.Sleep(50);
    logout=!client.Network.Connected;
    var receipt=new {schema_version=1,source_sha256=source.RootElement.GetProperty("source_sha256").GetString(),
        login,logout,region_id=a.GetProperty("region_id").GetString(),expected_parts=expected.Count,received_parts=prims.Length,
        matched_parts=expected.Count-mismatches.Count,all_transforms_verified=mismatches.Count==0,
        mismatch_count=mismatches.Count,mismatches=mismatches.Take(30),texture_ids=textures.OrderBy(x=>x.ToString()).Select(x=>x.ToString()),
        delivered_texture_samples=deliveredTextures,appearance_sent=false};
    using var outFile=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(output,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    JsonSerializer.Serialize(outFile,receipt,new JsonSerializerOptions{WriteIndented=true});
    Console.WriteLine($"OAR_SMOKE login={login} logout={logout} matched={expected.Count-mismatches.Count}/{expected.Count} textures={textures.Count}");
    return mismatches.Count==0&&logout?0:1;
}catch(Exception error){Console.Error.WriteLine("OAR_SMOKE_FAILED stage="+stage+" type="+error.GetType().Name);return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
