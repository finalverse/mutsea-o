// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using Permissions=MutSea.Framework.PermissionMask;

namespace Finalverse.World;
public sealed partial class WorldModule : IContentWorld
{
    string contentDirectory="";
    ContentKernel? content;
    void IContentWorld.Check(ContentPlan plan)
    {
        var avatar=Avatar(plan.actor_id);var anchor=plan.anchor;
        ContentCatalog.Require(Math.Sqrt(avatar.Zip(anchor,(a,b)=>(a-b)*(a-b)).Sum())<=16,"Move within 16 meters of the import site.");
        double radius=plan.package.parts.Max(p=>Math.Sqrt(p.position[0]*p.position[0]+p.position[1]*p.position[1])+Math.Sqrt(p.scale.Sum(s=>s*s))/2)+.25;
        ContentCatalog.Require(radius<=4,"Initial small-asset footprint exceeded.");
        var ground=Height(anchor[0],anchor[1]);
        ContentCatalog.Require(ground>WaterHeight+.5&&Math.Abs(anchor[2]-ground)<.05,"Choose dry ground at terrain height.");
        foreach(var offset in new[]{new[]{0d,0d},new[]{radius,radius},new[]{radius,-radius},new[]{-radius,radius},new[]{-radius,-radius}})
        {
            var x=anchor[0]+offset[0];var y=anchor[1]+offset[1];
            ContentCatalog.Require(x>1&&y>1&&x<SizeX-1&&y<SizeY-1&&Math.Abs(Height(x,y)-ground)<.3,"Import footprint crosses a boundary or uneven terrain.");
        }
        // Reuse the simulator's rotated, linked-object AABB. A long promenade
        // must not be approximated as a 40-meter-wide circular obstruction.
        foreach(var existing in scene.GetSceneObjectGroups().Where(g=>!g.IsDeleted&&!g.IsAttachment))
        {
            existing.GetAxisAlignedBoundingBoxRaw(out var minX,out var maxX,out var minY,out var maxY,out _,out _);
            var pos=existing.AbsolutePosition;
            ContentCatalog.Require(!ContentKernel.Overlaps(anchor[0],anchor[1],radius,pos.X+minX,pos.X+maxX,pos.Y+minY,pos.Y+maxY),"Import overlaps an existing object or path.");
        }
        foreach(var part in plan.package.parts)
            ContentCatalog.Require(CanCreate(plan.actor_id,plan.package.parts.Length,anchor.Zip(part.position,(a,b)=>a+b).ToArray()),"Native parcel rez policy denied the import.");
        ContentCatalog.Require(plan.entity_ids.All(id=>scene.GetSceneObjectPart(new UUID(id))==null),"Imported ID already exists.");
    }
    string IContentWorld.Create(ContentPlan plan,byte[][] meshes,byte[][] materials)
    {
        var owner=new UUID(plan.actor_id);SceneObjectGroup? group=null;
        for(int i=0;i<(plan.package.materials?.Length??0);i++)
        {
            var source=plan.package.materials![i];var id=ContentMaterialAsset.Id(plan,source.local_id);
            var asset=new AssetBase(id,source.local_id,(sbyte)AssetType.Material,owner.ToString()){Data=materials[i]};
            ContentCatalog.Require(scene.AssetService.Store(asset)==id.ToString(),"Native PBR material storage was not acknowledged.");
        }
        Vector3 rootPosition=Vector3.Zero;Quaternion rootRotation=Quaternion.Identity;
        for(int i=0;i<plan.package.parts.Length;i++)
        {
            var source=plan.package.parts[i];var assetId=new UUID(plan.asset_ids[i]);
            // Inherited asset service and mesh primitive representation.
            var asset=new AssetBase(assetId,source.name,(sbyte)AssetType.Mesh,owner.ToString()){Data=meshes[i]};
            ContentCatalog.Require(scene.AssetService.Store(asset)==assetId.ToString(),"Native mesh asset storage was not acknowledged.");
            var shape=PrimitiveBaseShape.CreateMesh(source.faces.Length,assetId);shape.Scale=ToV(source.scale);
            var te=new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE);
            for(uint face=0;face<source.faces.Length;face++)
            {
                var f=te.CreateFace(face);var c=source.faces[face].color;
                f.RGBA=new Color4((float)c[0],(float)c[1],(float)c[2],(float)c[3]);f.Fullbright=source.faces[face].fullbright;
            }
            shape.TextureEntry=te.GetBytes(source.faces.Length);
            ContentMaterialAsset.Bind(shape,plan,source);
            var position=ToV(plan.anchor)+ToV(source.position);
            var rotation=new Quaternion((float)source.rotation[0],(float)source.rotation[1],(float)source.rotation[2],(float)source.rotation[3]);
            var part=new SceneObjectPart(owner,shape,position,Quaternion.Identity,Vector3.Zero){UUID=new UUID(plan.entity_ids[i]),CreatorID=owner,OwnerID=owner,
                Name=i==0?plan.package.title:source.name,Description=source.description,BaseMask=(uint)Permissions.All,OwnerMask=(uint)Permissions.All,
                EveryoneMask=0,GroupMask=0,NextOwnerMask=(uint)(Permissions.Copy|Permissions.Modify|Permissions.Transfer)};
            part.PhysicsShapeType=(byte)(source.collision?.shape=="none"?PhysShapeType.none:PhysShapeType.convex);
            if(source.seat is {} seat)
            {
                part.SitTargetPosition=ToV(seat.position);
                part.SitTargetOrientation=new Quaternion((float)seat.rotation[0],(float)seat.rotation[1],(float)seat.rotation[2],(float)seat.rotation[3]);
            }
            if(group==null){rootPosition=position;rootRotation=rotation;part.RotationOffset=rotation;group=new SceneObjectGroup(part);}
            else{part.OffsetPosition=(position-rootPosition)*Quaternion.Inverse(rootRotation);part.RotationOffset=rotation*Quaternion.Inverse(rootRotation);group.AddPart(part);}
        }
        if(group==null)throw new InvalidDataException("Empty native import.");
        if(group.PrimCount>1)group.RootPart.LinkNum=1;
        if(!scene.AddNewSceneObject(group,true))throw new InvalidDataException("Native scene insertion failed.");
        group.HasGroupChanged=true;scene.ForceSceneObjectBackup(group);group.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
        return ((IContentWorld)this).Fingerprint(plan)??throw new InvalidDataException("Native import was not observable.");
    }
    string? IContentWorld.Fingerprint(ContentPlan plan)
    {
        var group=scene.GetSceneObjectGroup(new UUID(plan.entity_ids[0]));if(group==null||group.IsDeleted)return null;
        return ContentFingerprint.Hash(group);
    }
    bool IContentWorld.MatchesFingerprint(ContentPlan plan,string fingerprint)
    {
        var group=scene.GetSceneObjectGroup(new UUID(plan.entity_ids[0]));
        return group!=null&&!group.IsDeleted&&ContentFingerprint.Matches(group,fingerprint);
    }
    void IContentWorld.CheckDelete(ContentPlan plan)
    {
        var group=scene.GetSceneObjectGroup(new UUID(plan.entity_ids[0]));
        ContentCatalog.Require(group!=null&&group.RootPart.OwnerID==new UUID(plan.actor_id)&&scene.Permissions.CanDeleteObject(group.UUID,new UUID(plan.actor_id)),"Native ownership/delete policy changed.");
        ContentCatalog.Require(group!.GetSittingAvatars().Count==0,"Stand up before undoing occupied seating.");
    }
    void IContentWorld.Delete(ContentPlan plan)
    {
        ((IContentWorld)this).CheckDelete(plan);
        var group=scene.GetSceneObjectGroup(new UUID(plan.entity_ids[0]));
        scene.DeleteSceneObject(group!,false);
    }
}
