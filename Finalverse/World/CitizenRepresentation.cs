// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nini.Config;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Scenes.Serialization;

namespace Finalverse.World;

// Identity-preserving representation adapter. The logical citizen and its root
// entity ID never change; only the embodiment geometry/animation does. A
// migration is an operator action (pinned private configuration), journaled
// before mutation, with an exact native XML snapshot for explicit rollback.
public sealed partial class WorldModule
{
    const string EmbodimentDescription="Finalverse persistent citizen embodiment v1";
    // Representation profile: root height above ground and body-front yaw offset
    // relative to the motion heading. The linked primitive keeps its historic values.
    double citizenLift=.9,citizenYaw=Math.PI/2,citizenSpeed=2;
    string representation="linked_primitive_citizen";
    string representationDirectory="",representationPin="",representationRollback="";
    bool representationRecovery;
    AvatarCompiled? avatar;byte[]? avatarMesh;byte[][]? avatarMaterials,avatarMotions;
    UUID avatarMeshId=UUID.Zero;UUID[] avatarMaterialIds=[],avatarMotionIds=[];
    readonly List<JsonElement> representationRecords=new();
    string animationState="idle";DateTimeOffset animationUntil=DateTimeOffset.MaxValue;DateTimeOffset turnUntil=DateTimeOffset.MinValue,faceUntil=DateTimeOffset.MinValue;double faceYaw;

    void InitialiseRepresentation(IConfig c)
    {
        representationDirectory=c.GetString("Representation","");representationPin=c.GetString("RepresentationSha256","").ToLowerInvariant();
        representationRollback=c.GetString("RepresentationRollback","");
        if(representationDirectory!=""&&(!Path.IsPathRooted(representationDirectory)||representationPin.Length!=64))throw new InvalidDataException("Citizen representation needs an absolute directory and a pinned compiled.json SHA-256.");
        if(representationRollback!=""&&(representationDirectory!=""||!Guid.TryParse(representationRollback,out _)))throw new InvalidDataException("Rollback names one migration action and requires Representation to be unset.");
    }

    static UUID DerivedId(string kind,string hash)=>new(SHA256.HashData(Encoding.UTF8.GetBytes("finalverse-avatar:"+kind+":"+hash)).Take(16).ToArray(),0);
    string SnapshotDirectory=>Path.Combine(directory,RegionId+".citizen-representation");

