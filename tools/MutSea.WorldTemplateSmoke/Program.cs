// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Bounded first-session proof for a private world-template fixture. No scene or
// appearance writes; inherited HTTP login, inventory and UDP streaming only.
using System.Text.Json;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length!=3)throw new ArgumentException("Expected private account, world receipt and new output path.");
byte[] Read(string path,int limit,bool ownerOnly=false)
{
    var file=new FileInfo(path);
    if(!file.Exists||file.LinkTarget!=null||file.Length is <1||file.Length>limit)throw new IOException("Expected bounded regular input.");
    if(ownerOnly&&!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(path)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))!=0)throw new IOException("Account must be owner-only.");
    var bytes=File.ReadAllBytes(path);if(bytes.Length>limit)throw new IOException("Input exceeded its bound.");return bytes;
}
var account=JsonDocument.Parse(Read(args[0],16384,true)).RootElement;
if(account.GetProperty("grid_url").GetString()!="http://127.0.0.1:18098/"||!account.GetProperty("registered").GetBoolean())throw new InvalidDataException("Expected isolated registered world-template fixture.");
if(File.Exists(args[2]))throw new IOException("Preserve the previous receipt.");
var proof=JsonDocument.Parse(Read(args[1],16*1024*1024)).RootElement;
var plans=proof.GetProperty("places").EnumerateArray().Select(e=>e.GetProperty("plan").Clone()).ToArray();
var ids=plans.SelectMany(p=>p.GetProperty("placements").EnumerateArray()).SelectMany(p=>p.GetProperty("entity_ids").EnumerateArray()).Select(id=>new UUID(id.GetString())).ToHashSet();
if(ids.Count is <1 or >10000||plans.Length is <1 or >32)throw new InvalidDataException("Expected bounded provisioned world.");
var client=new GridClient();Settings.LOG_LEVEL=Helpers.LogLevel.Error;
client.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();
client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
client.Self.Movement.Camera.Far=512;
string stage="login";bool logout=false;
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)};
try
{
    if(!client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"MutSea.WorldTemplateSmoke","home","0.1.0"))throw new InvalidDataException("First-session login failed.");
    var login=client.Network.LoginResponseData;
    if(login.StartLocation!="home"||login.AgentID.ToString()!=account.GetProperty("user_id").GetString())throw new InvalidDataException("Home login fell back or changed identity.");
    stage="streaming";var deadline=DateTime.UtcNow.AddSeconds(35);Primitive[] prims=[];
    do{prims=client.Network.CurrentSim?.ObjectsPrimitives.Copy().Values.ToArray()??[];if(ids.IsSubsetOf(prims.Select(p=>p.ID)))break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
    if(client.Network.CurrentSim==null)throw new InvalidDataException("No active simulator.");
    if(client.Network.CurrentSim.RegionID.ToString()!=account.GetProperty("region_id").GetString())throw new InvalidDataException("Native region handshake differs from the fixture.");
    if(!ids.IsSubsetOf(prims.Select(p=>p.ID)))throw new InvalidDataException($"Incomplete world stream: received {ids.Intersect(prims.Select(p=>p.ID)).Count()} of {ids.Count} expected parts.");
    if(Vector3.Distance(client.Self.SimPosition,login.HomePosition)>3)throw new InvalidDataException($"Home arrival distance {Vector3.Distance(client.Self.SimPosition,login.HomePosition):F2} exceeds 3 m.");
    var byId=prims.ToDictionary(p=>p.ID);
    foreach(var placement in plans.SelectMany(p=>p.GetProperty("placements").EnumerateArray()))
    {
        var assetIds=placement.GetProperty("asset_ids").EnumerateArray().Select(id=>new UUID(id.GetString())).ToHashSet();
        var partIds=placement.GetProperty("entity_ids").EnumerateArray().Select(id=>new UUID(id.GetString())).ToArray();
        var root=byId[partIds[0]];
        foreach(var id in partIds)
        {
            var p=byId[id];
            if(p.Sculpt==null||!assetIds.Contains(p.Sculpt.SculptTexture)||id!=root.ID&&p.ParentID!=root.LocalID)throw new InvalidDataException("Native linked mesh binding differs from the world journal.");
        }
    }
    stage="capabilities";var caps=client.Network.CurrentSim.Caps??throw new InvalidDataException("Capabilities unavailable.");
    using var seed=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDArray{OSD.FromString("FinalverseWorld"),OSD.FromString("FetchInventory2")}),Encoding.UTF8,"application/llsd+xml");
    // Match the inherited LLSD capability contract without a charset suffix.
    seed.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
    var seedResponse=await http.PostAsync(caps.SeedCapsURI,seed);seedResponse.EnsureSuccessStatusCode();
    var map=(OSDMap)OSDParser.DeserializeLLSDXml(await seedResponse.Content.ReadAsStringAsync());
    Uri Capability(string name)
    {
        var uri=new Uri(map[name].AsString());
        if(uri.Scheme!="http"||uri.Authority!="127.0.0.1:18098"||uri.UserInfo!="")throw new InvalidDataException("Unexpected native capability origin.");
        return uri;
    }
    var cap=Capability("FinalverseWorld");
    stage="inventory";var cof=client.Inventory.FindFolderForType(FolderType.CurrentOutfit);
    var links=client.Inventory.FolderContents(cof,client.Self.AgentID,false,true,InventorySortOrder.ByName,15000)?.OfType<InventoryItem>().ToArray()??[];
    var items=links.Where(i=>i.AssetType==AssetType.Link).Select(i=>client.Inventory.FetchItem(i.AssetUUID,client.Self.AgentID,10000)).Where(i=>i!=null).ToArray();
    using(var diagnostic=new FileStream(args[2]+".inventory.json",FileMode.CreateNew,FileAccess.Write,FileShare.None))
    {
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[2]+".inventory.json",UnixFileMode.UserRead|UnixFileMode.UserWrite);
        JsonSerializer.Serialize(diagnostic,new{current_outfit=cof.ToString(),links=links.Select(i=>new{id=i.UUID.ToString(),asset_id=i.AssetUUID.ToString(),owner_id=i.OwnerID.ToString(),asset_type=i.AssetType.ToString(),inventory_type=i.InventoryType.ToString(),i.Flags}),
            items=items.Select(i=>new{id=i.UUID.ToString(),asset_id=i.AssetUUID.ToString(),owner_id=i.OwnerID.ToString(),asset_type=i.AssetType.ToString(),inventory_type=i.InventoryType.ToString(),i.Flags})});
    }
    // This old SDK reads item.agent_id, while the mature native wire format
    // carries item.permissions.owner_id. Verify the actual authenticated wire
    // response rather than accepting SDK zero-owner defaults or patching runtime.
    var fetched=links.Concat(items).ToDictionary(i=>i.UUID);
    var requested=new OSDArray();foreach(var i in fetched.Values)requested.Add(new OSDMap{["item_id"]=OSD.FromUUID(i.UUID),["owner_id"]=OSD.FromUUID(client.Self.AgentID)});
    using var inventoryBody=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDMap{["agent_id"]=OSD.FromUUID(client.Self.AgentID),["items"]=requested}),Encoding.UTF8,"application/llsd+xml");
    inventoryBody.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
    var inventoryResponse=await http.PostAsync(Capability("FetchInventory2"),inventoryBody);inventoryResponse.EnsureSuccessStatusCode();
    var native=(OSDMap)OSDParser.DeserializeLLSDXml(await inventoryResponse.Content.ReadAsStringAsync());
    var nativeItems=((OSDArray)native["items"]).Cast<OSDMap>().ToArray();
    if(native["agent_id"].AsUUID()!=client.Self.AgentID||nativeItems.Length!=fetched.Count||nativeItems.Select(i=>i["item_id"].AsUUID()).Distinct().Count()!=fetched.Count
        ||nativeItems.Any(i=>!fetched.TryGetValue(i["item_id"].AsUUID(),out var expected)||i["asset_id"].AsUUID()!=expected.AssetUUID||((OSDMap)i["permissions"])["owner_id"].AsUUID()!=client.Self.AgentID))throw new InvalidDataException("Native Current Outfit ownership or item binding differs from the request.");
    if(cof==UUID.Zero||links.Length<6||items.Length!=links.Count(i=>i.AssetType==AssetType.Link)||Enumerable.Range(0,6).Any(type=>!items.Any(i=>(i.Flags&255)==type&&i.AssetUUID!=UUID.Zero&&i.InventoryType==InventoryType.Wearable&&i.AssetType==(type<4?AssetType.Bodypart:AssetType.Clothing))))throw new InvalidDataException("Starter Current Outfit lacks its wearable bindings.");
    if(account.TryGetProperty("expect_genesis",out var genesis)&&genesis.GetBoolean()&&(!items.Any(i=>i.InventoryType==InventoryType.Attachment||i.AssetType==AssetType.Object)||!items.Any(i=>i.InventoryType==InventoryType.Wearable&&(i.Flags&255)==(uint)WearableType.Alpha)))throw new InvalidDataException("Genesis outfit lacks its attachment or alpha binding.");
    stage="semantics";
    bool expectRecovery=account.TryGetProperty("expect_recovery",out var recovery)&&recovery.GetBoolean();
    using(var inspectResponse=await http.PostAsync(cap,new StringContent("{\"op\":\"inspect\"}",Encoding.UTF8,"application/json")))
    {
        inspectResponse.EnsureSuccessStatusCode();var context=JsonDocument.Parse(await inspectResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("result");
        if(context.GetProperty("region").GetProperty("id").GetString()!=account.GetProperty("region_id").GetString())throw new InvalidDataException("Authenticated world region differs from the fixture.");
        if(context.GetProperty("recovery_required").GetBoolean()!=expectRecovery)throw new InvalidDataException("World recovery status is not observable in inspection.");
    }
    int blockedWrites=0;
    if(expectRecovery)
    {
        // Negative requests only after inspection positively confirms the fence.
        // The private operator harness retains/restores its own injected intent.
        foreach(var op in new[]{"prepare","commit","undo","content_preview"})
        {
            await Task.Delay(180);
            var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(new{op}),Encoding.UTF8,"application/json"));
            var error=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            if(response.StatusCode!=System.Net.HttpStatusCode.BadRequest||error.GetProperty("error").GetString()!="An interrupted world transaction requires administrator recovery.")throw new InvalidDataException("Recovery did not fence a world write before request execution.");
            ++blockedWrites;
        }
    }
    int semanticPlaces=0;
    foreach(var p in plans)
    {
        await Task.Delay(180);
        string id=p.GetProperty("placements")[0].GetProperty("entity_ids")[0].GetString()!;
        var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(new{op="semantic",entity_id=id}),Encoding.UTF8,"application/json"));response.EnsureSuccessStatusCode();
        var semantic=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("result");
        if(semantic.GetProperty("place").GetProperty("place_id").GetString()!=p.GetProperty("place").GetProperty("place_id").GetString()
            ||semantic.GetProperty("world_template").GetProperty("operation_id").GetString()!=proof.GetProperty("world").GetProperty("operation_id").GetString())throw new InvalidDataException("Missing canonical place or world provenance.");
        ++semanticPlaces;
    }
    stage="logout";client.Network.Logout();deadline=DateTime.UtcNow.AddSeconds(5);
    while(client.Network.Connected&&DateTime.UtcNow<deadline)await Task.Delay(50);
    logout=!client.Network.Connected;
    var result=new{schema_version=1,world_id=proof.GetProperty("world").GetProperty("world_id").GetString(),region_id=account.GetProperty("region_id").GetString(),home_login=true,start_location=login.StartLocation,
        expected_parts=ids.Count,matched_parts=ids.Count,semantic_places=semanticPlaces,current_outfit_links=links.Length,required_wearables=6,ownership_source="native permissions.owner_id",recovery_observed=expectRecovery,blocked_writes=blockedWrites,genesis_bindings_verified=account.TryGetProperty("expect_genesis",out var g)&&g.GetBoolean(),logout,appearance_sent=false};
    using var output=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write,FileShare.None);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[2],UnixFileMode.UserRead|UnixFileMode.UserWrite);
    JsonSerializer.Serialize(output,result,new JsonSerializerOptions{WriteIndented=true});
    Console.WriteLine($"WORLD_TEMPLATE_SMOKE home=true parts={ids.Count} places={semanticPlaces} outfit_links={links.Length} logout={logout}");return logout?0:1;
}
catch(Exception ex){Console.Error.WriteLine("WORLD_TEMPLATE_SMOKE_FAILED stage="+stage+" type="+ex.GetType().Name);
    if(ex is HttpRequestException request)Console.Error.WriteLine("WORLD_TEMPLATE_HTTP status="+request.StatusCode);
    if(ex is InvalidDataException&&!ex.Message.Contains("://"))Console.Error.WriteLine("WORLD_TEMPLATE_DETAIL "+ex.Message);return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
