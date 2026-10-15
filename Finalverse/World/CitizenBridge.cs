// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Drawing;
using PermissionMask = MutSea.Framework.PermissionMask;
using Nini.Config;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Framework.Servers;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.Framework.Scenes;

namespace Finalverse.World;
public sealed record CitizenNavigation(string action_id,string status,string home_id,double[] target,double[][] route,int waypoint,DateTimeOffset started_at,string? error=null);
public sealed partial class WorldModule
{
    bool citizensEnabled;
    string citizenOwner="",citizenToken="",runtimeUrl="",bridgePath="",citizenJournal="";
    CitizenNavigation? navigation;
    readonly Dictionary<string,JsonElement> citizenReceipts=new();
    readonly HashSet<string> citizenIntents=new(),gardenPlans=new(),gardenCommits=new();
    readonly HashSet<string> citizenPlacePlans=new(),citizenPlaceCommits=new();
    DateTimeOffset lease,lastTick=DateTimeOffset.UtcNow,lastBackup=DateTimeOffset.UtcNow;
    HttpClient runtimeHttp=new(new HttpClientHandler {UseProxy=false,AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(100)};
    void InitialiseCitizen(IConfigSource source)
    {
        var c=source.Configs["FinalverseCitizens"];
        citizensEnabled=c?.GetBoolean("Enabled",false)??false;
        if(!citizensEnabled)return;
        citizenOwner=c!.GetString("ControllerId","");runtimeUrl=c.GetString("RuntimeUrl","");
        string tokenFile=c.GetString("TokenFile","");
        if(!Guid.TryParse(citizenOwner,out _)||!Uri.TryCreate(runtimeUrl,UriKind.Absolute,out var url)||url.Scheme!="http"||url.Query!=""||url.UserInfo!="")throw new InvalidDataException("Invalid private citizen configuration.");
        citizenToken=File.ReadAllText(tokenFile).Trim();if(citizenToken.Length<32||citizenToken.Length>128)throw new InvalidDataException("Citizen service token must be 32–128 characters.");
        string runtimeSocket=c.GetString("RuntimeSocket","");
        if(runtimeSocket!=""){runtimeHttp.Dispose();runtimeHttp=PrivateServiceHttp.Client(runtimeSocket,TimeSpan.FromSeconds(100));}
        InitialiseRepresentation(c);
    }
    void StartCitizen()
    {
        if(!citizensEnabled)return;
        citizenJournal=Path.Combine(directory,RegionId+".citizens.worldline.jsonl");
        if(File.Exists(citizenJournal))foreach(var line in File.ReadLines(citizenJournal))
        {
            var record=JsonDocument.Parse(line).RootElement;
            string action=record.GetProperty("action_id").GetString()!;
            if(record.GetProperty("kind").GetString()!.StartsWith("representation"))representationRecords.Add(record.Clone());
            if(record.GetProperty("kind").GetString()=="intent")citizenIntents.Add(action);
            if(record.GetProperty("kind").GetString()=="result")
            {
                var result=record.GetProperty("result").Clone();citizenReceipts[action]=result;
                if(result.TryGetProperty("citizen_tool",out var tool)&&result.TryGetProperty("plan_id",out var placeId))
                {
                    if(tool.GetString()=="prepare_place")citizenPlacePlans.Add(placeId.GetString()!);
                    if(tool.GetString()=="commit_place"&&result.TryGetProperty("status",out var state)&&state.GetString()=="committed")citizenPlaceCommits.Add(placeId.GetString()!);
                }
                if(result.TryGetProperty("plan_id",out var plan)&&result.TryGetProperty("proposal",out var proposal)&&proposal.TryGetProperty("model",out var model)&&model.GetString()=="garden-v1")
                {gardenPlans.Add(plan.GetString()!);if(result.TryGetProperty("status",out var status)&&status.GetString()=="committed")gardenCommits.Add(plan.GetString()!);}
            }
        }
        // No motion replay on startup: world position is restored by inherited backup.
        bridgePath="/finalverse/citizen/"+RegionId;
        MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler(bridgePath,CitizenHttp));
        // Region modules load before persisted prims; resolve the embodiment once they exist.
        scene.EventManager.OnPrimsLoaded+=CitizenPrimsLoaded;
        scene.EventManager.OnFrame+=CitizenFrame;
    }
    bool representationReady;
    void CitizenPrimsLoaded(Scene s)
    {
        lock(gate)
        {
            try{StartRepresentation();}
            catch(Exception ex){representationRecovery=true;log.Error(JsonSerializer.Serialize(new{@event="citizen_representation_start_failed",exception_type=ex.GetType().Name,error=ex.Message}));}
            representationReady=true;
        }
    }
    void StopCitizen()
    {
        if(!citizensEnabled||string.IsNullOrEmpty(bridgePath))return;
        scene.EventManager.OnFrame-=CitizenFrame;scene.EventManager.OnPrimsLoaded-=CitizenPrimsLoaded;MainServer.Instance.RemoveSimpleStreamHandler(bridgePath);bridgePath="";
        lock(gate)if(navigation is {status:"moving"})FinishNavigation("blocked","Simulator stopped during navigation.");
    }
    void CitizenHttp(IOSHttpRequest request,IOSHttpResponse response)
    {
        response.ContentType="application/json";
        try
        {
            var supplied=Encoding.UTF8.GetBytes(request.Headers["Authorization"]??"");var expected=Encoding.UTF8.GetBytes("Bearer "+citizenToken);
            if(supplied.Length!=expected.Length||!CryptographicOperations.FixedTimeEquals(supplied,expected)||request.Headers["Origin"]!=null){response.StatusCode=403;response.RawBuffer=Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Not authorized.\"}");return;}
            if(request.HttpMethod!="POST"||request.ContentLength<1||request.ContentLength>32768)throw new InvalidDataException("Expected bounded JSON POST.");
            using var memory=new MemoryStream();byte[] bytes=new byte[4096];int n;
            while((n=request.InputStream.Read(bytes,0,bytes.Length))>0){memory.Write(bytes,0,n);if(memory.Length>32768)throw new InvalidDataException("Request too large.");}
            using var doc=JsonDocument.Parse(memory.ToArray(),new JsonDocumentOptions {MaxDepth=12});
            object result;lock(gate)result=CitizenTool(doc.RootElement);
            response.StatusCode=200;response.RawBuffer=JsonSerializer.SerializeToUtf8Bytes(new{ok=true,result});
        }
        catch(Exception ex) when(ex is InvalidDataException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {response.StatusCode=400;response.RawBuffer=JsonSerializer.SerializeToUtf8Bytes(new{ok=false,error=ex.Message});}
        catch(Exception){response.StatusCode=503;response.RawBuffer=Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Citizen bridge unavailable.\"}");}
    }
    object CitizenHuman(JsonElement data,string actor)
    {
        if(!citizensEnabled||actor!=citizenOwner)throw new InvalidDataException("This citizen is not controlled by your account.");
        string command=data.GetProperty("command").GetString()!;
        if(command is not ("inspect" or "say" or "pause" or "resume" or "cancel" or "approve" or "undo"))throw new InvalidDataException("Unknown citizen command.");
        string text=data.TryGetProperty("text",out var t)?t.GetString()??"":"";
        if(text.Length>2048)throw new InvalidDataException("Citizen message exceeds limit.");
        if(command=="say")lock(gate)HearGesture(text);
        using var message=new HttpRequestMessage(HttpMethod.Post,runtimeUrl.TrimEnd('/')+"/v1/citizens/"+Kernel.LumiId+"/command");
        message.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",citizenToken);
        message.Content=new StringContent(JsonSerializer.Serialize(new{controller_id=actor,command,text}),Encoding.UTF8,"application/json");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(100));
        using var reply=runtimeHttp.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,deadline.Token).GetAwaiter().GetResult();using var stream=reply.Content.ReadAsStream(deadline.Token);using var memory=new MemoryStream();byte[] buffer=new byte[4096];int count;
        while((count=stream.ReadAsync(buffer,deadline.Token).AsTask().GetAwaiter().GetResult())>0){memory.Write(buffer,0,count);if(memory.Length>65536)throw new InvalidDataException("Citizen reply exceeds limit.");}
        var root=JsonDocument.Parse(memory.ToArray()).RootElement;
        if(!reply.IsSuccessStatusCode)throw new InvalidDataException("Citizen runtime could not complete that request safely.");return root.Clone();
    }
    object CitizenTool(JsonElement data)
    {
        string op=data.GetProperty("op").GetString()!;
        if(op is not ("ensure" or "observe" or "navigate_home" or "stop" or "speak" or "prepare_garden" or "commit_garden" or "undo_garden" or "prepare_place" or "commit_place" or "undo_place"))throw new InvalidDataException("Tool capability denied.");
        lease=DateTimeOffset.UtcNow;
        if(!representationReady)throw new InvalidDataException("Citizen embodiment is still loading.");
        if(op=="observe")return ObserveCitizen();
        if(WorldRecoveryRequired&&op!="stop")throw new InvalidDataException("An interrupted world transaction requires administrator recovery.");
        if(representationRecovery&&op!="stop")throw new InvalidDataException("Citizen representation requires administrator recovery.");
        string action=data.GetProperty("action_id").GetString()!;if(!Guid.TryParse(action,out _))throw new InvalidDataException("Invalid action correlation.");
        if(citizenReceipts.TryGetValue(action,out var receipt))return receipt;
        if(citizenIntents.Contains(action))throw new InvalidDataException("Tool has an unresolved durable intent; do not replay uncertain actions.");
        if(op=="commit_garden"&&!gardenPlans.Contains(data.GetProperty("plan_id").GetString()!))throw new InvalidDataException("Only this citizen's approved garden plans are allowed.");
        if(op=="undo_garden"&&!gardenCommits.Contains(data.GetProperty("plan_id").GetString()!))throw new InvalidDataException("Only this citizen's committed garden can be undone.");
        if(op=="commit_place"&&!citizenPlacePlans.Contains(data.GetProperty("plan_id").GetString()!))throw new InvalidDataException("Only this citizen's prepared places can be approved.");
        if(op=="undo_place"&&!citizenPlaceCommits.Contains(data.GetProperty("plan_id").GetString()!))throw new InvalidDataException("Only this citizen's committed places can be undone.");
        RecordCitizen(action,"intent",new{op});citizenIntents.Add(action);object result;
        try
        {
            result=op switch {
                "ensure"=>EnsureCitizen(),"navigate_home"=>NavigateHome(action),"stop"=>StopMovement(),
                "speak"=>SpeakCitizen(data.GetProperty("text").GetString()!),
                "prepare_garden"=>PrepareGarden(),"commit_garden"=>kernel.Commit(data.GetProperty("plan_id").GetString()!,citizenOwner),
                "undo_garden"=>kernel.Undo(data.GetProperty("plan_id").GetString()!,citizenOwner),
                "prepare_place"=>PrepareCitizenPlace(data.GetProperty("plan")),
                "commit_place"=>ComposeCommit(data.GetProperty("plan_id").GetString()!,citizenOwner),
                "undo_place"=>ComposeUndo(data.GetProperty("plan_id").GetString()!,citizenOwner),
                _=>throw new InvalidDataException("Tool capability denied.")};
        }
        catch(Exception ex){log.Warn(JsonSerializer.Serialize(new{@event="citizen_tool_failed",op,exception_type=ex.GetType().Name,stack=ex.StackTrace}));result=new{status="blocked",error=ex is InvalidDataException?ex.Message:"Citizen execution failed."};}
        if(result is Prepared prepared)gardenPlans.Add(prepared.plan_id);
        if(result is Entry {status:"committed"} entry)gardenCommits.Add(entry.plan_id);
        if(op is "prepare_place" or "commit_place" or "undo_place")
        {
            var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(result))!.AsObject();
            node["citizen_tool"]=op;
            if(node["plan_id"] is {} id)
            {
                if(op=="prepare_place")citizenPlacePlans.Add(id.GetValue<string>());
                if(op=="commit_place"&&node["status"]?.GetValue<string>()=="committed")citizenPlaceCommits.Add(id.GetValue<string>());
            }
            result=JsonSerializer.SerializeToElement(node);
        }
        RecordCitizen(action,"result",result);citizenReceipts[action]=JsonSerializer.SerializeToElement(result);return result;
    }
    object PrepareCitizenPlace(JsonElement value)
    {
        var plan=value.Deserialize<Plan>(Kernel.Json)!;
        ContentCatalog.Require(plan!=null&&IsComposition(plan),"Lumi can propose only a bounded composed place.");
        var preview=JsonSerializer.SerializeToElement(ComposePrepare(plan!,citizenOwner));
        ContentCatalog.Require(preview.GetProperty("part_count").GetInt32() is >0 and <=192,"This creation exceeds Lumi's 192-part co-building budget. Try a smaller recipe.");
        return preview;
    }
    void RecordCitizen(string action,string kind,object result)
    {
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(new{event_id=Guid.NewGuid(),agent_id=Kernel.LumiId,action_id=action,kind,timestamp=DateTimeOffset.UtcNow,result});
        using var file=new FileStream(citizenJournal,FileMode.Append,FileAccess.Write,FileShare.Read);file.Write(data);file.WriteByte(10);file.Flush(true);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(citizenJournal,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }
    SceneObjectGroup Body()=>scene.GetSceneObjectGroup(new UUID(Kernel.LumiId))??throw new InvalidDataException("Lumi's embodiment is unavailable.");
    object EnsureCitizen()
    {
        var existing=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId));
        if(existing!=null){if(existing.RootPart.OwnerID.ToString()!=citizenOwner||existing.RootPart.Description!="Finalverse persistent citizen embodiment v1")throw new InvalidDataException("Embodiment ID conflicts with existing world state.");return new{status="present",position=V(existing.AbsolutePosition),embodiment_id=Kernel.LumiId};}
        // Deterministically find clear dry ground, never overwrite an existing object.
        double[]? spawn=null;
        for(int y=120;y<180&&spawn==null;y+=2)for(int x=120;x<190;x+=2){double[] p=[x,y,Height(x,y)+.9];if(CitizenNavigator.Clear(this,citizenOwner,Kernel.LumiId,p)){spawn=p;break;}}
        if(spawn==null)throw new InvalidDataException("No safe embodiment site.");
        var shape=PrimitiveBaseShape.CreateSphere();shape.Scale=new Vector3(.55f,.38f,.75f);
        var group=new SceneObjectGroup(new UUID(citizenOwner),ToV(spawn),shape);group.RootPart.UUID=new UUID(Kernel.LumiId);group.RootPart.Name="Lumi · AI Citizen";group.RootPart.Description="Finalverse persistent citizen embodiment v1";
        group.RootPart.Text="Lumi · AI Citizen";group.RootPart.Color=Color.White;
        void Appearance(SceneObjectPart p,Color4 color){var texture=new Primitive.TextureEntry(new UUID("5748decc-f629-461c-9a36-a35a221fe21f"));texture.DefaultTexture.RGBA=color;p.Shape.TextureEntry=texture.GetBytes();p.BaseMask=p.OwnerMask=(uint)PermissionMask.All;p.NextOwnerMask=(uint)(PermissionMask.Copy|PermissionMask.Modify|PermissionMask.Transfer);}
        Appearance(group.RootPart,new Color4(.45f,.25f,.75f,1));
        int index=1;
        void Part(Vector3 size,Vector3 offset,Color4 color){var s=PrimitiveBaseShape.CreateSphere();s.Scale=size;var p=new SceneObjectPart(new UUID(citizenOwner),s,ToV(spawn),Quaternion.Identity,offset);p.UUID=new UUID(Kernel.TraceIdForCitizen(index++));if(scene.GetSceneObjectPart(p.UUID)!=null)throw new InvalidDataException("Embodiment component identity conflicts with existing state.");p.Name="Lumi embodiment";group.AddPart(p);Appearance(p,color);}
        Part(new(.4f,.36f,.4f),new(0,0,.58f),new(.92f,.8f,.72f,1));
        Part(new(.44f,.38f,.23f),new(0,.015f,.73f),new(.2f,.13f,.35f,1));
        Part(new(.075f,.075f,.075f),new(-.1f,-.16f,.6f),new(.12f,.12f,.2f,1));
        Part(new(.075f,.075f,.075f),new(.1f,-.16f,.6f),new(.12f,.12f,.2f,1));
        Part(new(.18f,.22f,.5f),new(-.16f,0,-.5f),new(.2f,.15f,.3f,1));
        Part(new(.18f,.22f,.5f),new(.16f,0,-.5f),new(.2f,.15f,.3f,1));
        if(!scene.Permissions.CanRezObject(group.PrimCount,new UUID(citizenOwner),ToV(spawn))||!scene.AddNewSceneObject(group,true))throw new InvalidDataException("Embodiment creation denied.");
        group.HasGroupChanged=true;scene.ForceSceneObjectBackup(group);group.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
        // A configured representation is applied through the same journaled, snapshot-backed migration.
        if(avatar!=null){Migrate(group);Animate("idle",null);var body=Body();return new{status="present",position=V(body.AbsolutePosition),embodiment_id=Kernel.LumiId,representation};}
        return new{status="present",position=spawn,embodiment_id=Kernel.LumiId,representation="linked_primitive_citizen"};
    }
    (Entity floor,JsonElement semantic) Home()
    {
        var lumi=JsonSerializer.SerializeToElement(kernel.Lumi(citizenOwner));string? id=lumi.GetProperty("home_entity_id").GetString();
        var floor=id==null?null:Find(id,citizenOwner);
        if(floor==null||floor.owner_id!=citizenOwner)throw new InvalidDataException("Lumi's semantic home is missing or inaccessible.");
        return(floor,JsonSerializer.SerializeToElement(kernel.Semantic(id!,citizenOwner)));
    }
    double[] Door(Entity floor)
    {
        double yaw=2*Math.Atan2(floor.rotation[2],floor.rotation[3]);double dy=-floor.scale[1]/2-1.4;
        double x=floor.position[0]-dy*Math.Sin(yaw),y=floor.position[1]+dy*Math.Cos(yaw);
        return[x,y,Height(x,y)+citizenLift];
    }
    object ObserveCitizen()
    {
        var body=Body();if(body.RootPart.OwnerID.ToString()!=citizenOwner)throw new InvalidDataException("Embodiment ownership changed.");
        var pos=V(body.AbsolutePosition);object? home=null;
        try{var (floor,semantic)=Home();home=new{entity=floor,semantic,arrival=Door(floor)};}catch(InvalidDataException){}
        var entities=Entities(citizenOwner).Where(e=>e.id!=Kernel.LumiId).Select(e=>new{entity=e,distance=CitizenNavigator.Distance(e.position,pos)}).Where(e=>e.distance<=48).OrderBy(e=>e.distance).Take(32).ToArray();
        var people=scene.GetScenePresences().Where(p=>!p.IsChildAgent&&!p.IsNPC&&Vector3.Distance(p.AbsolutePosition,body.AbsolutePosition)<=24).Take(16).Select(p=>new{id=p.UUID.ToString(),name=p.Name,position=V(p.AbsolutePosition)}).ToArray();
        return new{agent_id=Kernel.LumiId,timestamp=DateTimeOffset.UtcNow,region_id=RegionId,region_name=RegionName,position=pos,radius=48,nearby_entities=entities,nearby_humans=people,home,navigation};
    }
    object NavigateHome(string action)
    {
        if(navigation is {status:"moving"})throw new InvalidDataException("Navigation is already active.");
        var body=Body();var (floor,_)=Home();var end=Door(floor);var route=CitizenNavigator.Route(this,citizenOwner,Kernel.LumiId,V(body.AbsolutePosition),end,citizenLift);
        navigation=new(action,"moving",floor.id,end,route,0,DateTimeOffset.UtcNow);
        if(representation=="animated_object")
        {
            // Face the route before walking: a native turn, then walk.
            var start=V(body.AbsolutePosition);var first=route[0];
            double diff=Math.IEEERemainder(Math.Atan2(first[1]-start[1],first[0]-start[0])+citizenYaw-Yaw(body.GroupRotation),2*Math.PI);
            if(Math.Abs(diff)>Math.PI/4){turnUntil=DateTimeOffset.UtcNow.AddSeconds(.8);Animate(diff>0?"turn_left":"turn_right",TimeSpan.FromSeconds(.8));}
            else Animate("walk",null);
        }
        return navigation;
    }
    object StopMovement(){if(navigation is {status:"moving"})FinishNavigation("cancelled","Human/runtime stopped motion.");return new{status="stopped"};}
    object SpeakCitizen(string text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>1024)throw new InvalidDataException("Speech exceeds limit.");var body=Body();
        scene.EventManager.TriggerOnChatFromWorld(this,new OSChatMessage {Channel=0,From="Lumi",Message=text,Position=body.AbsolutePosition,Type=ChatTypeEnum.Say,SenderUUID=new UUID(Kernel.LumiId),SenderObject=body,Scene=scene});
        SpeechGesture(text);return new{status="spoken"};
    }
    object PrepareGarden()
    {
        var (floor,_)=Home();var body=Body();if(CitizenNavigator.Distance(V(body.AbsolutePosition),Door(floor))>3)throw new InvalidDataException("Go home before proposing a garden.");
        // Creation remains assisted and uses the existing world kernel. Position is derived from home.
        double yaw=2*Math.Atan2(floor.rotation[2],floor.rotation[3]);var actions=new List<ActionSpec>();
        for(int i=0;i<3;i++){double x=-1.4+i*1.4,y=-floor.scale[1]/2-2.7;double px=floor.position[0]+x*Math.Cos(yaw)-y*Math.Sin(yaw),py=floor.position[1]+x*Math.Sin(yaw)+y*Math.Cos(yaw);double[] pos=[px,py,Height(px,py)+.22];
            double siteZ=representation=="animated_object"?Height(px,py)+citizenLift:pos[2]+.7;
            if(!CitizenNavigator.Clear(this,citizenOwner,Kernel.LumiId,[px,py,siteZ],citizenLift))throw new InvalidDataException("Garden site is obstructed.");
            actions.Add(new("create_primitive",null,JsonSerializer.SerializeToElement(new{position=pos,scale=new[]{.65,.65,.45},color=new[]{.2,.6,.32,1},shape="sphere",name="Lumi Garden · shrub "+(i+1)})));}
        return kernel.Prepare(new(1,"Lumi requests a small three-shrub garden near her home.","deterministic-recipe","garden-v1",actions.ToArray()),citizenOwner);
    }
    void FinishNavigation(string status,string? error)
    {
        if(navigation==null)return;navigation=navigation with {status=status,error=error};var body=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId));if(body!=null){body.HasGroupChanged=true;scene.ForceSceneObjectBackup(body);}
        RecordCitizen(navigation.action_id,"navigation_result",navigation);
        turnUntil=DateTimeOffset.MinValue;Animate("idle",null);
        if(status=="arrived")FaceOutward();
    }
    void CitizenFrame()
    {
        lock(gate)
        {
            var now=DateTimeOffset.UtcNow;if(now-lastTick<TimeSpan.FromMilliseconds(200))return;double dt=Math.Min(.25,(now-lastTick).TotalSeconds);lastTick=now;
            AnimationFrame(now);FaceFrame(now,dt);
            if(navigation is not {status:"moving"})return;
            try
            {
                if(WorldRecoveryRequired){FinishNavigation("blocked","World transaction requires administrator recovery.");return;}
                if(now-lease>TimeSpan.FromSeconds(5)){FinishNavigation("blocked","Runtime navigation lease expired.");return;}
                if(now-navigation.started_at>TimeSpan.FromSeconds(90*2/citizenSpeed)){FinishNavigation("blocked","Navigation time budget exhausted.");return;}
                var (home,_)=Home();if(home.id!=navigation.home_id||CitizenNavigator.Distance(Door(home),navigation.target)>.1){FinishNavigation("blocked","Home changed while navigating.");return;}
                var body=Body();var current=V(body.AbsolutePosition);var target=navigation.route[navigation.waypoint];double distance=CitizenNavigator.Distance(current,target);
                if(now<turnUntil)
                {
                    // Turn in place toward the first waypoint while the native turn animation plays.
                    double heading=Math.Atan2(target[1]-current[1],target[0]-current[0])+citizenYaw,yawNow=Yaw(body.GroupRotation);
                    double step=Math.Clamp(Math.IEEERemainder(heading-yawNow,2*Math.PI),-Math.PI*dt/.6,Math.PI*dt/.6);
                    body.UpdateGroupRotationR(YawRotation(yawNow+step));body.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);return;
                }
                if(distance<.12){int next=navigation.waypoint+1;if(next>=navigation.route.Length){FinishNavigation("arrived",null);return;}navigation=navigation with {waypoint=next};return;}
                double fraction=Math.Min(1,citizenSpeed*dt/distance);var position=current.Zip(target,(a,b)=>a+(b-a)*fraction).ToArray();
                if(!CitizenNavigator.Clear(this,citizenOwner,Kernel.LumiId,position,citizenLift)||!scene.Permissions.CanObjectEntry(body,false,ToV(position))||!scene.Permissions.CanEditObject(body.UUID,new UUID(citizenOwner))){FinishNavigation("blocked","Route or permission changed.");return;}
                double yaw=Math.Atan2(target[1]-current[1],target[0]-current[0])+citizenYaw;body.UpdateGroupRotationR(new Quaternion(0,0,(float)Math.Sin(yaw/2),(float)Math.Cos(yaw/2)));body.UpdateGroupPosition(ToV(position));body.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
                if(now-lastBackup>TimeSpan.FromSeconds(2)){body.HasGroupChanged=true;scene.ForceSceneObjectBackup(body);lastBackup=now;}
            }
            catch(Exception){FinishNavigation("blocked","World state unavailable during navigation.");}
        }
    }
}