    // Called from StartCitizen after the citizen journal has been read.
    void StartRepresentation()
    {
        var intents=representationRecords.Where(r=>r.GetProperty("kind").GetString()=="representation_intent").ToArray();
        var resolved=representationRecords.Where(r=>r.GetProperty("kind").GetString() is "representation_result" or "representation_rollback").Select(r=>r.GetProperty("action_id").GetString()).ToHashSet();
        if(representationDirectory!="")LoadAvatar();
        foreach(var intent in intents.Where(i=>!resolved.Contains(i.GetProperty("action_id").GetString())))ResolveIntent(intent);
        if(representationRecovery)return;
        if(representationRollback!=""){Rollback(representationRollback);return;}
        var body=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId));
        if(avatar!=null)
        {
            StoreAvatarAssets();
            if(body!=null&&!IsAvatarBody(body))Migrate(body);
            else if(body!=null){UseAvatarProfile();RecordPackageUpdate(body);}
            if(representation=="animated_object")Animate("idle",null);
            return;
        }
        // No configuration: keep whatever the journal says is current; never roll back implicitly.
        if(body!=null&&body.RootPart.Shape.AnimeshEnabled)
        {
            string mesh=body.RootPart.Shape.SculptTexture.ToString();
            var current=representationRecords.LastOrDefault(r=>r.GetProperty("kind").GetString()=="representation_result"
                &&r.GetProperty("result").TryGetProperty("mesh_asset_id",out var id)&&id.GetString()==mesh&&r.GetProperty("result").TryGetProperty("root_height",out _));
            if(current.ValueKind==JsonValueKind.Object){representation="animated_object";citizenLift=current.GetProperty("result").GetProperty("root_height").GetDouble();citizenYaw=0;citizenSpeed=AnimatedWalkSpeed;Animate("idle",null);}
            else{representationRecovery=true;log.Warn("{\"event\":\"citizen_representation_unpinned\"}");}
        }
    }

    void LoadAvatar()
    {
        var bytes=File.ReadAllBytes(Path.Combine(representationDirectory,"compiled.json"));
        if(ContentCatalog.Hash(bytes)!=representationPin)throw new InvalidDataException("Compiled avatar differs from its configuration pin.");
        (avatar,avatarMesh,avatarMaterials,avatarMotions)=AvatarCatalog.Load(representationDirectory);
        avatarMeshId=DerivedId("mesh",avatar.mesh_sha256);
        avatarMaterialIds=avatar.materials.Select(m=>DerivedId("material",m.asset_sha256)).ToArray();
        avatarMotionIds=(avatar.animation_assets??[]).Select(m=>DerivedId("animation",m.asset_sha256)).ToArray();
    }

    void StoreAvatarAssets()
    {
        void Store(UUID id,string name,sbyte type,byte[] data)
        {
            var existing=scene.AssetService.Get(id.ToString());
            if(existing!=null){ContentCatalog.Require(existing.Data!=null&&existing.Data.AsSpan().SequenceEqual(data),"Avatar asset ID conflicts with different stored data.");return;}
            ContentCatalog.Require(scene.AssetService.Store(new AssetBase(id,name,type,citizenOwner){Data=data})==id.ToString(),"Avatar asset storage was not acknowledged.");
        }
        Store(avatarMeshId,avatar!.title+" "+avatar.version,(sbyte)AssetType.Mesh,avatarMesh!);
        for(int i=0;i<avatarMaterialIds.Length;i++)Store(avatarMaterialIds[i],avatar.materials[i].local_id,(sbyte)AssetType.Material,avatarMaterials![i]);
        for(int i=0;i<avatarMotionIds.Length;i++)Store(avatarMotionIds[i],avatar.animation_assets![i].local_id,(sbyte)AssetType.Animation,avatarMotions![i]);
    }

    // Mesh and every bound material must match, so material-only package upgrades also migrate.
    bool IsAvatarBody(SceneObjectGroup body)=>body.PrimCount==1&&body.RootPart.Shape.AnimeshEnabled&&body.RootPart.Shape.SculptTexture==avatarMeshId
        &&body.RootPart.Shape.RenderMaterials?.entries?.Select(e=>e.id).SequenceEqual(avatarMaterialIds)==true;
    // A small companion walks at a child's pace; the linked primitive keeps its historic 2 m/s.
    const double AnimatedWalkSpeed=1.2;
    void UseAvatarProfile(){representation="animated_object";citizenLift=avatar!.root_height;citizenYaw=0;citizenSpeed=AnimatedWalkSpeed;}

    // Same body, newer pinned package (e.g. motions only): record it so the WorldLine stays truthful.
    void RecordPackageUpdate(SceneObjectGroup body)
    {
        var last=representationRecords.LastOrDefault(r=>r.GetProperty("kind").GetString()=="representation_result"&&r.GetProperty("result").TryGetProperty("compiled_sha256",out _));
        if(last.ValueKind==JsonValueKind.Object&&last.GetProperty("result").GetProperty("compiled_sha256").GetString()==representationPin)return;
        string action=Guid.NewGuid().ToString();
        RecordCitizen(action,"representation_result",new{status="package_updated",avatar!.package_id,avatar.version,compiled_sha256=representationPin,mesh_asset_id=avatarMeshId.ToString(),
            material_asset_ids=avatarMaterialIds.Select(x=>x.ToString()).ToArray(),motion_asset_ids=avatarMotionIds.Select(x=>x.ToString()).ToArray(),avatar.root_height,identity=Kernel.LumiId,after=Summary(body)});
        representationRecords.Add(JsonSerializer.SerializeToElement(new{kind="representation_result",action_id=action,result=new{compiled_sha256=representationPin,mesh_asset_id=avatarMeshId.ToString(),avatar.root_height}}));
    }

    object Summary(SceneObjectGroup g)
    {
        var p=g.RootPart;var q=p.RotationOffset;
        return new{root_id=g.UUID.ToString(),component_ids=g.Parts.Select(x=>x.UUID.ToString()).OrderBy(x=>x).ToArray(),parts=g.PrimCount,position=V(g.AbsolutePosition),
            rotation=new[]{q.X,q.Y,q.Z,q.W},owner=p.OwnerID.ToString(),masks=new[]{p.BaseMask,p.OwnerMask,p.GroupMask,p.EveryoneMask,p.NextOwnerMask},
            animesh=p.Shape.AnimeshEnabled,mesh_asset_id=p.Shape.SculptEntry?p.Shape.SculptTexture.ToString():null};
    }

    static double Yaw(Quaternion q)=>Math.Atan2(2*(q.W*q.Z+q.X*q.Y),1-2*(q.Y*q.Y+q.Z*q.Z));
    static Quaternion YawRotation(double yaw)=>new(0,0,(float)Math.Sin(yaw/2),(float)Math.Cos(yaw/2));

    SceneObjectGroup BuildAvatarBody(SceneObjectPart template,Vector3 position,Quaternion rotation)
    {
        var shape=PrimitiveBaseShape.CreateMesh(avatar!.faces.Length,avatarMeshId);shape.Scale=ToV(avatar.scale);
        var te=new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE);
        for(uint i=0;i<avatar.faces.Length;i++){var c=avatar.faces[i].color;te.CreateFace(i).RGBA=new Color4((float)c[0],(float)c[1],(float)c[2],(float)c[3]);}
        shape.TextureEntry=te.GetBytes(avatar.faces.Length);
        shape.RenderMaterials=new Primitive.RenderMaterials{entries=avatarMaterialIds.Select((id,i)=>new Primitive.RenderMaterials.RenderMaterialEntry{te_index=(byte)i,id=id}).ToArray()};
        shape.AnimeshEnabled=true;
        ContentCatalog.Require(shape.AnimeshEnabled,"Native animated-mesh flag was not accepted.");
        var part=new SceneObjectPart(template.OwnerID,shape,position,Quaternion.Identity,Vector3.Zero){UUID=new UUID(Kernel.LumiId),CreatorID=template.CreatorID,
            OwnerID=template.OwnerID,GroupID=template.GroupID,Name=template.Name,Description=EmbodimentDescription,Text=template.Text,Color=template.Color,
            BaseMask=template.BaseMask,OwnerMask=template.OwnerMask,GroupMask=template.GroupMask,EveryoneMask=template.EveryoneMask,NextOwnerMask=template.NextOwnerMask};
        part.RotationOffset=rotation;
        part.PhysicsShapeType=(byte)PhysShapeType.convex;
        return new SceneObjectGroup(part);
    }

    void Migrate(SceneObjectGroup old)
    {
        ContentCatalog.Require(navigation is not {status:"moving"}&&!WorldRecoveryRequired,"Representation changes require a stationary citizen and a recovered world.");
        ContentCatalog.Require(old.RootPart.OwnerID.ToString()==citizenOwner&&old.RootPart.Description==EmbodimentDescription&&!old.IsAttachment&&old.GetSittingAvatars().Count==0,"Embodiment identity, ownership or occupancy prevents migration.");
        string action=Guid.NewGuid().ToString();
        Directory.CreateDirectory(SnapshotDirectory);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(SnapshotDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        string snapshot=Path.Combine(SnapshotDirectory,action+".before.xml");
        var xml=Encoding.UTF8.GetBytes(SceneObjectSerializer.ToXml2Format(old));
        using(var file=new FileStream(snapshot,FileMode.CreateNew,FileAccess.Write)){file.Write(xml);file.Flush(true);}
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(snapshot,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var before=Summary(old);double oldYaw=old.RootPart.Shape.AnimeshEnabled?0:Math.PI/2;
        var p=old.AbsolutePosition;double heading=Yaw(old.GroupRotation)-oldYaw;
        var position=new Vector3(p.X,p.Y,(float)(Height(p.X,p.Y)+avatar!.root_height));
        RecordCitizen(action,"representation_intent",new{from=before,to=new{avatar.package_id,avatar.version,compiled_sha256=representationPin,mesh_asset_id=avatarMeshId.ToString(),avatar.root_height},
            snapshot=Path.GetFileName(snapshot),snapshot_sha256=ContentCatalog.Hash(xml)});
        var template=old.RootPart;var retired=old.Parts.Where(x=>x.UUID!=old.UUID).Select(x=>x.UUID.ToString()).OrderBy(x=>x).ToArray();
        scene.DeleteSceneObject(old,false);
        try
        {
            var body=BuildAvatarBody(template,position,YawRotation(heading));
            ContentCatalog.Require(scene.AddNewSceneObject(body,true),"Simulator rejected the new embodiment.");
            body.HasGroupChanged=true;scene.ForceSceneObjectBackup(body);body.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
            UseAvatarProfile();
            RecordCitizen(action,"representation_result",new{status="migrated",avatar.package_id,avatar.version,compiled_sha256=representationPin,mesh_asset_id=avatarMeshId.ToString(),
                material_asset_ids=avatarMaterialIds.Select(x=>x.ToString()).ToArray(),motion_asset_ids=avatarMotionIds.Select(x=>x.ToString()).ToArray(),avatar.root_height,yaw_offset=0,identity=Kernel.LumiId,
                mapping=new{preserved=new[]{Kernel.LumiId},retired},after=Summary(body),snapshot=Path.GetFileName(snapshot)});
            representationRecords.Add(JsonSerializer.SerializeToElement(new{kind="representation_result",action_id=action,result=new{compiled_sha256=representationPin,mesh_asset_id=avatarMeshId.ToString(),avatar.root_height}}));
            log.Info(JsonSerializer.Serialize(new{@event="citizen_representation_migrated",action_id=action,avatar.package_id,avatar.version}));
        }
        catch(Exception ex)
        {
            // Compensate with the exact snapshot; if that fails, block citizen writes for an operator.
            try{RestoreSnapshot(snapshot);RecordCitizen(action,"representation_result",new{status="compensated",error=ex.Message});}
            catch(Exception restore){representationRecovery=true;log.Error(JsonSerializer.Serialize(new{@event="citizen_representation_recovery_required",action_id=action,error=ex.Message,restore_error=restore.Message}));}
        }
    }

    void RestoreSnapshot(string snapshot)
    {
        var group=SceneObjectSerializer.FromXml2Format(File.ReadAllText(snapshot))??throw new InvalidDataException("Snapshot could not be parsed.");
        ContentCatalog.Require(group.UUID==new UUID(Kernel.LumiId)&&scene.GetSceneObjectGroup(group.UUID)==null,"Snapshot identity conflicts with current world state.");
        ContentCatalog.Require(scene.AddNewSceneObject(group,true),"Simulator rejected the restored embodiment.");
        group.HasGroupChanged=true;scene.ForceSceneObjectBackup(group);group.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
    }

    // An unresolved intent is completed only if the world observably matches one
    // side of it; anything else requires an operator (no automatic repair).
    void ResolveIntent(JsonElement intent)
    {
        string action=intent.GetProperty("action_id").GetString()!;var to=intent.GetProperty("result").GetProperty("to");
        var body=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId));
        string? mesh=body?.RootPart.Shape.SculptEntry==true?body.RootPart.Shape.SculptTexture.ToString():null;
        if(body!=null&&body.PrimCount==1&&body.RootPart.Shape.AnimeshEnabled&&mesh==to.GetProperty("mesh_asset_id").GetString())
            RecordCitizen(action,"representation_result",new{status="observed_complete",mesh_asset_id=mesh,root_height=to.GetProperty("root_height").GetDouble()});
        else if(body!=null&&body.PrimCount==intent.GetProperty("result").GetProperty("from").GetProperty("parts").GetInt32()&&mesh==intent.GetProperty("result").GetProperty("from").GetProperty("mesh_asset_id").GetString())
            RecordCitizen(action,"representation_result",new{status="not_applied"});
        else{representationRecovery=true;log.Error(JsonSerializer.Serialize(new{@event="citizen_representation_recovery_required",action_id=action}));}
    }

    void Rollback(string action)
    {
        if(representationRecords.Any(r=>r.GetProperty("kind").GetString()=="representation_rollback"&&r.GetProperty("action_id").GetString()==action))return;
        var intent=representationRecords.Single(r=>r.GetProperty("kind").GetString()=="representation_intent"&&r.GetProperty("action_id").GetString()==action);
        var details=intent.GetProperty("result");string snapshot=Path.Combine(SnapshotDirectory,details.GetProperty("snapshot").GetString()!);
        ContentCatalog.Require(Path.GetFileName(snapshot)==action+".before.xml"&&ContentCatalog.Hash(File.ReadAllBytes(snapshot))==details.GetProperty("snapshot_sha256").GetString(),"Rollback snapshot is missing or changed.");
        var body=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId));
        ContentCatalog.Require(body==null||body.PrimCount==1&&body.RootPart.Shape.SculptTexture.ToString()==details.GetProperty("to").GetProperty("mesh_asset_id").GetString(),"Current embodiment is not the one created by this migration.");
        RecordCitizen(action,"representation_rollback_intent",new{snapshot=Path.GetFileName(snapshot)});
        if(body!=null)scene.DeleteSceneObject(body,false);
        RestoreSnapshot(snapshot);
        representation="linked_primitive_citizen";citizenLift=.9;citizenYaw=Math.PI/2;citizenSpeed=2;
        RecordCitizen(action,"representation_rollback",new{status="restored",after=Summary(scene.GetSceneObjectGroup(new UUID(Kernel.LumiId))!)});
        log.Info(JsonSerializer.Serialize(new{@event="citizen_representation_rolled_back",action_id=action}));
    }

    object? AvatarBinding()=>representation!="animated_object"?null:
        new{representation,package_id=avatar?.package_id,version=avatar?.version,mesh_asset_id=avatar!=null?avatarMeshId.ToString():null,root_height=citizenLift,animation=animationState};

    // Exactly one role animation is active; timed roles return to walk/idle.
    void Animate(string role,TimeSpan? duration)
    {
        if(representation!="animated_object"||avatar==null)return;
        var part=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId))?.RootPart;if(part==null)return;
        if(!avatar.animations.TryGetValue(role,out var value))return;
        var asset=Array.FindIndex(avatar.animation_assets??[],x=>"package:"+x.local_id==value);
        var target=asset>=0?avatarMotionIds[asset]:new UUID(value);
        if(part.Animations!=null)foreach(var id in part.Animations.Keys.ToArray())if(id!=target)part.RemoveAnimation(id);
        part.AddAnimation(target,role);
        animationState=role;animationUntil=duration is {} d?DateTimeOffset.UtcNow+d:DateTimeOffset.MaxValue;
    }

    void AnimationFrame(DateTimeOffset now)
    {
        if(representation!="animated_object"||now<animationUntil)return;
        Animate(navigation is {status:"moving"}?"walk":"idle",null);
    }

    static readonly string[] Greetings=["hello","hi","hey","welcome","greetings","good morning","good afternoon","good evening"];
    static bool IsGreeting(string text)
    {
        var lower=text.TrimStart().ToLowerInvariant();
        return Greetings.Any(g=>lower.StartsWith(g)&&(lower.Length==g.Length||!char.IsLetter(lower[g.Length])));
    }
    // A person greeting Lumi gets an immediate native wave while the reply is prepared.
    // A person greeting Lumi gets an immediate wave; any other message gets an attentive listening
    // pose until her reply (talk) replaces it, bounded so a failed reply still returns to idle.
    void HearGesture(string text)
    {
        if(navigation is {status:"moving"})return;
        if(IsGreeting(text))Animate("greet",TimeSpan.FromSeconds(1.8));
        else if(avatar?.animations.ContainsKey("listen")==true)Animate("listen",TimeSpan.FromSeconds(20));
    }
    void SpeechGesture(string text)
    {
        if(navigation is {status:"moving"})return;
        if(IsGreeting(text))Animate("greet",TimeSpan.FromSeconds(1.8));
        else Animate("talk",TimeSpan.FromSeconds(Math.Clamp(text.Length*.065,1.6,8)));
    }

    // After arriving home, turn to face outward from the door (native turn animation).
    void FaceOutward()
    {
        if(representation!="animated_object")return;
        try
        {
            var (floor,_)=Home();var door=Door(floor);
            faceYaw=Math.Atan2(door[1]-floor.position[1],door[0]-floor.position[0])+citizenYaw;
            double diff=Math.IEEERemainder(faceYaw-Yaw(Body().GroupRotation),2*Math.PI);
            if(Math.Abs(diff)<Math.PI/8)return;
            faceUntil=DateTimeOffset.UtcNow.AddSeconds(1.2);Animate(diff>0?"turn_left":"turn_right",TimeSpan.FromSeconds(1.2));
        }
        catch(InvalidDataException){}
    }
    void FaceFrame(DateTimeOffset now,double dt)
    {
        if(now>=faceUntil||navigation is {status:"moving"})return;
        var body=scene.GetSceneObjectGroup(new UUID(Kernel.LumiId));if(body==null)return;
        double yawNow=Yaw(body.GroupRotation),step=Math.Clamp(Math.IEEERemainder(faceYaw-yawNow,2*Math.PI),-Math.PI*dt/.6,Math.PI*dt/.6);
        body.UpdateGroupRotationR(YawRotation(yawNow+step));body.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
        if(now.AddMilliseconds(250)>=faceUntil){body.HasGroupChanged=true;scene.ForceSceneObjectBackup(body);}
    }
}
