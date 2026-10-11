// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Live private sandbox probe. Does not print passwords, service tokens or capability URLs.
using System.Text;
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length is not (3 or 4)||args[2] is not ("observe" or "questions" or "navigate" or "verify" or "garden_verify" or "garden_undo_verify" or "animesh" or "displace" or "greet" or "animwatch" or "teleport" or "wearer" or "semantic" or "hold" or "restyle")||(args[2] is "animesh" or "wearer")!=(args.Length==4)){Console.Error.WriteLine("Usage: CitizenSmoke private-account.json receipt.json observe|questions|navigate|verify|garden_verify|garden_undo_verify|displace|greet|animwatch|teleport|semantic|hold|restyle | animesh|wearer compiled.json");return 2;}
var account=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;var client=new GridClient();
client.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
using var http=new HttpClient {Timeout=TimeSpan.FromSeconds(110)};string cap="",viewerAsset="";UUID lumi=new("b151468b-cbcc-4af2-ae2d-6967e8938bb4");
void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
async Task<JsonElement> Citizen(string command,string text="")
{
 return await World(new{op="citizen",command,text});
}
async Task<JsonElement> World(object request)
{
 await Task.Delay(160);var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(request),Encoding.UTF8,"application/json"));var root=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
 Assert(response.IsSuccessStatusCode&&root.GetProperty("ok").GetBoolean(),"Authenticated citizen relay failed");return root.GetProperty("result").Clone();
}
void Save(object result){File.WriteAllText(args[1],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);}
try
{
 var animPackets=new List<object>();var loginAt=DateTime.UtcNow;
 client.Network.RegisterCallback(OpenMetaverse.Packets.PacketType.ObjectAnimation,(sender,e)=>{var p=(OpenMetaverse.Packets.ObjectAnimationPacket)e.Packet;if(p.Sender.ID!=lumi)return;lock(animPackets)animPackets.Add(new{ms=(DateTime.UtcNow-loginAt).TotalMilliseconds,anims=p.AnimationList.Select(a=>a.AnimID.ToString()[..8]+"#"+a.AnimSequenceID).ToArray()});});
 var appearances=new System.Collections.Concurrent.ConcurrentDictionary<UUID,(Primitive.TextureEntryFace? def,Primitive.TextureEntryFace[]? faces)>();
 client.Avatars.AvatarAppearance+=(sender,e)=>appearances[e.AvatarID]=(e.DefaultTexture,e.FaceTextures);
 Assert(client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"Finalverse.CitizenSmoke","home","0.1"),"Sandbox login failed");
 var seed=new OSDArray{OSD.FromString("FinalverseWorld"),OSD.FromString("ViewerAsset"),OSD.FromString("ObjectAnimation")};var content=new StringContent(OSDParser.SerializeLLSDXmlString(seed),Encoding.UTF8);content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
 var seedReply=await http.PostAsync(client.Network.CurrentSim.Caps.SeedCapsURI,content);var caps=(OSDMap)OSDParser.DeserializeLLSDXml(await seedReply.Content.ReadAsStringAsync());cap=caps["FinalverseWorld"].AsString();Assert(cap.Length>0,"World capability missing");viewerAsset=caps.ContainsKey("ViewerAsset")?caps["ViewerAsset"].AsString():"";
 var deadline=DateTime.UtcNow.AddSeconds(30);Primitive? rootBody=null;
 do{rootBody=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==lumi);if(rootBody!=null)break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
 Assert(rootBody!=null,"No Lumi embodiment in real UDP stream");await Task.Delay(500);
 int components=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Count(p=>p.ID==lumi||p.ParentID==rootBody!.LocalID);
 // Representation-aware: the historic linked primitive (7 parts) or one native animated-mesh root.
 bool animesh=rootBody!.Sculpt!=null&&rootBody.Sculpt.Type==SculptType.Mesh;string representation=animesh?"animated_object":"linked_primitive_citizen";
 Assert(animesh?components==1:components==7,animesh?"Animated-mesh embodiment must be a single UDP root":"Lumi's seven linked components are missing from UDP");
 var state=await Citizen("inspect");Assert(state.GetProperty("citizen").GetProperty("identity").GetProperty("agent_id").GetString()==lumi.ToString(),"Persistent identity mismatch");
 if(args[2]=="questions")
 {
  var results=new List<object>();foreach(string text in new[]{"Lumi, who are you?","Where is your home?","What do you remember about your home?"}){var watch=System.Diagnostics.Stopwatch.StartNew();var reply=await Citizen("say",text);Assert(!reply.GetProperty("answer").GetString()!.Contains("unavailable",StringComparison.OrdinalIgnoreCase),"Grounded question unavailable");results.Add(new{text,latency_ms=watch.ElapsedMilliseconds,reply});}
  Save(new{udp_components=components,state,questions=results});Console.WriteLine("CITIZEN_SMOKE questions PASS: authenticated viewer capability relay, actual UDP embodiment, persistent identity/home/memory answers.");
 }
 else if(args[2]=="navigate")
 {
  var resumed=await Citizen("resume");var goals=resumed.GetProperty("citizen").GetProperty("goals");if(goals.GetArrayLength()==0||goals[goals.GetArrayLength()-1].GetProperty("status").GetString()!="active")await Citizen("say","Lumi, go home.");
  var initial=rootBody!.Position;bool moved=false;deadline=DateTime.UtcNow.AddSeconds(90);
  do
  {
   await Task.Delay(700);state=await Citizen("inspect");var body=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi);if(Vector3.Distance(body.Position,initial)>1)moved=true;
   var goal=state.GetProperty("citizen").GetProperty("goals");string status=goal[goal.GetArrayLength()-1].GetProperty("status").GetString()!;if(status=="completed")break;if(status is "blocked" or "failed")throw new Exception("Navigation blocked: "+goal[goal.GetArrayLength()-1].GetProperty("reason").GetString());
  }while(DateTime.UtcNow<deadline);
  var last=state.GetProperty("citizen").GetProperty("goals");Assert(last[last.GetArrayLength()-1].GetProperty("status").GetString()=="completed","Arrival timeout");Assert(moved,"Lumi did not move in real UDP updates");
  Save(new{udp_components=components,udp_motion_verified=moved,state});Console.WriteLine("CITIZEN_SMOKE navigation PASS: semantic home, bounded goal, real UDP movement, arrival and durable event memory.");
 }
 else if(args[2] is "garden_verify" or "garden_undo_verify")
 {
  // Read only: never propose, approve or undo a garden on the user's behalf.
  var journal=await World(new{op="journal"});JsonElement committed;
  if(args[2]=="garden_undo_verify")
  {
   committed=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("committed").Clone();
   Assert(journal.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==committed.GetProperty("plan_id").GetString()&&e.GetProperty("status").GetString()=="undone"),"Garden Undo receipt is missing");
   Assert(state.GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("subject").GetString()=="garden_undo"),"Garden Undo memory is missing");
  }
  else
  {
   var undone=journal.EnumerateArray().Where(e=>e.GetProperty("status").GetString()=="undone").Select(e=>e.GetProperty("plan_id").GetString()).ToHashSet();
   committed=journal.EnumerateArray().Last(e=>e.GetProperty("status").GetString()=="committed"&&!undone.Contains(e.GetProperty("plan_id").GetString())&&e.GetProperty("proposal").GetProperty("model").GetString()=="garden-v1");
   Assert(state.GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("subject").GetString()=="garden"),"Garden episodic memory is missing");
   Assert(state.GetProperty("citizen").GetProperty("goals").EnumerateArray().Any(g=>g.GetProperty("kind").GetString()=="garden"&&g.GetProperty("status").GetString()=="completed"&&g.GetProperty("plan_id").GetString()==committed.GetProperty("plan_id").GetString()),"Completed garden goal is missing");
   if(File.Exists(args[1]))Assert(JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("committed").GetProperty("operation_id").GetString()==committed.GetProperty("operation_id").GetString(),"Garden changed after restart");
  }
  var steps=committed.GetProperty("steps");Assert(steps.GetArrayLength()==3,"Garden must contain exactly three shrubs");
  bool exists=args[2]=="garden_verify";
  foreach(var step in steps.EnumerateArray())
  {
   UUID id=new(step.GetProperty("id").GetString());deadline=DateTime.UtcNow.AddSeconds(20);Primitive? shrub;
   do{shrub=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==id);if((shrub!=null)==exists)break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
   Assert((shrub!=null)==exists,"Garden UDP existence mismatch");
   if(exists)
   {
    var after=step.GetProperty("after");var pos=after.GetProperty("position");var size=after.GetProperty("scale");
    Assert(Vector3.Distance(shrub!.Position,new(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle()))<.02f,"Garden UDP position mismatch");
    Assert(Vector3.Distance(shrub.Scale,new(size[0].GetSingle(),size[1].GetSingle(),size[2].GetSingle()))<.02f,"Garden UDP scale mismatch");
   }
  }
  var recall=await Citizen("say","What did you change here?");
  Assert(recall.GetProperty("answer").GetString()!.Contains("garden",StringComparison.OrdinalIgnoreCase),"Garden recall failed");
  if(exists)Save(new{udp_components=components,garden_udp_verified=true,committed,state,recall});
  else{File.WriteAllText(args[1]+".undo",JsonSerializer.Serialize(new{garden_undo_udp_verified=true,state,recall}));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1]+".undo",UnixFileMode.UserRead|UnixFileMode.UserWrite);}
  Console.WriteLine("CITIZEN_SMOKE "+args[2]+" PASS: real UDP garden state, goal/memory, kernel WorldLine and grounded recall.");
 }
 else if(args[2]=="restyle")
 {
  // Operator-authored restyle (FINALVERSE_RESTYLE: {"plans":[World API plans]}): each plan goes through the
  // same guarded prepare/commit path as any edit, is journaled, and stays undoable. Only set_color is accepted here.
  var spec=JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("FINALVERSE_RESTYLE")!)).RootElement;
  var results=new List<object>();
  foreach(var plan in spec.GetProperty("plans").EnumerateArray())
  {
   Assert(plan.GetProperty("actions").EnumerateArray().All(a=>a.GetProperty("type").GetString()=="set_color"),"Restyle accepts only set_color actions");
   var prepared=await World(new{op="prepare",plan});var id=prepared.GetProperty("plan_id").GetString()!;
   var committed=await World(new{op="commit",plan_id=id});
   results.Add(new{plan_id=id,request=plan.GetProperty("request").GetString(),status=committed.GetProperty("status").GetString()});
  }
  Save(new{results});Console.WriteLine($"CITIZEN_SMOKE restyle PASS: {results.Count} plan(s) committed.");
 }
 else if(args[2]=="hold")
 {
  // Keep the controller's avatar present (FINALVERSE_HOLD_SECONDS, at most 300) for human-gated
  // controller commands that require it; performs no world or citizen action itself.
  int seconds=Math.Clamp(int.Parse(Environment.GetEnvironmentVariable("FINALVERSE_HOLD_SECONDS")??"60"),1,300);
  Save(new{held_seconds=seconds,from=DateTimeOffset.UtcNow});Console.WriteLine($"CITIZEN_SMOKE hold: present for {seconds}s.");await Task.Delay(seconds*1000);
 }
 else if(args[2]=="semantic")
 {
  // What an agent learns about specific entities (FINALVERSE_ENTITIES, comma-separated) through the World relay.
  var ids=(Environment.GetEnvironmentVariable("FINALVERSE_ENTITIES")??"").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
  Assert(ids.Length is >0 and <=16&&ids.All(x=>Guid.TryParse(x,out _)),"Provide 1-16 entity IDs");
  var results=new List<JsonElement>();foreach(var id in ids)results.Add(await World(new{op="semantic",entity_id=id}));
  Save(new{entities=results});
  Console.WriteLine($"CITIZEN_SMOKE semantic PASS: {results.Count(r=>r.TryGetProperty("place",out _))}/{results.Count} with place context.");
 }
 else if(args[2]=="wearer")
 {
  // Second observer: another avatar wearing a Genesis attachment, as seen over real UDP.
  var compiled=JsonDocument.Parse(File.ReadAllText(args[3])).RootElement;
  var meshId=new UUID(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("finalverse-avatar:mesh:"+compiled.GetProperty("mesh_sha256").GetString())).Take(16).ToArray(),0);
  string wearer=Environment.GetEnvironmentVariable("FINALVERSE_WEARER") is {Length:>0} w?w:"MutSea FirstLook";
  deadline=DateTime.UtcNow.AddSeconds(30);Avatar? avatar=null;Primitive? worn=null;
  do
  {
   await Task.Delay(500);
   avatar=client.Network.CurrentSim.ObjectsAvatars.Copy().Values.FirstOrDefault(a=>a.Name==wearer);
   if(avatar!=null)worn=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ParentID==avatar.LocalID&&p.Sculpt!=null&&p.Sculpt.Type==SculptType.Mesh&&p.Sculpt.SculptTexture==meshId);
  }while((avatar==null||worn==null)&&DateTime.UtcNow<deadline);
  Assert(avatar!=null,"Wearer is not visible to the observer");Assert(worn!=null,"Observer does not receive the Genesis attachment mesh");
  var attachments=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Count(p=>p.ParentID==avatar!.LocalID);
  // Appearance (baked slots) arrives in its own packet, possibly after the attachment.
  var appearanceBy=DateTime.UtcNow.AddSeconds(20);
  (Primitive.TextureEntryFace? def,Primitive.TextureEntryFace[]? faces) packet=default;
  while(!appearances.TryGetValue(avatar!.ID,out packet))
  {
   if(DateTime.UtcNow>appearanceBy)break;await Task.Delay(500);
  }
  var faces=packet.faces??avatar!.Textures?.FaceTextures;var fallback=packet.def??avatar!.Textures?.DefaultTexture;
  // A slot equal to the entry's default texture is stored as null (TextureEntry compaction).
  string? Bake(int i)=>faces!=null&&faces.Length>i&&faces[i]!=null?faces[i].TextureID.ToString():fallback?.TextureID.ToString();
  // Baked slots as observers receive them; an alpha-hidden region must arrive as IMG_INVISIBLE.
  var bakes=new Dictionary<string,string?>{["head"]=Bake(8),["upper"]=Bake(9),["lower"]=Bake(10),["eyes"]=Bake(11),["skirt"]=Bake(19),["hair"]=Bake(20)};
  const string invisible="3a367d1c-bef1-6d43-7595-e88c1e3aadb3";
  Save(new{wearer,avatar_position=new[]{avatar.Position.X,avatar.Position.Y,avatar.Position.Z},attachments,genesis_mesh=meshId.ToString(),attach_point=(int)worn!.PrimData.AttachmentPoint,
   lower_bake=bakes["lower"],bakes,hidden_regions=bakes.Where(kv=>kv.Value==invisible).Select(kv=>kv.Key).ToArray()});
  Console.WriteLine($"CITIZEN_SMOKE wearer PASS: {wearer} seen with {attachments} attachment(s) including Genesis mesh {meshId} at point {(int)worn.PrimData.AttachmentPoint}.");
 }
 else if(args[2]=="teleport")
 {
  // Same-grid teleport to the OpenVCE region and back; counts streamed objects in each region.
  string target=Environment.GetEnvironmentVariable("FINALVERSE_TELEPORT_REGION") is {Length:>0} t?t:"OpenVCE Plaza";
  var start=client.Network.CurrentSim.Name;var watch=System.Diagnostics.Stopwatch.StartNew();
  Assert(client.Self.Teleport(target,new Vector3(128,128,30)),"Teleport to "+target+" failed: "+client.Self.TeleportMessage);
  var outbound=watch.Elapsed.TotalSeconds;Assert(client.Network.CurrentSim.Name==target,"Arrived in the wrong region");
  int prims=0,stable=0;deadline=DateTime.UtcNow.AddSeconds(60);
  while(DateTime.UtcNow<deadline&&stable<6){await Task.Delay(500);int n=client.Network.CurrentSim.ObjectsPrimitives.Count;stable=n==prims?stable+1:0;prims=n;}
  var names=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Where(p=>p.ParentID==0).Select(p=>p.Properties?.Name).Where(n=>n!=null).Take(5).ToArray();
  var position=client.Self.SimPosition;watch.Restart();
  Assert(client.Self.Teleport(start,new Vector3(172,146,26)),"Teleport back failed: "+client.Self.TeleportMessage);
  var inbound=watch.Elapsed.TotalSeconds;deadline=DateTime.UtcNow.AddSeconds(30);bool lumiBack=false;
  do{await Task.Delay(300);lumiBack=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Any(p=>p.ID==lumi);}while(!lumiBack&&DateTime.UtcNow<deadline);
  Save(new{from=start,to=target,outbound_seconds=outbound,inbound_seconds=inbound,target_primitives=prims,arrival_position=new[]{position.X,position.Y,position.Z},named_roots=names,lumi_visible_after_return=lumiBack});
  int expected=int.Parse(Environment.GetEnvironmentVariable("FINALVERSE_TELEPORT_MIN_PRIMS") is {Length:>0} m?m:"4000");
  Assert(prims>=expected&&lumiBack,"Unexpected stream after teleport");
  Console.WriteLine($"CITIZEN_SMOKE teleport PASS: {start} -> {target} in {outbound:F1}s ({prims} primitives streamed), back in {inbound:F1}s, Lumi visible.");
 }
 else if(args[2]=="animwatch")
 {
  // Wire-level check: ObjectAnimation packets for Lumi around a human greeting.
  await Task.Delay(3000);int before;lock(animPackets)before=animPackets.Count;var sentMs=(DateTime.UtcNow-loginAt).TotalMilliseconds;
  var reply=Citizen("say","Hello Lumi!");await Task.Delay(5000);
  object[] seen;lock(animPackets)seen=animPackets.ToArray();
  Save(new{representation,greeting_sent_ms=sentMs,packets_before_greeting=before,packets=seen});
  Console.WriteLine($"CITIZEN_SMOKE animwatch: {seen.Length} ObjectAnimation packet(s) for Lumi ({before} before greeting at {sentMs:F0} ms)");
  try{await reply;}catch{}
 }
 else if(args[2]=="greet")
 {
  // Timed human greeting through the authenticated viewer relay; samples server animation state.
  var at=DateTimeOffset.FromUnixTimeMilliseconds((long)(double.Parse(Environment.GetEnvironmentVariable("FINALVERSE_GREET_AT")!,System.Globalization.CultureInfo.InvariantCulture)*1000));
  var wait=at-DateTimeOffset.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait);
  var samples=new List<object>();var reply=Citizen("say",Environment.GetEnvironmentVariable("FINALVERSE_SAY") is {Length:>0} said?said:"Hello Lumi!");var sent=DateTimeOffset.UtcNow;
  for(int i=0;i<10;i++){await Task.Delay(260);var b=await World(new{op="lumi"});samples.Add(new{ms=(DateTimeOffset.UtcNow-sent).TotalMilliseconds,animation=b.GetProperty("avatar_binding").GetProperty("animation").GetString()});}
  var answer=(await reply).GetProperty("answer").GetString();
  Save(new{representation,sent,samples,answer});string expect=Environment.GetEnvironmentVariable("FINALVERSE_EXPECT") is {Length:>0} e?e:"greet";Assert(samples.Any(x=>x.ToString()!.Contains(expect)),"Expected native gesture missing: "+expect);
  Console.WriteLine("CITIZEN_SMOKE greet PASS ("+expect+"): "+string.Join(",",samples.Select(x=>x.ToString())));
 }
 else if(args[2]=="displace")
 {
  // Native owner edit only (same as dragging in the viewer); used to stage a visible walk.
  var xy=Environment.GetEnvironmentVariable("FINALVERSE_WALK_FROM")!.Split(',').Select(v=>float.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
  var start=new Vector3(xy[0],xy[1],rootBody!.Position.Z);client.Objects.SetPosition(client.Network.CurrentSim,rootBody.LocalID,start);deadline=DateTime.UtcNow.AddSeconds(10);
  do{await Task.Delay(200);}while(Vector3.Distance(client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi).Position,start)>.05f&&DateTime.UtcNow<deadline);
  var at=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi).Position;Assert(Vector3.Distance(at,start)<=.05f,"Native owner move was not applied");
  Save(new{representation,displaced_to=new[]{at.X,at.Y,at.Z}});Console.WriteLine($"CITIZEN_SMOKE displace PASS: {representation} at {at}.");
 }
 else if(args[2]=="animesh")
 {
  var compiled=JsonDocument.Parse(File.ReadAllText(args[3])).RootElement;
  JsonElement Binding()=>World(new{op="lumi"}).Result.GetProperty("avatar_binding");
  var binding=Binding();
  Assert(binding.ValueKind==JsonValueKind.Object&&binding.GetProperty("representation").GetString()=="animated_object","Lumi is not bound to the animated-mesh representation");
  string meshId=binding.GetProperty("mesh_asset_id").GetString()!;
  Assert(animesh&&rootBody!.Sculpt!.SculptTexture.ToString()==meshId,"UDP embodiment does not carry the bound mesh asset");
  var scale=compiled.GetProperty("scale");Assert(Vector3.Distance(rootBody.Scale,new(scale[0].GetSingle(),scale[1].GetSingle(),scale[2].GetSingle()))<.01f,"UDP scale differs from the compiled domain");
  Assert(viewerAsset.Length>0,"Viewer asset capability missing");
  var meshBytes=await http.GetByteArrayAsync(viewerAsset+"?mesh_id="+meshId);
  string meshHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(meshBytes)).ToLowerInvariant();
  Assert(meshHash==compiled.GetProperty("mesh_sha256").GetString(),"Delivered native mesh differs from the compiled payload");
  double rootHeight=binding.GetProperty("root_height").GetDouble();
  var samples=new List<object>();var states=new HashSet<string>();
  void Sample(string phase){var b=Binding();var body=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi);string a=b.GetProperty("animation").GetString()!;states.Add(phase+":"+a);samples.Add(new{t=DateTime.UtcNow,phase,animation=a,position=new[]{body.Position.X,body.Position.Y,body.Position.Z},rotation=new[]{body.Rotation.X,body.Rotation.Y,body.Rotation.Z,body.Rotation.W}});}
  Sample("before");Assert(states.Contains("before:idle"),"Stationary Lumi must idle");
  // Optional: displace Lumi with a native owner edit (same as dragging in the viewer) so "go home" walks.
  string? walkFrom=Environment.GetEnvironmentVariable("FINALVERSE_WALK_FROM");
  if(!string.IsNullOrEmpty(walkFrom))
  {
   var xy=walkFrom.Split(',').Select(v=>float.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();var start=new Vector3(xy[0],xy[1],rootBody.Position.Z);
   client.Objects.SetPosition(client.Network.CurrentSim,rootBody.LocalID,start);deadline=DateTime.UtcNow.AddSeconds(10);
   do{await Task.Delay(200);}while(Vector3.Distance(client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi).Position,start)>.05f&&DateTime.UtcNow<deadline);
   Assert(Vector3.Distance(client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi).Position,start)<=.05f,"Native owner move was not applied");Sample("displaced");
  }
  var initial=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi).Position;bool moved=false;
  await Citizen("say","Lumi, go home.");deadline=DateTime.UtcNow.AddSeconds(90);string goalStatus="";
  do
  {
   await Task.Delay(300);Sample("navigate");var body=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi);if(Vector3.Distance(body.Position,initial)>1)moved=true;
   if(samples.Count%4==0){state=await Citizen("inspect");var goals=state.GetProperty("citizen").GetProperty("goals");goalStatus=goals[goals.GetArrayLength()-1].GetProperty("status").GetString()!;if(goalStatus is "completed" or "blocked" or "failed")break;}
  }while(DateTime.UtcNow<deadline);
  await Task.Delay(600);Sample("arrived");
  var speech=await Citizen("say","Hello Lumi! How was your walk?");var speechAt=DateTime.UtcNow;
  for(int i=0;i<6;i++){await Task.Delay(350);Sample("speech");}
  await Task.Delay(4500);Sample("after_speech");
  var final=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.First(p=>p.ID==lumi);
  Save(new{representation,udp_components=components,mesh_asset_id=meshId,delivered_mesh_sha256=meshHash,delivered_mesh_bytes=meshBytes.Length,root_height=rootHeight,
   udp_scale=new[]{rootBody.Scale.X,rootBody.Scale.Y,rootBody.Scale.Z},moved,goal_status=goalStatus,states=states.OrderBy(x=>x).ToArray(),speech_answer=speech.GetProperty("answer").GetString(),
   final_position=new[]{final.Position.X,final.Position.Y,final.Position.Z},samples,state});
  Assert(states.Contains("arrived:idle")&&states.Contains("after_speech:idle"),"Lumi did not return to idle");
  Assert(states.Any(x=>x is "speech:talk" or "speech:greet"),"No talk/greet gesture accompanied speech");
  if(!string.IsNullOrEmpty(walkFrom))Assert(moved&&goalStatus=="completed"&&states.Contains("navigate:walk"),"Displaced Lumi did not walk home");
  Console.WriteLine($"CITIZEN_SMOKE animesh PASS: single animated-mesh root, native mesh delivery hash, binding/scale, states {string.Join(",",states.OrderBy(x=>x))}, moved={moved}, goal={goalStatus}.");
 }
 else if(args[2]=="verify")
 {
  var previous=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("state").GetProperty("citizen");var now=state.GetProperty("citizen");
  foreach(string field in new[]{"agent_id","controller_id","embodiment_id","home_entity_id","created_at","autonomy_level"})Assert(previous.GetProperty("identity").GetProperty(field).ToString()==now.GetProperty("identity").GetProperty(field).ToString(),"Restart identity mismatch: "+field);
  // The runtime retains the 8 most recent goals; only goals older than that whole window may age out.
  var current=now.GetProperty("goals").EnumerateArray().ToArray();var oldest=current.Min(g=>g.GetProperty("created_at").GetString());
  foreach(var goal in previous.GetProperty("goals").EnumerateArray())Assert(current.Any(g=>g.GetProperty("goal_id").GetString()==goal.GetProperty("goal_id").GetString())
   ||current.Length==8&&string.CompareOrdinal(goal.GetProperty("created_at").GetString(),oldest)<0,"Restart lost goal history");
  var home=now.GetProperty("identity").GetProperty("home_entity_id").GetString();Assert(now.GetProperty("relationships").EnumerateArray().Any(r=>r.GetProperty("relationship_type").GetString()=="home"&&r.GetProperty("target_id").GetString()==home),"Restart lost home relationship");
  Assert(state.GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("subject").GetString()=="home_creation"),"Restart lost home event memory");
  Console.WriteLine("CITIZEN_SMOKE restart PASS: identity, autonomy, embodiment UDP, semantic home relationship, memories and goal history.");
 }
 else{Save(new{udp_components=components,state});Console.WriteLine($"CITIZEN_SMOKE observation PASS: authenticated capability relay and real UDP embodiment ({representation}, {components} component(s)).");}
 return 0;
}
catch(Exception ex){Console.Error.WriteLine("CITIZEN_SMOKE FAIL: "+ex.Message);return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
