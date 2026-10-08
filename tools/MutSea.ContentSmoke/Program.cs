// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Human-authorized, isolated-grid import acceptance; never an LLM executor.
using System.Text.Json;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
if(args.Length is <3 or >5||args[2] is not ("import" or "verify" or "undo" or "verify_undo"))throw new ArgumentException("Expected private account path, NEW receipt path and import|verify|undo|verify_undo, optional approved package ID and version.");
var account=JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
var grid=new Uri(account.GetProperty("grid_url").GetString()!);
// A remote test must be explicitly named in the private operator account file.
// No request, model output or simulator metadata can expand this authorization.
if(!grid.IsLoopback&&!(grid.Scheme=="https"&&grid.UserInfo==""&&grid.Query==""&&grid.Fragment==""&&account.TryGetProperty("content_test_grid",out var allowed)&&allowed.GetString()==grid.AbsoluteUri))throw new InvalidDataException("Use loopback or explicitly authorize one HTTPS development grid in the private account file.");
string packageId=args.Length>=4?args[3]:"mutsea.starter.path-lantern";
string packageVersion=args.Length==5?args[4]:"0.1.0";
Settings.LOG_LEVEL=Helpers.LogLevel.Error;
var client=new GridClient();client.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();
client.Settings.OBJECT_TRACKING=true;client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};string cap="";
async Task<JsonElement> World(object body){await Task.Delay(160);var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json"));var root=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;if(!response.IsSuccessStatusCode||!root.GetProperty("ok").GetBoolean())throw new InvalidDataException(root.GetProperty("error").GetString());return root.GetProperty("result").Clone();}
try{
 if(!client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"MutSea.ContentSmoke","home","0.1.0"))throw new InvalidDataException("Test login failed: "+client.Network.LoginMessage);
 var deadline=DateTime.UtcNow.AddSeconds(30);
 while(DateTime.UtcNow<deadline){if(client.Network.CurrentSim?.Caps!=null&&client.Network.CurrentSim.ObjectsPrimitives.Copy().Count>=40)break;await Task.Delay(200);}
 await Task.Delay(1500);
 var seed=new OSDArray{OSD.FromString("FinalverseWorld")};
 var seedContent=new StringContent(OSDParser.SerializeLLSDXmlString(seed),Encoding.UTF8);
 seedContent.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
 var caps=client.Network.CurrentSim?.Caps??throw new InvalidDataException("No active simulator capabilities.");
 var response=await http.PostAsync(caps.SeedCapsURI,seedContent);
 response.EnsureSuccessStatusCode();var capabilities=(OSDMap)OSDParser.DeserializeLLSDXml(await response.Content.ReadAsStringAsync());cap=capabilities["FinalverseWorld"].AsString();
 if(cap.Length>0){var origin=new Uri(cap);if(origin.Scheme!=grid.Scheme||origin.Authority!=grid.Authority||origin.UserInfo!="")throw new InvalidDataException("Unexpected World capability origin.");}
 if(cap.Length==0)throw new InvalidDataException("No active World capability.");
 JsonElement receipt;
 if(args[2]=="import"){
  if(File.Exists(args[1]))throw new InvalidDataException("Preserve the prior receipt; choose a new path.");
  // Named fixture's clear creator site, outside the protected arrival plaza.
  // This is acceptance setup, not destination/product coordinate routing.
  var teleport=new OpenMetaverse.Packets.TeleportLocationRequestPacket();
  teleport.AgentData.AgentID=client.Self.AgentID;teleport.AgentData.SessionID=client.Self.SessionID;
  teleport.Info.RegionHandle=client.Network.CurrentSim.Handle;teleport.Info.Position=new Vector3(168,128,27);teleport.Info.LookAt=Vector3.UnitY;client.Network.SendPacket(teleport);
  deadline=DateTime.UtcNow.AddSeconds(15);
  while(DateTime.UtcNow<deadline){await Task.Delay(250);var location=await World(new{op="inspect"});var v=location.GetProperty("avatar").GetProperty("position");if(Math.Abs(v[0].GetDouble()-168)<1&&Math.Abs(v[1].GetDouble()-128)<1)break;}
  var before=await World(new{op="inspect"});var pos=before.GetProperty("avatar").GetProperty("position");
  var site=args[1]+".site-before.json";File.WriteAllText(site,before.GetRawText());if(!OperatingSystem.IsWindows())File.SetUnixFileMode(site,UnixFileMode.UserRead|UnixFileMode.UserWrite);
  JsonElement plan=default;bool found=false;
  // Bounded site search around the authenticated avatar; the server validates
  // every candidate against terrain, all objects/paths and native parcel policy.
  foreach(var delta in new[]{new[]{8d,8d},new[]{-8d,8d},new[]{8d,-8d},new[]{-8d,-8d},new[]{12d,0d},new[]{-12d,0d},new[]{0d,12d},new[]{0d,-12d}}){
   try{plan=await World(new{op="content_preview",package_id=packageId,version=packageVersion,anchor=new[]{pos[0].GetDouble()+delta[0],pos[1].GetDouble()+delta[1],before.GetProperty("terrain").GetProperty("height").GetDouble()}});found=true;break;}catch(InvalidDataException){}
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
 }
 var after=await World(new{op="inspect"});var history=await World(new{op="content_history"});
 // Citizen inspection is controller-only and is tested separately with its
 // controller account. Ordinary content creators do not need that privilege.
 var output=new{mode=args[2],udp_mesh_parts_verified=ids.Length,semantic_components_verified=semanticComponents,exists,semantic,content_history=history,world_inspect=after};
 var evidence=args[1]+"."+args[2]+".json";File.WriteAllText(evidence,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true})+"\n");if(!OperatingSystem.IsWindows())File.SetUnixFileMode(evidence,UnixFileMode.UserRead|UnixFileMode.UserWrite);
 Console.WriteLine(JsonSerializer.Serialize(new{mode=args[2],verified=true,parts=ids.Length,exists,semantic=exists,semantic_components_verified=semanticComponents,worldline_entries=history.GetArrayLength()}));
 return 0;
}catch(Exception error){
 var diagnostic=args[1]+"."+args[2]+".diagnostic";File.WriteAllText(diagnostic,error.ToString());if(!OperatingSystem.IsWindows())File.SetUnixFileMode(diagnostic,UnixFileMode.UserRead|UnixFileMode.UserWrite);
 Console.Error.WriteLine("CONTENT_SMOKE FAIL: "+error.GetType().Name+" (private diagnostic saved)");return 1;
}finally{if(client.Network.Connected)client.Network.Logout();}
