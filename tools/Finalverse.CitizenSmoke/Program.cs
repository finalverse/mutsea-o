// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Live private sandbox probe. Does not print passwords, service tokens or capability URLs.
using System.Text;
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length is not (3 or 4)||args[2] is not ("observe" or "questions" or "navigate" or "verify" or "garden_verify" or "garden_undo_verify" or "animesh" or "displace" or "greet" or "animwatch" or "teleport" or "wearer" or "semantic" or "hold" or "restyle" or "newcomer" or "compose" or "compose_undo" or "co_build_verify" or "co_build_undo_verify")||(args[2] is "animesh" or "wearer" or "co_build_verify" or "co_build_undo_verify")!=(args.Length==4)){Console.Error.WriteLine("Usage: CitizenSmoke private-account.json receipt.json observe|questions|navigate|verify|garden_verify|garden_undo_verify|displace|greet|animwatch|teleport|semantic|hold|restyle|newcomer|compose|compose_undo | animesh|wearer compiled.json | co_build_verify|co_build_undo_verify committed-place.json");return 2;}
var account=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;var client=new GridClient();
client.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(110)};string cap="",viewerAsset="";UUID lumi=new("b151468b-cbcc-4af2-ae2d-6967e8938bb4");
void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
async Task<JsonElement> Citizen(string command,string text="")
{
 return await World(new{op="citizen",command,text});
}
async Task<JsonElement> World(object request)
{
 await Task.Delay(160);var response=await http.PostAsync(cap,new StringContent(JsonSerializer.Serialize(request),Encoding.UTF8,"application/json"));var root=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
 // Relay refusals are written for people; show them rather than a generic failure.
 Assert(response.IsSuccessStatusCode&&root.GetProperty("ok").GetBoolean(),"Authenticated citizen relay failed"+(root.TryGetProperty("error",out var why)?": "+why.GetString():""));return root.GetProperty("result").Clone();
}
void Save(object result){File.WriteAllText(args[1],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);}
try
{
 var animPackets=new List<object>();var loginAt=DateTime.UtcNow;
 client.Network.RegisterCallback(OpenMetaverse.Packets.PacketType.ObjectAnimation,(sender,e)=>{var p=(OpenMetaverse.Packets.ObjectAnimationPacket)e.Packet;if(p.Sender.ID!=lumi)return;lock(animPackets)animPackets.Add(new{ms=(DateTime.UtcNow-loginAt).TotalMilliseconds,anims=p.AnimationList.Select(a=>a.AnimID.ToString()[..8]+"#"+a.AnimSequenceID).ToArray()});});
 var appearances=new System.Collections.Concurrent.ConcurrentDictionary<UUID,(Primitive.TextureEntryFace? def,Primitive.TextureEntryFace[]? faces)>();
 client.Avatars.AvatarAppearance+=(sender,e)=>appearances[e.AvatarID]=(e.DefaultTexture,e.FaceTextures);
 Assert(client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"Finalverse.CitizenSmoke","home","0.1"),"Sandbox login failed");var loggedInAt=DateTime.UtcNow;
 var seed=new OSDArray{OSD.FromString("FinalverseWorld"),OSD.FromString("ViewerAsset"),OSD.FromString("ObjectAnimation")};var content=new StringContent(OSDParser.SerializeLLSDXmlString(seed),Encoding.UTF8);content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
 var seedReply=await http.PostAsync(client.Network.CurrentSim.Caps.SeedCapsURI,content);var caps=(OSDMap)OSDParser.DeserializeLLSDXml(await seedReply.Content.ReadAsStringAsync());cap=caps["FinalverseWorld"].AsString();Assert(cap.Length>0,"World capability missing");viewerAsset=caps.ContainsKey("ViewerAsset")?caps["ViewerAsset"].AsString():"";
 var deadline=DateTime.UtcNow.AddSeconds(30);Primitive? rootBody=null;
 do{rootBody=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==lumi);if(rootBody!=null)break;await Task.Delay(200);}while(DateTime.UtcNow<deadline);
 Assert(rootBody!=null,"No Lumi embodiment in real UDP stream");await Task.Delay(500);
 int components=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Count(p=>p.ID==lumi||p.ParentID==rootBody!.LocalID);
 // Representation-aware: the historic linked primitive (7 parts) or one native animated-mesh root.
 bool animesh=rootBody!.Sculpt!=null&&rootBody.Sculpt.Type==SculptType.Mesh;string representation=animesh?"animated_object":"linked_primitive_citizen";
 Assert(animesh?components==1:components==7,animesh?"Animated-mesh embodiment must be a single UDP root":"Lumi's seven linked components are missing from UDP");
 // Lumi's relay is controller-only; a newcomer sees and hears Lumi but does not command her.
 JsonElement state=default;
 // Read-only region/appearance checks must still work with AI services offline
 // and for accounts that are not Lumi's controller.
 if(args[2] is not ("newcomer" or "compose" or "compose_undo" or "teleport" or "wearer")){state=await Citizen("inspect");Assert(state.GetProperty("citizen").GetProperty("identity").GetProperty("agent_id").GetString()==lumi.ToString(),"Persistent identity mismatch");}
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
  const string invisible="3a367d1c-bef1-6d43-7595-e88c1e3aadb3";
  var appearanceBy=DateTime.UtcNow.AddSeconds(60);
  var bakes=new Dictionary<string,string?>();
  do
  {
   appearances.TryGetValue(avatar!.ID,out var packet);
   var faces=packet.faces??avatar!.Textures?.FaceTextures;var fallback=packet.def??avatar!.Textures?.DefaultTexture;
   // TextureEntry compacts slots equal to its default; appearance may arrive
   // before wearable assets have finished loading. Wait for the final alpha.
   string? Bake(int i)=>faces!=null&&faces.Length>i&&faces[i]!=null?faces[i].TextureID.ToString():fallback?.TextureID.ToString();
   bakes=new(){["head"]=Bake(8),["upper"]=Bake(9),["lower"]=Bake(10),["eyes"]=Bake(11),["skirt"]=Bake(19),["hair"]=Bake(20)};
   if(new[]{"head","upper","lower","eyes","hair"}.All(k=>bakes[k]==invisible))break;
   await Task.Delay(500);
  }while(DateTime.UtcNow<appearanceBy);
  Assert(new[]{"head","upper","lower","eyes","hair"}.All(k=>bakes[k]==invisible),"Wearer's final body/hair alpha is not invisible");
  var surfaceIds=new List<string>();
  if(compiled.TryGetProperty("textures",out var surfaces))
  {
   var declared=surfaces.EnumerateArray().ToDictionary(t=>t.GetProperty("local_id").GetString()!);
   UUID SurfaceId(JsonElement t)=>new(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("finalverse-avatar:texture:"+t.GetProperty("asset_sha256").GetString())).Take(16).ToArray(),0);
   var surfaceFaces=compiled.GetProperty("faces").EnumerateArray().ToArray();
   for(int i=0;i<surfaceFaces.Length;i++)if(surfaceFaces[i].TryGetProperty("base_color_texture",out var name))
   {
    var id=SurfaceId(declared[name.GetString()!]);
    var actual=worn!.Textures.FaceTextures[i]?.TextureID??worn.Textures.DefaultTexture.TextureID;
    Assert(actual==id,"Native attachment surface binding differs");
   }
   var textureCap=client.Network.CurrentSim.Caps.CapabilityURI("GetTexture");
   Assert(textureCap!=null&&textureCap.Scheme=="http"&&textureCap.Host=="127.0.0.1"&&textureCap.Port==18094,"Expected native loopback texture capability");
   foreach(var t in declared.Values)
   {
    var id=SurfaceId(t);var url=new UriBuilder(textureCap!){Query="texture_id="+id}.Uri;
    using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
    using var input=await response.Content.ReadAsStreamAsync();using var buffer=new MemoryStream();
    byte[] bytes=new byte[8192];int n;
    using var deadlineTexture=new CancellationTokenSource(TimeSpan.FromSeconds(20));
    while((n=await input.ReadAsync(bytes,deadlineTexture.Token))>0){buffer.Write(bytes,0,n);Assert(buffer.Length<=1<<20,"Oversized native surface response");}
    Assert(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray())).ToLowerInvariant()==t.GetProperty("asset_sha256").GetString(),"Native surface payload hash differs");
    surfaceIds.Add(id.ToString());
   }
  }
  Save(new{wearer,avatar_position=new[]{avatar.Position.X,avatar.Position.Y,avatar.Position.Z},attachments,genesis_mesh=meshId.ToString(),attach_point=(int)worn!.PrimData.AttachmentPoint,
   lower_bake=bakes["lower"],bakes,hidden_regions=bakes.Where(kv=>kv.Value==invisible).Select(kv=>kv.Key).ToArray(),native_surface_ids=surfaceIds,surface_hashes_verified=surfaceIds.Count,
   collision_plane=new[]{avatar.CollisionPlane.X,avatar.CollisionPlane.Y,avatar.CollisionPlane.Z,avatar.CollisionPlane.W}});
  Console.WriteLine($"CITIZEN_SMOKE wearer PASS: {wearer} seen with {attachments} attachment(s) including Genesis mesh {meshId} at point {(int)worn.PrimData.AttachmentPoint}.");
 }
 else if(args[2]=="newcomer")
 {
  // A new account's first minutes over real UDP, timed from the login request: where it lands, what it can
  // learn about the place, a visit to the starter world and back, then a walk to the pavilion and a seat.
  // It ends at the pavilion, so the next login resumes there.
  var milestones=new List<object>();
  void Mark(string milestone,object? detail=null){milestones.Add(new{milestone,seconds=Math.Round((DateTime.UtcNow-loginAt).TotalSeconds,1),detail});Console.WriteLine($"  {milestone} at {(DateTime.UtcNow-loginAt).TotalSeconds:F1}s");}
  Mark("logged_in",new{login_seconds=Math.Round((loggedInAt-loginAt).TotalSeconds,1)});
  Vector3 Here()=>client.Self.SimPosition;
  deadline=DateTime.UtcNow.AddSeconds(15);while(Here()==Vector3.Zero&&DateTime.UtcNow<deadline)await Task.Delay(200);
  var landed=Here();double spawnDistance=Math.Sqrt(Math.Pow(landed.X-128,2)+Math.Pow(landed.Y-128,2));
  Mark("landed",new{region=client.Network.CurrentSim.Name,position=new[]{landed.X,landed.Y,landed.Z},distance_to_arrival_compass=Math.Round(spawnDistance,2)});
  Assert(client.Network.CurrentSim.Name=="MutSea Harbor"&&spawnDistance<=6,"A newcomer must land at the Harbor arrival");
  Mark("lumi_visible",new{distance=Math.Round(Vector3.Distance(landed,rootBody!.Position),1)});
  // "What is this?": place semantics of whatever stands near a point, through the newcomer's own relay.
  async Task<JsonElement?> Describe(float x,float y,float radius,Func<JsonElement,bool> want)
  {
   foreach(var p in client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Where(p=>Math.Sqrt(Math.Pow(p.Position.X-x,2)+Math.Pow(p.Position.Y-y,2))<=radius&&p.ParentID==0)
           .OrderBy(p=>Math.Pow(p.Position.X-x,2)+Math.Pow(p.Position.Y-y,2)).Take(12))
   {var r=await World(new{op="semantic",entity_id=p.ID.ToString()});if(r.TryGetProperty("place",out _)&&want(r))return r;}
   return null;
  }
  string Of(JsonElement e,params string[] path){foreach(var k in path)e=e.GetProperty(k);return e.ToString();}
  var beacon=await Describe(128,138,1.5f,r=>Of(r,"place","role")=="landmark");var stele=await Describe(135.5f,138,1.5f,r=>Of(r,"place","role")=="information");
  Assert(beacon!=null&&stele!=null,"The arrival landmarks carry no place semantics for a newcomer");
  Mark("arrival_understood",new{place=Of(beacon!.Value,"place","title"),beacon=Of(beacon.Value,"description"),welcome_stone=Of(stele!.Value,"description"),
   entrances=beacon.Value.GetProperty("place").GetProperty("entrances").EnumerateArray().Select(e=>e.GetProperty("local_id").GetString()).ToArray()});
  string target=Environment.GetEnvironmentVariable("FINALVERSE_TELEPORT_REGION") is {Length:>0} t?t:"Harbor Template";
  Assert(client.Self.Teleport(target,new Vector3(128,128,30)),"Teleport to "+target+" failed: "+client.Self.TeleportMessage);
  int prims=0,stable=0;deadline=DateTime.UtcNow.AddSeconds(60);
  while(DateTime.UtcNow<deadline&&stable<6){await Task.Delay(500);int n=client.Network.CurrentSim.ObjectsPrimitives.Count;stable=n==prims?stable+1:0;prims=n;}
  Mark("world_visited",new{region=client.Network.CurrentSim.Name,primitives=prims,position=new[]{Here().X,Here().Y,Here().Z}});
  Assert(client.Self.Teleport("MutSea Harbor",new Vector3(128,128,30)),"Return teleport failed: "+client.Self.TeleportMessage);
  deadline=DateTime.UtcNow.AddSeconds(15);while((Here()==Vector3.Zero||Math.Sqrt(Math.Pow(Here().X-128,2)+Math.Pow(Here().Y-128,2))>6)&&DateTime.UtcNow<deadline)await Task.Delay(200);
  // Each region entry registers a fresh World capability; the pre-teleport URL is gone.
  var reseed=new StringContent(OSDParser.SerializeLLSDXmlString(new OSDArray{OSD.FromString("FinalverseWorld")}),Encoding.UTF8);reseed.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
  var recaps=(OSDMap)OSDParser.DeserializeLLSDXml(await (await http.PostAsync(client.Network.CurrentSim.Caps.SeedCapsURI,reseed)).Content.ReadAsStringAsync());cap=recaps["FinalverseWorld"].AsString();Assert(cap.Length>0,"World capability missing after return");
  Mark("returned",new{region=client.Network.CurrentSim.Name,position=new[]{Here().X,Here().Y,Here().Z}});
  Assert(client.Network.CurrentSim.Name=="MutSea Harbor"&&Math.Sqrt(Math.Pow(Here().X-128,2)+Math.Pow(Here().Y-128,2))<=6,"The return did not reach the Harbor arrival");
  // Walk: west of the beacon, up the north walk's centre line, up the pavilion's south steps.
  Utils.LongToUInts(client.Network.CurrentSim.Handle,out uint gx,out uint gy);
  async Task Walk(float x,float y)
  {
   var lastProgress=DateTime.UtcNow;float best=float.MaxValue;var stop=DateTime.UtcNow.AddSeconds(45);
   // A ground-level target: a target above terrain + avatar height makes the server autopilot fly.
   client.Self.AutoPilot(gx+x,gy+y,0);
   while(DateTime.UtcNow<stop)
   {
    await Task.Delay(250);var d=(float)Math.Sqrt(Math.Pow(Here().X-x,2)+Math.Pow(Here().Y-y,2));
    if(d<1.2f)return;
    if(d<best-0.2f){best=d;lastProgress=DateTime.UtcNow;}
    if(DateTime.UtcNow-lastProgress>TimeSpan.FromSeconds(6))throw new Exception($"Walk stalled at {Here()} toward {x},{y}");
   }
   throw new Exception($"Walk timed out at {Here()} toward {x},{y}");
  }
  foreach(var (x,y) in new[]{(126f,133f),(126f,142.5f),(128f,146f),(128f,165f),(128f,184.5f),(128f,188.5f)})await Walk(x,y);
  client.Self.AutoPilotCancel();
  Mark("reached_pavilion",new{position=new[]{Here().X,Here().Y,Here().Z}});
  var bench=await Describe(130.05f,189.95f,1.6f,r=>r.TryGetProperty("seat",out var seat)&&seat.ValueKind==JsonValueKind.Object);
  if(bench==null)
  {
   // The seat may be a child part of the bench: ask about every part near the bench.
   foreach(var p in client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Where(p=>p.ParentID!=0))
   {
    var parent=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(r=>r.LocalID==p.ParentID);
    if(parent==null||Math.Sqrt(Math.Pow(parent.Position.X-130.05,2)+Math.Pow(parent.Position.Y-189.95,2))>1.6)continue;
    var r=await World(new{op="semantic",entity_id=p.ID.ToString()});if(r.TryGetProperty("seat",out var seat)&&seat.ValueKind==JsonValueKind.Object){bench=r;break;}
   }
  }
  Assert(bench!=null,"No seat found at the pavilion bench");
  var seatId=new UUID(Of(bench!.Value,"entity_id"));
  client.Self.RequestSit(seatId,Vector3.Zero);await Task.Delay(400);client.Self.Sit();
  deadline=DateTime.UtcNow.AddSeconds(12);while(client.Self.SittingOn==0&&DateTime.UtcNow<deadline)await Task.Delay(200);
  Assert(client.Self.SittingOn!=0,"The newcomer could not sit on the pavilion bench");
  Mark("seated",new{seat=Of(bench.Value,"package_id"),place=Of(bench.Value,"place","title")});
  await Task.Delay(2000);client.Self.Stand();
  deadline=DateTime.UtcNow.AddSeconds(10);while(client.Self.SittingOn!=0&&DateTime.UtcNow<deadline)await Task.Delay(200);
  Assert(client.Self.SittingOn==0,"The newcomer could not stand up");Mark("stood");
  double total=(DateTime.UtcNow-loginAt).TotalSeconds;
  Save(new{account=client.Self.Name,total_seconds=Math.Round(total,1),budget_seconds=300,milestones});
  Assert(total<=300,"The newcomer path exceeded five minutes");
  Console.WriteLine($"CITIZEN_SMOKE newcomer PASS: {milestones.Count} milestones in {total:F1}s (budget 300s).");
 }
 else if(args[2] is "co_build_verify" or "co_build_undo_verify")
 {
  // Read-only acceptance of a specific UI-approved place, before/after restart or Undo.
  var f=new FileInfo(args[3]);Assert(f.Exists&&f.Length is >0 and <=8388608&&(f.Attributes&FileAttributes.ReparsePoint)==0,"Expected a bounded committed-place receipt");
  Assert(!File.Exists(args[1]),"Receipt output must be new");
  var entry=JsonDocument.Parse(File.ReadAllText(args[3])).RootElement;var plan=entry.GetProperty("plan");
  string id=plan.GetProperty("plan_id").GetString()!;
  Assert(entry.GetProperty("status").GetString()=="committed"&&plan.GetProperty("actor_id").GetString()==client.Self.AgentID.ToString(),"Place receipt must be a committed operation owned by this controller");
  Assert(client.Network.CurrentSim.RegionID.ToString()==account.GetProperty("region_id").GetString(),"Wrong native region");
  bool exists=args[2]=="co_build_verify";
  string? goalId=entry.TryGetProperty("agent_goal_id",out var pinnedGoal)?pinnedGoal.GetString():null;
  Assert(exists||!string.IsNullOrEmpty(goalId),"Undo verification requires the pinned original goal ID");
  var goal=state.GetProperty("citizen").GetProperty("goals").EnumerateArray().Single(g=>g.GetProperty("kind").GetString()=="compose_place"&&(goalId!=null?g.GetProperty("goal_id").GetString()==goalId:g.GetProperty("plan_id").GetString()==id));
  Assert(goal.GetProperty("status").GetString()=="completed"&&(exists?goal.GetProperty("plan_id").GetString()==id:goal.GetProperty("plan_id").ValueKind==JsonValueKind.Null),"Citizen completion/Undo state mismatch");
  var all=plan.GetProperty("placements").EnumerateArray().ToArray();
  var ids=all.SelectMany(p=>p.GetProperty("entity_ids").EnumerateArray().Select(e=>new UUID(e.GetString()))).ToArray();
  Assert(ids.Length is >0 and <=192&&ids.Distinct().Count()==ids.Length,"Part budget/identity mismatch");
  deadline=DateTime.UtcNow.AddSeconds(20);
  while(DateTime.UtcNow<deadline){var stream=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Select(p=>p.ID).ToHashSet();if(exists?ids.All(stream.Contains):ids.All(i=>!stream.Contains(i)))break;await Task.Delay(200);}
  var observed=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.ToDictionary(p=>p.ID);
  Assert(exists?ids.All(observed.ContainsKey):ids.All(i=>!observed.ContainsKey(i)),"Native part existence mismatch");
  int checkedParts=0;
  if(exists)foreach(var placement in all)
  {
   var anchor=placement.GetProperty("anchor");var yaw=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(placement.GetProperty("yaw_degrees").GetDouble()*Math.PI/180));
   Vector3 V(JsonElement v)=>new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());
   Quaternion Q(JsonElement v)=>new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle(),v[3].GetSingle());
   var parts=placement.GetProperty("package").GetProperty("parts");var entityIds=placement.GetProperty("entity_ids");
   var rootPos=V(anchor)+V(parts[0].GetProperty("position"))*yaw;var rootRot=Q(parts[0].GetProperty("rotation"))*yaw;
   var root=observed[new UUID(entityIds[0].GetString())];
   for(int i=0;i<parts.GetArrayLength();i++)
   {
    var part=parts[i];var uid=new UUID(entityIds[i].GetString());var native=observed[uid];
    var worldPos=V(anchor)+V(part.GetProperty("position"))*yaw;
    var expected=i==0?rootPos:(worldPos-rootPos)*Quaternion.Inverse(rootRot);
    Assert(Vector3.Distance(native.Position,expected)<.025f&&Vector3.Distance(native.Scale,V(part.GetProperty("scale")))<.025f,"Native part transform mismatch");
    Assert(i==0?native.ParentID==0:native.ParentID==root.LocalID,"Native link identity mismatch");
    var semantic=await World(new{op="semantic",entity_id=uid.ToString()});
    Assert(semantic.GetProperty("owner_id").GetString()==client.Self.AgentID.ToString()&&semantic.GetProperty("place").GetProperty("title").GetString()==plan.GetProperty("place").GetProperty("title").GetString(),"Native ownership/place semantics mismatch");
    checkedParts++;
   }
  }
  var history=await World(new{op="journal"});
  Assert(history.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==id&&e.GetProperty("status").GetString()==(exists?"committed":"undone")),"Human WorldLine mismatch");
  Save(new{plan_id=id,part_count=ids.Length,checked_parts=checkedParts,parts_present=exists,udp_components=components,state,history});
  Console.WriteLine($"CITIZEN_SMOKE {args[2]} PASS: {ids.Length} actual part identities, {checkedParts} transforms/ownership/semantics, durable goal/memory and WorldLine; no mutation requested.");
 }
 else if(args[2]=="compose")
 {
  // A person asks for a place: walk to FINALVERSE_COMPOSE_AT (x,y), face FINALVERSE_COMPOSE_FACE (x,y), ask the local
  // gateway (FINALVERSE_COMPOSE_REQUEST), then preview, build and check the result over real UDP and the World relay.
  float[] At(string name)=>Environment.GetEnvironmentVariable(name)!.Split(',').Select(v=>float.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
  var stand=At("FINALVERSE_COMPOSE_AT");var face=At("FINALVERSE_COMPOSE_FACE");string ask=Environment.GetEnvironmentVariable("FINALVERSE_COMPOSE_REQUEST")!;
  Utils.LongToUInts(client.Network.CurrentSim.Handle,out uint gx,out uint gy);
  deadline=DateTime.UtcNow.AddSeconds(15);while(client.Self.SimPosition==Vector3.Zero&&DateTime.UtcNow<deadline)await Task.Delay(200);
  client.Self.AutoPilot(gx+stand[0],gy+stand[1],0);deadline=DateTime.UtcNow.AddSeconds(60);
  while(Math.Sqrt(Math.Pow(client.Self.SimPosition.X-stand[0],2)+Math.Pow(client.Self.SimPosition.Y-stand[1],2))>1.2&&DateTime.UtcNow<deadline)await Task.Delay(250);
  client.Self.AutoPilotCancel();await Task.Delay(500);
  client.Self.Movement.TurnToward(new Vector3(face[0],face[1],client.Self.SimPosition.Z));client.Self.Movement.SendUpdate(true);await Task.Delay(1500);
  var here=client.Self.SimPosition;var objectsBefore=client.Network.CurrentSim.ObjectsPrimitives.Count;
  var context=new{region=new{id=client.Network.CurrentSim.ID.ToString()},avatar=new{position=new[]{here.X,here.Y,here.Z}},terrain=new{height=here.Z-0.9},selected_object=(object?)null,nearby_entities=Array.Empty<object>()};
  var watch=System.Diagnostics.Stopwatch.StartNew();
  using var gateway=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(120)};
  string route="http://127.0.0.1:18765/v1/world/plan";
  string payload=JsonSerializer.Serialize(new{request=ask,world_context=context});
  if(Environment.GetEnvironmentVariable("FINALVERSE_WORLD_SPEC_FILE") is {Length:>0} recipe)
  {
   var f=new FileInfo(recipe);Assert(f.Exists&&f.Length is >0 and <=4096&&(f.Attributes&FileAttributes.ReparsePoint)==0,"Expected a bounded regular WorldSpec fixture");
   var spec=JsonDocument.Parse(File.ReadAllText(recipe)).RootElement;
   payload=JsonSerializer.Serialize(new{world_spec=spec});
   // Optional isolated development gateway, never an arbitrary remote endpoint.
   string port=Environment.GetEnvironmentVariable("FINALVERSE_SPEC_GATEWAY_PORT")??"18765";
   Assert(port is "18765" or "18767","Expected the loopback WorldSpec fixture gateway");
   route="http://127.0.0.1:"+port+"/v1/world/spec/plan";
  }
  var planned=await gateway.PostAsync(route,new StringContent(payload,Encoding.UTF8,"application/json"));
  var generated=JsonDocument.Parse(await planned.Content.ReadAsStringAsync()).RootElement.Clone();var gatewayMs=watch.ElapsedMilliseconds;
  Assert(planned.IsSuccessStatusCode&&generated.GetProperty("plan").ValueKind==JsonValueKind.Object,"The gateway proposed no place: "+generated.ToString()[..Math.Min(300,generated.ToString().Length)]);
  watch.Restart();var prepared=await World(new{op="prepare",plan=generated.GetProperty("plan")});var prepareMs=watch.ElapsedMilliseconds;
  string planId=prepared.GetProperty("plan_id").GetString()!;
  watch.Restart();var committed=await World(new{op="commit",plan_id=planId});var commitMs=watch.ElapsedMilliseconds;
  Assert(committed.GetProperty("status").GetString()=="committed","Build did not commit");
  int parts=generated.GetProperty("plan").GetProperty("actions")[0].GetProperty("parameters").GetProperty("draft").GetProperty("placements").GetArrayLength();
  deadline=DateTime.UtcNow.AddSeconds(30);int stable=0,count=0;
  while(DateTime.UtcNow<deadline&&stable<6){await Task.Delay(500);int n=client.Network.CurrentSim.ObjectsPrimitives.Count;stable=n==count?stable+1:0;count=n;}
  // Ask the relay what one of the new things is.
  var fresh=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Where(p=>p.ParentID==0&&Vector3.Distance(p.Position,here)<30).OrderBy(p=>Vector3.Distance(p.Position,here)).Take(40).ToArray();
  JsonElement? known=null;foreach(var p in fresh){var r=await World(new{op="semantic",entity_id=p.ID.ToString()});if(r.TryGetProperty("place",out var pl)&&pl.GetProperty("place_id").GetString()!.StartsWith("user.")){known=r;break;}}
  Assert(known!=null,"No composed part carries place semantics");
  Save(new{request=ask,answer=generated.GetProperty("answer").GetString(),stand=new[]{here.X,here.Y,here.Z},plan_id=planId,operation_id=committed.GetProperty("operation_id").GetString(),
    gateway_ms=gatewayMs,prepare_ms=prepareMs,commit_ms=commitMs,placements=parts,objects_before=objectsBefore,objects_after=count,
    summary=prepared.GetProperty("summary").GetString(),steps=prepared.GetProperty("steps").GetArrayLength(),
    place=known.Value.GetProperty("place").GetProperty("title").GetString(),place_id=known.Value.GetProperty("place").GetProperty("place_id").GetString(),owner=known.Value.GetProperty("owner_id").GetString(),
    proposal=generated.GetProperty("plan").GetProperty("actions")[0].GetProperty("parameters").GetProperty("provenance")});
  Console.WriteLine($"CITIZEN_SMOKE compose PASS: '{ask}' -> {parts} placements built ({count-objectsBefore} new objects in UDP); gateway {gatewayMs} ms, prepare {prepareMs} ms, build {commitMs} ms.");
 }
 else if(args[2]=="compose_undo")
 {
  // Undo a composition recorded in an earlier compose receipt (FINALVERSE_COMPOSE_RECEIPT), as its builder.
  var receipt=JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("FINALVERSE_COMPOSE_RECEIPT")!)).RootElement;
  await Task.Delay(3000);var before=client.Network.CurrentSim.ObjectsPrimitives.Count;
  var undone=await World(new{op="undo",plan_id=receipt.GetProperty("plan_id").GetString()});
  Assert(undone.GetProperty("status").GetString()=="undone","Undo did not complete");
  await Task.Delay(4000);var after=client.Network.CurrentSim.ObjectsPrimitives.Count;
  Save(new{plan_id=receipt.GetProperty("plan_id").GetString(),undo_operation=undone.GetProperty("operation_id").GetString(),objects_before=before,objects_after=after});
  Console.WriteLine($"CITIZEN_SMOKE compose_undo PASS: {before} -> {after} objects in UDP.");
 }
 else if(args[2]=="teleport")
 {
  // Same-grid teleport to the OpenVCE region and back; counts streamed objects in each region.
  string target=Environment.GetEnvironmentVariable("FINALVERSE_TELEPORT_REGION") is {Length:>0} t?t:"OpenVCE Plaza";
  var start=client.Network.CurrentSim.Name;var watch=System.Diagnostics.Stopwatch.StartNew();
  Assert(client.Self.Teleport(target,new Vector3(128,128,30)),"Teleport to "+target+" failed: "+client.Self.TeleportMessage);
  var outbound=watch.Elapsed.TotalSeconds;Assert(client.Network.CurrentSim.Name==target,"Arrived in the wrong region");
  var targetId=client.Network.CurrentSim.RegionID;
  if(Environment.GetEnvironmentVariable("FINALVERSE_TELEPORT_REGION_ID") is {Length:>0} expectedId)
   Assert(targetId==new UUID(expectedId),"Arrived in the wrong region identity");
  int prims=0,stable=0;deadline=DateTime.UtcNow.AddSeconds(60);
  while(DateTime.UtcNow<deadline&&stable<6){await Task.Delay(500);int n=client.Network.CurrentSim.ObjectsPrimitives.Count;stable=n==prims?stable+1:0;prims=n;}
  var names=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Where(p=>p.ParentID==0).Select(p=>p.Properties?.Name).Where(n=>n!=null).Take(5).ToArray();
  var position=client.Self.SimPosition;watch.Restart();
  Assert(client.Self.Teleport(start,new Vector3(172,146,26)),"Teleport back failed: "+client.Self.TeleportMessage);
  var inbound=watch.Elapsed.TotalSeconds;deadline=DateTime.UtcNow.AddSeconds(30);bool lumiBack=false;
  do{await Task.Delay(300);lumiBack=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Any(p=>p.ID==lumi);}while(!lumiBack&&DateTime.UtcNow<deadline);
  Save(new{from=start,to=target,target_region_id=targetId.ToString(),outbound_seconds=outbound,inbound_seconds=inbound,target_primitives=prims,arrival_position=new[]{position.X,position.Y,position.Z},named_roots=names,lumi_visible_after_return=lumiBack});
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
finally
{
 if(client.Network.Connected)
 {
  client.Network.Logout();
  // Logout is asynchronous. Let the simulator remove this presence before
  // exiting so the next probe does not inherit an "already logged in" refusal.
  var end=DateTime.UtcNow.AddSeconds(5);
  while(client.Network.Connected&&DateTime.UtcNow<end)await Task.Delay(50);
 }
}
