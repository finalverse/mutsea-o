// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using Caps = MutSea.Framework.Capabilities.Caps;
using Permissions = MutSea.Framework.PermissionMask;

[assembly: Addin("Finalverse.World", MutSea.VersionInfo.VersionNumber)]
[assembly: AddinDependency("MutSea.Region.Framework", MutSea.VersionInfo.VersionNumber)]
namespace Finalverse.World;

[Extension(Path="/MutSea/RegionModules",NodeName="RegionModule",Id="FinalverseWorldModule")]
public sealed partial class WorldModule : INonSharedRegionModule, IWorld
{
    static readonly ILog log=LogManager.GetLogger(typeof(WorldModule));
    readonly object gate = new();
    readonly Dictionary<UUID,Guid> sessions = new();
    readonly Dictionary<UUID,DateTimeOffset> lastRequest = new();
    Scene scene = null!;
    Kernel kernel = null!;
    bool WorldRecoveryRequired => kernel?.RecoveryRequired==true||content?.RecoveryRequired==true;
    bool enabled;
    string directory = "";
    public string Name => "FinalverseWorldModule";
    public Type ReplaceableInterface => null!;
    public string RegionId => scene.RegionInfo.RegionID.ToString();
    public string RegionName => scene.RegionInfo.RegionName;
    public double WaterHeight => scene.RegionInfo.RegionSettings.WaterHeight;
    public double SizeX => scene.RegionInfo.RegionSizeX;
    public double SizeY => scene.RegionInfo.RegionSizeY;
    public void Initialise(IConfigSource config)
    {
        var c=config.Configs["FinalverseWorld"];
        enabled=c?.GetBoolean("Enabled",false)??false;
        directory=c?.GetString("DataDirectory","/data/finalverse")??"/data/finalverse";
        contentDirectory=c?.GetString("ContentLibraryDirectory","")??"";
        InitialiseCitizen(config);
        InitialiseGenesis(config);
        InitialisePlaces(config);
        InitialiseWorlds(config);
    }
    public void AddRegion(Scene s)
    {
        scene=s;
        if(!enabled)return;
        kernel=new Kernel(this,directory);
        var catalog=new ContentCatalog(contentDirectory,ContentMaterialAsset.Matches);
        content=new ContentKernel(this,catalog,directory,RegionId);
        StartPlaces(catalog);
        StartWorlds(catalog);
        kernel.ContentSemantics=(id,actor)=>content.Semantic(id,actor)??places?.Semantic(id,actor)??worldPlaces?.Semantic(id,actor);
        kernel.AvatarBinding=AvatarBinding;
        s.EventManager.OnRegisterCaps+=RegisterCaps;
        s.EventManager.OnPrimsLoaded+=GenesisPrimsLoaded;
        s.EventManager.OnPrimsLoaded+=PlacesPrimsLoaded;
        s.EventManager.OnPrimsLoaded+=WorldsPrimsLoaded;
    }
    public void RegionLoaded(Scene s) { if(enabled) StartCitizen(); }
    public void RemoveRegion(Scene s) { StopCitizen(); if(enabled){s.EventManager.OnRegisterCaps-=RegisterCaps;s.EventManager.OnPrimsLoaded-=GenesisPrimsLoaded;s.EventManager.OnPrimsLoaded-=PlacesPrimsLoaded;s.EventManager.OnPrimsLoaded-=WorldsPrimsLoaded;} lock(gate)sessions.Clear(); }
    public void Close() { StopCitizen(); lock(gate)sessions.Clear(); }
    void RegisterCaps(UUID agent,Caps caps)
    {
        Guid session=Guid.NewGuid(); lock(gate) { kernel.InvalidateSession(agent.ToString()); content?.Invalidate(agent.ToString()); sessions[agent]=session; }
        caps.RegisterSimpleHandler("FinalverseWorld",new SimpleStreamHandler("/"+UUID.Random(),(request,response)=>
        {
            response.ContentType="application/json";
            var timer=Stopwatch.StartNew(); string operation="invalid"; object? output=null;
            try
            {
                if(request.HttpMethod!="POST"||request.ContentLength<1||request.ContentLength>32768)throw new InvalidDataException("Expected a bounded JSON POST.");
                // Hard byte bound also covers chunked/truncated bodies.
                using var memory=new MemoryStream();
                byte[] buffer=new byte[4096]; int n;
                while((n=request.InputStream.Read(buffer,0,buffer.Length))>0) { memory.Write(buffer,0,n);if(memory.Length>32768)throw new InvalidDataException("Request too large."); }
                using var doc=JsonDocument.Parse(memory.ToArray(),new JsonDocumentOptions {MaxDepth=20});
                var data=doc.RootElement;
                if(data.GetProperty("op").GetString()=="citizen")
                {
                    lock(gate)
                    {
                        if(!sessions.TryGetValue(agent,out var active)||active!=session||scene.GetScenePresence(agent) is not {IsChildAgent:false})throw new InvalidDataException("Your region session is no longer active.");
                        if(lastRequest.TryGetValue(agent,out var recent)&&DateTimeOffset.UtcNow-recent<TimeSpan.FromMilliseconds(100))throw new InvalidDataException("Please wait briefly before the next request.");
                        lastRequest[agent]=DateTimeOffset.UtcNow;
                    }
                    operation="citizen";output=CitizenHuman(data,agent.ToString());
                    response.RawBuffer=JsonSerializer.SerializeToUtf8Bytes(new{ok=true,result=output});response.StatusCode=200;return;
                }
                lock(gate)
                {
                    if(!sessions.TryGetValue(agent,out var current)||current!=session||scene.GetScenePresence(agent) is not {IsChildAgent:false})throw new InvalidDataException("Your region session is no longer active.");
                    if(lastRequest.TryGetValue(agent,out var last)&&DateTimeOffset.UtcNow-last<TimeSpan.FromMilliseconds(100))throw new InvalidDataException("Please wait briefly before the next request.");
                    lastRequest[agent]=DateTimeOffset.UtcNow;
                    string actor=agent.ToString(); string op=data.GetProperty("op").GetString()!;
                    if(WorldRecoveryRequired && op is "prepare" or "commit" or "undo" or "content_preview" or "content_commit" or "content_undo")throw new InvalidDataException("An interrupted world transaction requires administrator recovery.");
                    if(op is "inspect" or "prepare" or "commit" or "undo" or "journal" or "lumi" or "semantic")operation=op;
                    if(op.StartsWith("content_",StringComparison.Ordinal))operation=op;
                    object result=op switch {
                        "content_preview"=>content!.Preview(data.GetProperty("package_id").GetString()!,data.GetProperty("version").GetString()!,data.GetProperty("anchor").Deserialize<double[]>()!,actor),
                        "content_commit"=>content!.Commit(data.GetProperty("plan_id").GetString()!,actor),
                        "content_undo"=>content!.Undo(data.GetProperty("plan_id").GetString()!,actor),
                        "content_history"=>content!.History(actor),
                        "inspect"=>kernel.Inspect(actor,data.TryGetProperty("selected_id",out var selected)&&selected.ValueKind==JsonValueKind.String?selected.GetString():null),
                        "prepare"=>kernel.Prepare(data.GetProperty("plan").Deserialize<Plan>(Kernel.Json)!,actor),
                        "commit"=>kernel.Commit(data.GetProperty("plan_id").GetString()!,actor),
                        "undo"=>kernel.Undo(data.GetProperty("plan_id").GetString()!,actor),
                        "journal"=>kernel.History(actor), "lumi"=>kernel.Lumi(actor),
                        "semantic"=>kernel.Semantic(data.GetProperty("entity_id").GetString()!,actor)??new { found=false },
                        _=>throw new InvalidDataException("Unknown World API operation.")};
                    output=result;
                    response.RawBuffer=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {ok=true,result},Kernel.Json));
                    response.StatusCode=200;
                }
            }
            catch(Exception ex) when(ex is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            { response.StatusCode=400;response.RawBuffer=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {ok=false,error=ex.Message})); }
            catch(Exception)
            { response.StatusCode=500;response.RawBuffer=Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"World service failed. Inspect WorldLine before retrying.\"}"); }
            finally
            {
                // Correlate policy/execution without logging bearer URLs, prompts or scene labels.
                log.Info(JsonSerializer.Serialize(new { @event="world_request",region_id=RegionId,actor_id=agent.ToString(),op=operation,
                    http_status=response.StatusCode,latency_ms=timer.ElapsedMilliseconds,
                    plan_id=output is Prepared p?p.plan_id:output is Entry e?e.plan_id:null,
                    operation_id=(output as Entry)?.operation_id,outcome=(output as Entry)?.status,
                    step_count=output is Prepared prepared?prepared.steps.Length:output is Entry entry?entry.steps.Length:0 }));
            }
        }));
    }
    public double[] Avatar(string actor)
    {
        var p=scene.GetScenePresence(new UUID(actor));
        if(p==null||p.IsChildAgent)throw new InvalidDataException("No active avatar in this region.");
        return V(p.AbsolutePosition);
    }
    public double[] Orientation(string actor)
    {var q=scene.GetScenePresence(new UUID(actor)).Rotation;return [q.X,q.Y,q.Z,q.W];}
    public IEnumerable<Entity> Entities(string actor)=>scene.GetSceneObjectGroups().Where(g=>!g.IsDeleted&&!g.IsAttachment).Select(g=>Snapshot(g,actor));
    public Entity? Find(string id,string actor)
    { var g=scene.GetSceneObjectGroup(new UUID(id));return g==null||g.IsDeleted?null:Snapshot(g,actor); }
    Entity Snapshot(SceneObjectGroup g,string actor)
    {
        var p=g.RootPart;var q=p.RotationOffset;var te=new Primitive.TextureEntry(p.Shape.TextureEntry,0,p.Shape.TextureEntry.Length);var color=te.DefaultTexture.RGBA;
        g.GetAxisAlignedBoundingBoxRaw(out var minX,out var maxX,out var minY,out var maxY,out _,out _);
        double clearance=Math.Sqrt(Math.Pow(Math.Max(Math.Abs(minX),Math.Abs(maxX)),2)+Math.Pow(Math.Max(Math.Abs(minY),Math.Abs(maxY)),2));
        // Only colliding parts at body height obstruct a walker: a canopy without collision or a roof
        // overhead does not. An upright part (turned only about Z) reaches its XY half-diagonal and
        // spans its Z half-height; a tilted part keeps its full 3D half-diagonal both ways.
        var root=g.AbsolutePosition;double ground=Height(Math.Clamp(root.X,0,SizeX-1),Math.Clamp(root.Y,0,SizeY-1)),body=0;
        foreach(var part in g.Parts)
        {
            if(part.PhysicsShapeType==(byte)PhysShapeType.none)continue;
            var c=part.AbsolutePosition;var s=part.Scale;var r=part.GetWorldRotation();
            bool upright=Math.Abs(r.X)<1e-3&&Math.Abs(r.Y)<1e-3;double diagonal=Math.Sqrt(s.X*s.X+s.Y*s.Y+s.Z*s.Z)/2;
            double reach=upright?Math.Sqrt(s.X*s.X+s.Y*s.Y)/2:diagonal,height=upright?s.Z/2:diagonal;
            if(c.Z+height<ground+.35||c.Z-height>ground+1.8)continue;
            body=Math.Max(body,Math.Sqrt((c.X-root.X)*(c.X-root.X)+(c.Y-root.Y)*(c.Y-root.Y))+reach);
        }
        // The citizen embodiment is owned by the representation adapter, never by generic plans
        // (the historic seven-part body was excluded implicitly by its prim count).
        bool safe=g.PrimCount==1&&!g.IsAttachment&&!g.UsesPhysics&&p.Inventory.GetInventoryItems().Count==0&&g.UUID.ToString()!=Kernel.LumiId&&!galleryIds.Contains(g.UUID.ToString());
        var shape=p.Shape;
        string kind=shape.ProfileShape==ProfileShape.Square&&shape.PathCurve==(byte)Extrusion.Straight?"box":shape.ProfileShape==ProfileShape.HalfCircle&&shape.PathCurve==(byte)Extrusion.Curve1?"sphere":"other";
        // Deletion/restoration and cloning must round-trip every supported attribute.
        var canonicalTexture=new Primitive.TextureEntry(new UUID("5748decc-f629-461c-9a36-a35a221fe21f"));
        canonicalTexture.DefaultTexture.RGBA=color;
        bool plain=safe&&kind!="other"&&p.Flags==PrimFlags.None&&!p.IsSitTargetSet&&p.ClickAction==0&&!shape.SculptEntry&&!shape.FlexiEntry&&!shape.LightEntry&&!shape.ProjectionEntry&&!shape.MeshFlagEntry&&shape.ReflectionProbe==null&&shape.RenderMaterials==null&&shape.Media==null
            &&shape.ProfileHollow==0&&shape.ProfileBegin==0&&shape.ProfileEnd==0&&shape.PathBegin==0&&shape.PathEnd==0
            &&shape.PathScaleX==100&&shape.PathScaleY==100&&shape.PathShearX==0&&shape.PathShearY==0&&shape.PathSkew==0&&shape.PathTaperX==0&&shape.PathTaperY==0&&shape.PathTwist==0&&shape.PathTwistBegin==0&&shape.PathRadiusOffset==0&&shape.PathRevolutions==0
            &&te.GetBytes().SequenceEqual(canonicalTexture.GetBytes());
        return new(g.UUID.ToString(),p.Name,p.Description,p.OwnerID.ToString(),kind,
            V(g.AbsolutePosition),[q.X,q.Y,q.Z,q.W],V(p.Scale),[color.R,color.G,color.B,color.A],
            safe&&scene.Permissions.CanEditObject(g.UUID,new UUID(actor)),plain&&p.BaseMask==(uint)Permissions.All&&p.OwnerMask==(uint)Permissions.All&&p.EveryoneMask==0&&p.GroupMask==0&&p.NextOwnerMask==(uint)(Permissions.Copy|Permissions.Modify|Permissions.Transfer),
            safe&&scene.Permissions.CanDeleteObject(g.UUID,new UUID(actor)),clearance,body);
    }
    public double Height(double x,double y)
    {
        if(x<0||y<0||x>=SizeX||y>=SizeY)throw new InvalidDataException("Terrain sample outside region.");
        return scene.Heightmap[(int)x,(int)y];
    }
    public bool CanCreate(string actor,int count,double[] position)=>scene.Permissions.CanRezObject(count,new UUID(actor),ToV(position));
    public void Apply(Step step,string actor)
    {
        var owner=new UUID(actor);var group=scene.GetSceneObjectGroup(new UUID(step.id));
        if(step.after==null)
        {
            if(group==null)return;
            if(group.RootPart.OwnerID!=owner||!scene.Permissions.CanDeleteObject(group.UUID,owner))throw new InvalidDataException("Delete permission changed.");
            scene.DeleteSceneObject(group,false);return;
        }
        var e=step.after;
        if(group==null)
        {
            if(!CanCreate(actor,1,e.position))throw new InvalidDataException("Create permission changed.");
            var shape=e.shape=="sphere"?PrimitiveBaseShape.CreateSphere():PrimitiveBaseShape.CreateBox();shape.Scale=ToV(e.scale);
            group=new SceneObjectGroup(owner,ToV(e.position),shape);group.RootPart.UUID=new UUID(e.id);
            var part=group.RootPart;part.CreatorID=owner;part.OwnerID=owner;part.BaseMask=part.OwnerMask=(uint)Permissions.All;
            part.EveryoneMask=part.GroupMask=0;part.NextOwnerMask=(uint)(Permissions.Copy|Permissions.Modify|Permissions.Transfer);
            Set(group,e,true);
            if(!scene.AddNewSceneObject(group,true))throw new InvalidDataException("Simulator rejected insertion.");
        }
        else
        {
            if(group.RootPart.OwnerID!=owner||!scene.Permissions.CanEditObject(group.UUID,owner)||!scene.Permissions.CanObjectEntry(group,false,ToV(e.position)))throw new InvalidDataException("Edit or entry permission changed.");
            group.UpdateGroupPosition(ToV(e.position));Set(group,e,false);
        }
        group.HasGroupChanged=true;
        scene.ForceSceneObjectBackup(group);
        group.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
    }
    static void Set(SceneObjectGroup g,Entity e,bool created)
    {
        var p=g.RootPart;p.Name=e.name;p.Description=e.description;p.Scale=ToV(e.scale);
        g.UpdateGroupRotationR(new Quaternion((float)e.rotation[0],(float)e.rotation[1],(float)e.rotation[2],(float)e.rotation[3]));
        var texture=created ? new Primitive.TextureEntry(new UUID("5748decc-f629-461c-9a36-a35a221fe21f")) : new Primitive.TextureEntry(p.Shape.TextureEntry,0,p.Shape.TextureEntry.Length);
        var target=new Color4((float)e.color[0],(float)e.color[1],(float)e.color[2],(float)e.color[3]);
        if(created || texture.DefaultTexture.RGBA!=target)
        {
            if(!created && texture.FaceTextures.Any(f=>f!=null))throw new InvalidDataException("Per-face color editing is not supported in this slice.");
            texture.DefaultTexture.RGBA=target;p.Shape.TextureEntry=texture.GetBytes();
        }
    }
    static double[] V(Vector3 v)=>[v.X,v.Y,v.Z];
    static Vector3 ToV(double[] v)=>new((float)v[0],(float)v[1],(float)v[2]);
}
