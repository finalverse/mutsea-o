// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Human-authorized, isolated-grid import acceptance; never an LLM executor.
using System.Text.Json;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Finalverse.World;
using System.Collections.Concurrent;
if((args.Length is <3 or >5&&args.Length!=8)||args[2] is not ("import" or "verify" or "undo" or "verify_undo"))throw new ArgumentException("Expected private account path, receipt path and import|verify|undo|verify_undo, optional package ID/version, optional explicit loopback import XYZ.");
var account=JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
var grid=new Uri(account.GetProperty("grid_url").GetString()!);
// A remote test must be explicitly named in the private operator account file.
// No request, model output or simulator metadata can expand this authorization.
if(!grid.IsLoopback&&!(grid.Scheme=="https"&&grid.UserInfo==""&&grid.Query==""&&grid.Fragment==""&&account.TryGetProperty("content_test_grid",out var allowed)&&allowed.GetString()==grid.AbsoluteUri))throw new InvalidDataException("Use loopback or explicitly authorize one HTTPS development grid in the private account file.");
string packageId=args.Length>=4?args[3]:"mutsea.starter.path-lantern";
string packageVersion=args.Length>=5?args[4]:"0.1.0";
double[]? explicitAnchor=args.Length==8?args.Skip(5).Select(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray():null;
if(explicitAnchor!=null&&(!grid.IsLoopback||args[2]!="import"||explicitAnchor.Any(x=>!double.IsFinite(x)||x<1||x>255)))throw new InvalidDataException("Explicit import coordinates are bounded isolated-fixture setup only.");
Settings.LOG_LEVEL=Helpers.LogLevel.Error;
var client=new GridClient();client.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();
client.Settings.OBJECT_TRACKING=true;client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};string cap="";
var extraParams=new ConcurrentDictionary<UUID,byte[]>();
client.Objects.ObjectDataBlockUpdate+=(_,e)=>extraParams[e.Block.FullID]=e.Block.ExtraParams;
// The inherited probe SDK ignores the newer material extra parameter. Observe
// its original compressed packet too, using ObjectManager's documented layout;
// keep production SDK/runtime code unchanged.
client.Network.RegisterCallback(OpenMetaverse.Packets.PacketType.ObjectUpdateCompressed,(_,e)=>{
 foreach(var block in ((OpenMetaverse.Packets.ObjectUpdateCompressedPacket)e.Packet).ObjectData)
  if(CompressedExtra(block.Data) is {} extra)extraParams[new UUID(block.Data,0)]=extra;
});
async Task<JsonElement> World(object body){await Task.Delay(160);var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json"));var root=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;if(!response.IsSuccessStatusCode||!root.GetProperty("ok").GetBoolean())throw new InvalidDataException(root.GetProperty("error").GetString());return root.GetProperty("result").Clone();}
try{
 if(!client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"MutSea.ContentSmoke","home","0.1.0"))throw new InvalidDataException("Test login failed: "+client.Network.LoginMessage);
 var deadline=DateTime.UtcNow.AddSeconds(30);
 while(DateTime.UtcNow<deadline){if(client.Network.CurrentSim?.Caps!=null&&client.Network.CurrentSim.ObjectsPrimitives.Copy().Count>=40)break;await Task.Delay(200);}
 await Task.Delay(1500);
 var seed=new OSDArray{OSD.FromString("FinalverseWorld"),OSD.FromString("ViewerAsset"),OSD.FromString("GetObjectPhysicsData")};
 var seedContent=new StringContent(OSDParser.SerializeLLSDXmlString(seed),Encoding.UTF8);
 seedContent.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
 var caps=client.Network.CurrentSim?.Caps??throw new InvalidDataException("No active simulator capabilities.");
 var response=await http.PostAsync(caps.SeedCapsURI,seedContent);
 response.EnsureSuccessStatusCode();var capabilities=(OSDMap)OSDParser.DeserializeLLSDXml(await response.Content.ReadAsStringAsync());cap=capabilities["FinalverseWorld"].AsString();
 if(cap.Length>0){var origin=new Uri(cap);if(origin.Scheme!=grid.Scheme||origin.Authority!=grid.Authority||origin.UserInfo!="")throw new InvalidDataException("Unexpected World capability origin.");}
 if(cap.Length==0)throw new InvalidDataException("No active World capability.");
 var assetCap=capabilities["ViewerAsset"].AsString();
 if(assetCap.Length>0){var origin=new Uri(assetCap);if(origin.Scheme!=grid.Scheme||origin.Authority!=grid.Authority||origin.UserInfo!=""||origin.Query!=""||origin.Fragment!="")throw new InvalidDataException("Unexpected native asset capability origin.");}
 JsonElement receipt;
 if(args[2]=="import"){
  if(File.Exists(args[1]))throw new InvalidDataException("Preserve the prior receipt; choose a new path.");
  // Named fixture's clear creator site, outside the protected arrival plaza.
  // This is acceptance setup, not destination/product coordinate routing.
  var teleport=new OpenMetaverse.Packets.TeleportLocationRequestPacket();
  teleport.AgentData.AgentID=client.Self.AgentID;teleport.AgentData.SessionID=client.Self.SessionID;
  var setup=explicitAnchor==null?new Vector3(168,128,27):new Vector3((float)explicitAnchor[0],(float)explicitAnchor[1]+4,(float)explicitAnchor[2]+2);
  teleport.Info.RegionHandle=client.Network.CurrentSim.Handle;teleport.Info.Position=setup;teleport.Info.LookAt=Vector3.UnitY;client.Network.SendPacket(teleport);
  deadline=DateTime.UtcNow.AddSeconds(15);
  while(DateTime.UtcNow<deadline){await Task.Delay(250);var location=await World(new{op="inspect"});var v=location.GetProperty("avatar").GetProperty("position");if(Math.Abs(v[0].GetDouble()-setup.X)<1&&Math.Abs(v[1].GetDouble()-setup.Y)<1)break;}
  var before=await World(new{op="inspect"});var pos=before.GetProperty("avatar").GetProperty("position");
  var site=args[1]+".site-before.json";File.WriteAllText(site,before.GetRawText());if(!OperatingSystem.IsWindows())File.SetUnixFileMode(site,UnixFileMode.UserRead|UnixFileMode.UserWrite);
  JsonElement plan=default;bool found=false;
  // Bounded site search around the authenticated avatar; the server validates
  // every candidate against terrain, all objects/paths and native parcel policy.
  var candidates=explicitAnchor!=null?new[]{explicitAnchor}:new[]{new[]{8d,8d},new[]{-8d,8d},new[]{8d,-8d},new[]{-8d,-8d},new[]{12d,0d},new[]{-12d,0d},new[]{0d,12d},new[]{0d,-12d}}.Select(delta=>new[]{pos[0].GetDouble()+delta[0],pos[1].GetDouble()+delta[1],before.GetProperty("terrain").GetProperty("height").GetDouble()}).ToArray();
  foreach(var anchor in candidates){
   try{plan=await World(new{op="content_preview",package_id=packageId,version=packageVersion,anchor});found=true;break;}catch(InvalidDataException){}
  }
  if(!found)throw new InvalidDataException("No clear small-asset site near this avatar. Move the test avatar to dry clear ground.");
  var entry=await World(new{op="content_commit",plan_id=plan.GetProperty("plan_id").GetString()});
  receipt=JsonSerializer.SerializeToElement(new{entry,before});File.WriteAllText(args[1],receipt.GetRawText()+"\n");if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);
 }else receipt=JsonDocument.Parse(File.ReadAllBytes(args[1])).RootElement.Clone();
 var saved=receipt.GetProperty("entry");var savedPlan=saved.GetProperty("plan");var ids=savedPlan.GetProperty("entity_ids").EnumerateArray().Select(x=>new UUID(x.GetString())).ToArray();var assets=savedPlan.GetProperty("asset_ids").EnumerateArray().Select(x=>new UUID(x.GetString())).ToArray();
 if(args[2]=="undo")await World(new{op="content_undo",plan_id=savedPlan.GetProperty("plan_id").GetString()});
 bool exists=args[2] is "import" or "verify";
 foreach(var id in ids){deadline=DateTime.UtcNow.AddSeconds(20);Primitive? prim;
  do{prim=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(x=>x.ID==id);if((prim!=null)==exists)break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
  if((prim!=null)!=exists)throw new InvalidDataException("UDP import existence mismatch.");
  if(exists&&(prim!.Sculpt==null||!assets.Contains(prim.Sculpt.SculptTexture)))throw new InvalidDataException("UDP mesh asset binding mismatch.");
 }
 var semantic=await World(new{op="semantic",entity_id=ids[0].ToString()});
 if(exists&&semantic.GetProperty("package_id").GetString()!=savedPlan.GetProperty("package").GetProperty("package_id").GetString())throw new InvalidDataException("Missing canonical content semantics.");
 int semanticComponents=0;
 int materialFaces=0,materialAssets=0,meshAssets=0,collisionShapes=0,nativeLights=0;bool seatResponse=false,occupiedUndoDenied=false;
 if(exists){
  var parts=savedPlan.GetProperty("package").GetProperty("parts");
  for(int i=0;i<ids.Length;i++){
   if(!parts[i].TryGetProperty("semantics",out var metadata)||metadata.ValueKind==JsonValueKind.Null)continue; // legacy journal
   var observed=i==0?semantic:await World(new{op="semantic",entity_id=ids[i].ToString()});
   if(!observed.GetProperty("semantic_metadata_available").GetBoolean()||observed.GetProperty("semantic_type").GetString()!=parts[i].GetProperty("semantic_type").GetString()
    ||!observed.GetProperty("affordances").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(metadata.GetProperty("affordances").EnumerateArray().Select(x=>x.GetString()))
    ||!observed.GetProperty("tags").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(metadata.GetProperty("tags").EnumerateArray().Select(x=>x.GetString())))throw new InvalidDataException("Canonical semantic metadata mismatch.");
   var expected=metadata.GetProperty("relationships").EnumerateArray().ToArray();var actual=observed.GetProperty("relationships").EnumerateArray().ToArray();
   if(actual.Length!=expected.Length)throw new InvalidDataException("Canonical relationship count mismatch.");
   for(int j=0;j<expected.Length;j++){
    int target=parts.EnumerateArray().Select((p,index)=>(p,index)).Single(x=>x.p.GetProperty("local_id").GetString()==expected[j].GetProperty("target").GetString()).index;
    if(actual[j].GetProperty("type").GetString()!=expected[j].GetProperty("type").GetString()||actual[j].GetProperty("target").GetString()!=ids[target].ToString())throw new InvalidDataException("Canonical relationship target mismatch.");
   }
   semanticComponents++;
  }
  if(savedPlan.GetProperty("package").GetProperty("schema_version").GetInt32()==3)
  {
   var plan=savedPlan.Deserialize<ContentPlan>(ContentCatalog.Json)!;
   if(assetCap.Length==0)throw new InvalidDataException("Native material capability is unavailable.");
   foreach(var material in plan.package.materials!)
   {
    var bytes=await http.GetByteArrayAsync(assetCap+"?material_id="+ContentMaterialAsset.Id(plan,material.local_id));
    if(ContentCatalog.Hash(bytes)!=material.asset_sha256||!ContentMaterialAsset.Matches(bytes,material.factors))throw new InvalidDataException("Native PBR material delivery mismatch.");
    materialAssets++;
   }
   for(int i=0;i<ids.Length;i++)
   {
    var payload=await http.GetByteArrayAsync(assetCap+"?mesh_id="+assets[i]);
    if(ContentCatalog.Hash(payload)!=plan.package.parts[i].mesh_sha256)throw new InvalidDataException("Native mesh/LOD delivery mismatch.");
    meshAssets++;
    var primitive=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Single(p=>p.ID==ids[i]);
    if(plan.package.parts[i].light is {} light)
    {
     var observed=primitive.Light;
     if(observed==null||Math.Abs(observed.Radius-light.radius)>.0001||Math.Abs(observed.Falloff-light.falloff)>.0001||Math.Abs(observed.Intensity-light.intensity)>1d/255||Math.Abs(observed.Color.R-light.color_linear[0])>1d/255||Math.Abs(observed.Color.G-light.color_linear[1])>1d/255||Math.Abs(observed.Color.B-light.color_linear[2])>1d/255)throw new InvalidDataException("Native linear light extra parameter mismatch.");
     nativeLights++;
    }
    client.Objects.RequestObject(client.Network.CurrentSim,primitive.LocalID);
    deadline=DateTime.UtcNow.AddSeconds(10);
    while(!extraParams.ContainsKey(ids[i])&&DateTime.UtcNow<deadline)await Task.Delay(100);
    if(!extraParams.TryGetValue(ids[i],out var extra))throw new InvalidDataException("Native full object update unavailable for material validation.");
    var bindings=ReadMaterials(extra);
    if(bindings.Length!=plan.package.parts[i].faces.Length)throw new InvalidDataException("Native face material count mismatch.");
    for(int face=0;face<bindings.Length;face++)
    {
     if(bindings[face].te_index!=face||bindings[face].id!=ContentMaterialAsset.Id(plan,plan.package.parts[i].faces[face].material_id!))throw new InvalidDataException("UDP native PBR face binding mismatch.");
     if(primitive.Textures.GetFace((uint)face).Fullbright)throw new InvalidDataException("PBR bench must not be fullbright.");
     materialFaces++;
    }
   }
   if(plan.package.parts.Any(p=>p.collision!=null))
   {
    var physicsCap=capabilities["GetObjectPhysicsData"].AsString();
    if(!Uri.TryCreate(physicsCap,UriKind.Absolute,out var origin)||origin.Scheme!=grid.Scheme||origin.Authority!=grid.Authority||origin.UserInfo!=""||origin.Query!=""||origin.Fragment!="")throw new InvalidDataException("Unexpected native physics capability origin.");
    // Inherited MutSea.Framework.PhysShapeType wire values: none=1, convex=2.
    for(int i=0;i<ids.Length;i++)if(plan.package.parts[i].collision is {} collision)
    {
     // The inherited handler closes its outer LLSD map inside its ID loop.
     // Query one ID at a time instead of accepting a truncated multi-ID reply.
     var requested=new OSDMap { ["object_ids"]=new OSDArray{OSD.FromUUID(ids[i])} };
     var content=new StringContent(OSDParser.SerializeLLSDXmlString(requested),Encoding.UTF8,"application/llsd+xml");
     var reply=await http.PostAsync(physicsCap,content);reply.EnsureSuccessStatusCode();
     var physics=(OSDMap)OSDParser.DeserializeLLSDXml(await reply.Content.ReadAsStringAsync());
     if(!physics.TryGetValue(ids[i].ToString(),out var value)||((OSDMap)value)["PhysicsShapeType"].AsInteger()!=(collision.shape=="none"?1:2))throw new InvalidDataException("Native collision shape mismatch.");
     collisionShapes++;
    }
   }
   var seatIndex=Array.FindIndex(plan.package.parts,p=>p.seat!=null);
   if(seatIndex>=0)
   {
    var seat=plan.package.parts[seatIndex].seat!;
    var sit=new TaskCompletionSource<AvatarSitResponseEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Self.AvatarSitResponse+=(_,e)=>sit.TrySetResult(e);
    client.Self.RequestSit(ids[seatIndex],Vector3.Zero);
    var ready=await Task.WhenAny(sit.Task,Task.Delay(10000));
    if(ready!=sit.Task)throw new InvalidDataException("Native sit response timed out.");
    var result=await sit.Task;
    if(result.ObjectID!=ids[0]||Vector3.Distance(result.SitPosition,new Vector3((float)seat.position[0],(float)seat.position[1],(float)seat.position[2]))>.002f)throw new InvalidDataException("Native sit target mismatch.");
    double alignment=Math.Abs(result.SitRotation.X*seat.rotation[0]+result.SitRotation.Y*seat.rotation[1]+result.SitRotation.Z*seat.rotation[2]+result.SitRotation.W*seat.rotation[3]);
    if(Math.Abs(alignment-1)>.002)throw new InvalidDataException("Native sit orientation mismatch.");
    deadline=DateTime.UtcNow.AddSeconds(10);while(client.Self.SittingOn==0&&DateTime.UtcNow<deadline)await Task.Delay(100);
    if(client.Self.SittingOn==0)throw new InvalidDataException("Native avatar did not sit.");
    seatResponse=true;
    var historyBefore=await World(new{op="content_history"});
    try{await World(new{op="content_undo",plan_id=plan.plan_id});throw new InvalidDataException("Occupied seating Undo was accepted.");}
    catch(InvalidDataException error) when(error.Message=="Stand up before undoing occupied seating."){occupiedUndoDenied=true;}
    var historyAfter=await World(new{op="content_history"});
    if(historyBefore.GetArrayLength()!=historyAfter.GetArrayLength())throw new InvalidDataException("Occupied seating denial wrote an undo intent.");
    client.Self.Stand();await Task.Delay(300);
   }
  }
 }
 var after=await World(new{op="inspect"});var history=await World(new{op="content_history"});
 // Citizen inspection is controller-only and is tested separately with its
 // controller account. Ordinary content creators do not need that privilege.
 var output=new{mode=args[2],udp_mesh_parts_verified=ids.Length,semantic_components_verified=semanticComponents,material_faces_verified=materialFaces,material_assets_verified=materialAssets,mesh_lod_assets_verified=meshAssets,native_collision_shapes_verified=collisionShapes,native_lights_verified=nativeLights,native_seat_verified=seatResponse,occupied_seat_undo_denied=occupiedUndoDenied,exists,semantic,content_history=history,world_inspect=after};
 var evidence=args[1]+"."+args[2]+".json";File.WriteAllText(evidence,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true})+"\n");if(!OperatingSystem.IsWindows())File.SetUnixFileMode(evidence,UnixFileMode.UserRead|UnixFileMode.UserWrite);
 Console.WriteLine(JsonSerializer.Serialize(new{mode=args[2],verified=true,parts=ids.Length,exists,semantic=exists,semantic_components_verified=semanticComponents,material_faces_verified=materialFaces,material_assets_verified=materialAssets,mesh_lod_assets_verified=meshAssets,native_collision_shapes_verified=collisionShapes,native_lights_verified=nativeLights,native_seat_verified=seatResponse,occupied_seat_undo_denied=occupiedUndoDenied,worldline_entries=history.GetArrayLength()}));
 return 0;
}catch(Exception error){
 var diagnostic=args[1]+"."+args[2]+".diagnostic";File.WriteAllText(diagnostic,error.ToString());if(!OperatingSystem.IsWindows())File.SetUnixFileMode(diagnostic,UnixFileMode.UserRead|UnixFileMode.UserWrite);
 Console.Error.WriteLine("CONTENT_SMOKE FAIL: "+error.GetType().Name+" (private diagnostic saved)");return 1;
}finally{if(client.Network.Connected)client.Network.Logout();}

static Primitive.RenderMaterials.RenderMaterialEntry[] ReadMaterials(byte[] extra)
{
 if(extra.Length<1)throw new InvalidDataException("Empty native extra parameters.");
 int pos=1;
 for(int i=0;i<extra[0];i++)
 {
  if(pos+6>extra.Length)throw new InvalidDataException("Truncated native extra parameters.");
  var type=Utils.BytesToUInt16(extra,pos);int size=(int)Utils.BytesToUInt(extra,pos+2);pos+=6;
  if(size<0||size>extra.Length-pos)throw new InvalidDataException("Invalid native extra parameter length.");
  if(type==0x80)return new Primitive.RenderMaterials(extra,pos,size).entries??[];
  pos+=size;
 }
 return [];
}

static byte[]? CompressedExtra(byte[] data)
{
 // Layout evidence: inherited OpenMetaverse/ObjectManager.cs f3ee229, and
 // MutSea's LLClientView compressed serializer. This observer extracts only
 // bounded extra parameters; it never executes media, scripts or callbacks.
 if(data.Length<85)return null;
 var flags=(CompressedFlags)Utils.BytesToUInt(data,64);int pos=84;
 bool Has(CompressedFlags flag)=>(flags&flag)!=0;
 bool Advance(int count){if(count<0||count>data.Length-pos)return false;pos+=count;return true;}
 bool String(){while(pos<data.Length&&data[pos]!=0)pos++;return Advance(1);}
 if(Has(CompressedFlags.HasAngularVelocity)&&!Advance(12))return null;
 if(Has(CompressedFlags.HasParent)&&!Advance(4))return null;
 if(Has(CompressedFlags.Tree)){if(!Advance(1))return null;}
 else if(Has(CompressedFlags.ScratchPad)){if(pos>=data.Length)return null;int count=data[pos++];if(!Advance(count))return null;}
 if(Has(CompressedFlags.HasText)&&(!String()||!Advance(4)))return null;
 if(Has(CompressedFlags.MediaURL)&&!String())return null;
 if(Has(CompressedFlags.HasParticles)&&!Advance(86))return null;
 if(pos>=data.Length)return null;int start=pos,countExtra=data[pos++];
 for(int i=0;i<countExtra;i++)
 {
  if(pos+6>data.Length)return null;int length=(int)Utils.BytesToUInt(data,pos+2);
  if(!Advance(6)||!Advance(length))return null;
 }
 return data.Skip(start).Take(pos-start).ToArray();
}
