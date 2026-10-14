// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Scenes.Serialization;
using Permissions = MutSea.Framework.PermissionMask;

namespace Finalverse.World;

// Genesis wardrobe: provisions a pinned, compiled worn avatar onto an account
// through the inherited asset, inventory and avatar services, exactly as a
// wearer's own outfit would be stored: a rigged attachment object, an alpha
// layer that hides the system body, Current Outfit links and appearance. It
// is an operator action (private configuration), applied while the wearer is
// offline, journaled with an appearance snapshot for explicit rollback.
public sealed partial class WorldModule
{
    bool genesisEnabled;string genesisHost="",genesisRollback="";
    readonly List<(UUID user,string directory,string pin)> genesisAssignments=new();
    double[]? galleryRow;readonly List<(string directory,string pin)> galleryPackages=new();readonly HashSet<string> galleryIds=new();
    string GenesisJournal=>Path.Combine(directory,"genesis.wardrobe.jsonl");

    void InitialiseGenesis(IConfigSource source)
    {
        var c=source.Configs["FinalverseGenesis"];
        genesisEnabled=c?.GetBoolean("Enabled",false)??false;
        if(!genesisEnabled)return;
        genesisHost=c!.GetString("HostRegion","");genesisRollback=c.GetString("Rollback","");
        foreach(var entry in c.GetString("Assignments","").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var parts=entry.Split('|',StringSplitOptions.TrimEntries);
            if(parts.Length!=3||!UUID.TryParse(parts[0],out var user)||!Path.IsPathRooted(parts[1])||parts[2].Length!=64)throw new InvalidDataException("Genesis assignment must be user|absolute compiled directory|compiled.json SHA-256.");
            if(genesisAssignments.Any(a=>a.user==user))throw new InvalidDataException("One Genesis assignment per wearer.");
            genesisAssignments.Add((user,parts[1],parts[2].ToLowerInvariant()));
        }
        foreach(var entry in c.GetString("GalleryPackages","").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var parts=entry.Split('|',StringSplitOptions.TrimEntries);
            if(parts.Length!=2||!Path.IsPathRooted(parts[0])||parts[1].Length!=64)throw new InvalidDataException("Genesis gallery entry must be absolute compiled directory|compiled.json SHA-256.");
            galleryPackages.Add((parts[0],parts[1].ToLowerInvariant()));
        }
        if(galleryPackages.Count>0)
        {
            var row=c.GetString("Gallery","").Split(',',StringSplitOptions.TrimEntries).Select(x=>double.TryParse(x,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var v)?v:double.NaN).ToArray();
            if(row.Length!=5||row.Any(double.IsNaN)||row[4]<0.5||row[4]>10||galleryPackages.Count>16)throw new InvalidDataException("Genesis gallery needs Gallery = x,y,floor z,facing degrees,spacing and at most 16 packages.");
            galleryRow=row;
        }
        if(!Guid.TryParse(genesisHost,out _)||genesisRollback!=""&&(!Guid.TryParse(genesisRollback,out _)||genesisAssignments.Count>0))throw new InvalidDataException("Genesis needs a host region; rollback names one action and excludes assignments.");
    }

    void GenesisPrimsLoaded(Scene s)
    {
        if(!genesisEnabled||RegionId!=genesisHost)return;
        lock(gate)
        {
            try
            {
                var records=File.Exists(GenesisJournal)?File.ReadAllLines(GenesisJournal).Select(l=>JsonDocument.Parse(l).RootElement.Clone()).ToList():new List<JsonElement>();
                if(genesisRollback!="")GenesisRollback(records,genesisRollback);
                else foreach(var (user,dir,pin) in genesisAssignments)GenesisProvision(records,user,dir,pin);
            }
            catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="genesis_wardrobe_failed",exception_type=ex.GetType().Name,error=ex.Message}));}
            foreach(var (dir,pin) in galleryPackages)
            {
                try{GenesisGallery(dir,pin,galleryIds.Count);}
                catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="genesis_gallery_failed",exception_type=ex.GetType().Name,error=ex.Message}));}
            }
        }
    }

    void GenesisRecord(string action,string kind,object result)
    {
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(new{event_id=Guid.NewGuid(),action_id=action,kind,timestamp=DateTimeOffset.UtcNow,result});
        using var file=new FileStream(GenesisJournal,FileMode.Append,FileAccess.Write,FileShare.Read);file.Write(data);file.WriteByte(10);file.Flush(true);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(GenesisJournal,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }

    static UUID WardrobeId(UUID user,string pin,string role)=>new(SHA256.HashData(Encoding.UTF8.GetBytes("finalverse-genesis:"+user+":"+pin+":"+role)).Take(16).ToArray(),0);

    void GenesisProvision(List<JsonElement> records,UUID user,string dir,string pin)
    {
        // Idempotent: a provisioned, not rolled-back assignment is left alone.
        bool RolledBack(string? a)=>records.Any(r=>r.GetProperty("kind").GetString()=="wardrobe_rollback"&&r.GetProperty("action_id").GetString()==a);
        var active=records.Where(r=>r.GetProperty("kind").GetString()=="wardrobe_result"&&r.GetProperty("result").GetProperty("user").GetString()==user.ToString()&&!RolledBack(r.GetProperty("action_id").GetString())).ToList();
        if(active.Any(r=>r.GetProperty("result").GetProperty("compiled_sha256").GetString()==pin))return;
        ContentCatalog.Require(scene.GetScenePresence(user)==null,"Provision Genesis wardrobes while the wearer is offline.");
        // Validate selected bytes before rolling back an existing outfit.
        var (package,mesh,materials,_)=AvatarCatalog.LoadWardrobe(dir,pin,out var alpha);
        // A new assignment replaces the wearer's previous Genesis outfit: journaled rollback first, so
        // the snapshot below is the wearer's own appearance again.
        foreach(var previous in active)GenesisRollback(records,previous.GetProperty("action_id").GetString()!);
        var appearance=scene.AvatarService.GetAppearance(user)??throw new InvalidDataException("The wearer has no stored appearance.");
        var inventory=scene.InventoryService;
        var clothing=inventory.GetFolderForType(user,FolderType.Clothing)??inventory.GetRootFolder(user)??throw new InvalidDataException("The wearer has no inventory.");
        var cof=inventory.GetFolderForType(user,FolderType.CurrentOutfit)??throw new InvalidDataException("The wearer has no Current Outfit folder.");

        string action=Guid.NewGuid().ToString();
        string snapshot=OSDParser.SerializeJsonString(appearance.Pack(new EntityTransferContext()));
        GenesisRecord(action,"wardrobe_intent",new{user=user.ToString(),package.package_id,package.version,compiled_sha256=pin,appearance_before=snapshot});

        void Store(UUID id,string name,sbyte type,byte[] data)=>StoreGenesisAsset(id,name,type,data,user);
        var meshId=DerivedId("mesh",package.mesh_sha256);Store(meshId,package.title+" "+package.version,(sbyte)AssetType.Mesh,mesh);
        var materialIds=package.materials.Select(m=>DerivedId("material",m.asset_sha256)).ToArray();
        for(int i=0;i<materialIds.Length;i++)Store(materialIds[i],package.materials[i].local_id,(sbyte)AssetType.Material,materials[i]);
        var surfaces=AvatarCatalog.LoadTextures(dir,package);
        for(int i=0;i<surfaces.Length;i++)Store(AvatarTextureAsset.Id(package.textures![i]),package.textures[i].local_id,(sbyte)AssetType.Texture,surfaces[i]);
        var alphaAsset=DerivedId("wearable",package.alpha_asset_sha256!);Store(alphaAsset,package.title+" body alpha",(sbyte)AssetType.Clothing,alpha!);

        // The worn object: one rigged mesh prim, owned by the wearer.
        var shape=PrimitiveBaseShape.CreateMesh(package.faces.Length,meshId);shape.Scale=new Vector3((float)package.scale[0],(float)package.scale[1],(float)package.scale[2]);
        var te=new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE);
        for(uint f=0;f<package.faces.Length;f++){var face=te.CreateFace(f);face.RGBA=Color4.White;face.TextureID=AvatarCatalog.TextureId(package,(int)f)??Primitive.TextureEntry.WHITE_TEXTURE;}
        shape.TextureEntry=te.GetBytes(package.faces.Length);
        shape.RenderMaterials=new Primitive.RenderMaterials{entries=materialIds.Select((id,i)=>new Primitive.RenderMaterials.RenderMaterialEntry{te_index=(byte)i,id=id}).ToArray()};
        var part=new SceneObjectPart(user,shape,Vector3.Zero,Quaternion.Identity,Vector3.Zero){UUID=WardrobeId(user,pin,"part"),CreatorID=user,OwnerID=user,LastOwnerID=user,
            Name=package.title+" (Genesis)",Description="Finalverse Genesis "+package.package_id+" "+package.version,
            BaseMask=(uint)Permissions.All,OwnerMask=(uint)Permissions.All,GroupMask=0,EveryoneMask=0,NextOwnerMask=(uint)(Permissions.Copy|Permissions.Modify|Permissions.Transfer)};
        part.PhysicsShapeType=(byte)PhysShapeType.none;
        var group=new SceneObjectGroup(part);
        var objectXml=Encoding.UTF8.GetBytes(SceneObjectSerializer.ToOriginalXmlFormat(group));
        var objectAsset=DerivedId("object",ContentCatalog.Hash(objectXml));Store(objectAsset,part.Name,(sbyte)AssetType.Object,objectXml);

        // Inventory: a Genesis folder with the object and alpha items, then Current Outfit links.
        string form=package.package_id.Replace("finalverse.genesis.","");
        var folder=new InventoryFolderBase(WardrobeId(user,pin,"folder"),"Genesis · "+package.title+" "+package.version+(form!=package.title.ToLowerInvariant()?" ("+form+")":""),user,clothing.ID){Type=(short)FolderType.None,Version=1};
        if(inventory.GetFolder(user,folder.ID)==null)ContentCatalog.Require(inventory.AddFolder(folder),"Genesis folder creation failed.");
        InventoryItemBase Item(string role,string name,UUID asset,AssetType type,InventoryType inv,uint flags)=>new()
        {
            ID=WardrobeId(user,pin,role),Owner=user,CreatorId=user.ToString(),Name=name,Description=part.Description,AssetID=asset,AssetType=(int)type,InvType=(int)inv,
            Folder=folder.ID,Flags=flags,BasePermissions=(uint)Permissions.All,CurrentPermissions=(uint)Permissions.All,EveryOnePermissions=0,GroupPermissions=0,
            NextPermissions=(uint)(Permissions.Copy|Permissions.Modify|Permissions.Transfer),CreationDate=(int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        var objectItem=Item("object",part.Name,objectAsset,AssetType.Object,InventoryType.Object,0);
        var alphaItem=Item("alpha",package.title+" body alpha",alphaAsset,AssetType.Clothing,InventoryType.Wearable,(uint)WearableType.Alpha);
        var links=new List<UUID>();
        foreach(var item in new[]{objectItem,alphaItem})
        {
            if(inventory.GetItem(user,item.ID)==null)ContentCatalog.Require(inventory.AddItem(item),"Genesis item creation failed.");
            // Current Outfit link order as viewers validate it: "@" + wearable type × 100 + layer.
            string order=item.InvType==(int)InventoryType.Wearable?"@"+((int)item.Flags*100):"";
            var link=new InventoryItemBase{ID=WardrobeId(user,pin,"link-"+item.ID),Owner=user,CreatorId=user.ToString(),Name=item.Name,Description=order,AssetID=item.ID,
                AssetType=(int)AssetType.Link,InvType=item.InvType,Flags=item.Flags,Folder=cof.ID,BasePermissions=(uint)Permissions.All,CurrentPermissions=(uint)Permissions.All,
                NextPermissions=(uint)Permissions.All,CreationDate=item.CreationDate};
            if(inventory.GetItem(user,link.ID)==null)ContentCatalog.Require(inventory.AddItem(link),"Current Outfit link failed.");
            links.Add(link.ID);
        }

        // Viewers trust their inventory cache by folder version; adding items does not bump it here.
        BumpFolders(user,cof.ID,folder.ID,clothing.ID);

        // Appearance: wear the alpha layer and the attachment (appended on its native point).
        int point=package.attachment_point switch{"Chest"=>(int)AttachmentPoint.Chest,"Spine"=>(int)AttachmentPoint.Spine,_=>(int)AttachmentPoint.Pelvis};
        appearance.SetWearable(AvatarWearable.ALPHA,new AvatarWearable(alphaItem.ID,alphaAsset));
        appearance.SetAttachment(point|0x80,objectItem.ID,objectAsset);
        ContentCatalog.Require(scene.AvatarService.SetAppearance(user,appearance),"Genesis appearance storage failed.");
        var stored=scene.AvatarService.GetAppearance(user);
        ContentCatalog.Require(stored!=null&&stored.Wearables[AvatarWearable.ALPHA].Count>0&&stored.Wearables[AvatarWearable.ALPHA][0].ItemID==alphaItem.ID
            &&stored.Wearables[AvatarWearable.ALPHA][0].AssetID==alphaAsset&&stored.GetAttachments().Any(a=>a.ItemID==objectItem.ID&&a.AttachPoint==point),"Genesis appearance postcondition failed.");
        // The inherited avatar store persists attachment item/point, not its
        // asset ID (resolved from owned inventory at login). Verify the object
        // asset via the native inventory binding below, preserving that contract.
        foreach(var id in links)
        {
            var link=inventory.GetItem(user,id);var target=link==null?null:inventory.GetItem(user,link.AssetID);
            ContentCatalog.Require(link!=null&&link.Owner==user&&link.Folder==cof.ID&&link.AssetType==(int)AssetType.Link&&target!=null&&target.Owner==user
                &&(target.ID==objectItem.ID&&target.AssetID==objectAsset||target.ID==alphaItem.ID&&target.AssetID==alphaAsset),"Genesis Current Outfit postcondition failed.");
        }
        GenesisRecord(action,"wardrobe_result",new{status="provisioned",user=user.ToString(),package.package_id,package.version,compiled_sha256=pin,
            folder=folder.ID.ToString(),object_item=objectItem.ID.ToString(),object_asset=objectAsset.ToString(),alpha_item=alphaItem.ID.ToString(),alpha_asset=alphaAsset.ToString(),
            mesh_asset=meshId.ToString(),attachment_point=point,cof_links=links.Select(x=>x.ToString()).ToArray()});
        log.Info(JsonSerializer.Serialize(new{@event="genesis_wardrobe_provisioned",action_id=action,package.package_id,package.version}));
    }

    // Assets: content-derived IDs; existing IDs must hold identical bytes.
    void StoreGenesisAsset(UUID id,string name,sbyte type,byte[] data,UUID creator)
    {
        var existing=scene.AssetService.Get(id.ToString());
        if(existing!=null){ContentCatalog.Require(existing.Data!=null&&existing.Data.AsSpan().SequenceEqual(data),"Genesis asset ID conflicts with different stored data.");return;}
        ContentCatalog.Require(scene.AssetService.Store(new AssetBase(id,name,type,creator.ToString()){Data=data})==id.ToString(),"Genesis asset storage was not acknowledged.");
    }

    // QA gallery: each pinned package as a standing animated-mesh mannequin in a
    // row, owned by the estate owner and never persisted (it is not attached to
    // backup, so removing the configuration and restarting removes it). Worn
    // and animated meshes share the skin, so this is the same rigged asset a
    // wearer would show, posed by the native stand on its own skeleton.
    void GenesisGallery(string dir,string pin,int slot)
    {
        var compiledBytes=File.ReadAllBytes(Path.Combine(dir,"compiled.json"));
        ContentCatalog.Require(ContentCatalog.Hash(compiledBytes)==pin,"Compiled avatar differs from its gallery pin.");
        var (package,mesh,materials,_)=AvatarCatalog.Load(dir,out _);
        AvatarCatalog.RequireCurrent(package);
        var owner=scene.RegionInfo.EstateSettings.EstateOwner;
        ContentCatalog.Require(owner!=UUID.Zero,"The gallery needs an estate owner.");
        var id=DerivedId("gallery",pin);
        if(scene.GetSceneObjectGroup(id)!=null){galleryIds.Add(id.ToString());return;}
        var meshId=DerivedId("mesh",package.mesh_sha256);StoreGenesisAsset(meshId,package.title+" "+package.version,(sbyte)AssetType.Mesh,mesh,owner);
        var materialIds=package.materials.Select(m=>DerivedId("material",m.asset_sha256)).ToArray();
        for(int i=0;i<materialIds.Length;i++)StoreGenesisAsset(materialIds[i],package.materials[i].local_id,(sbyte)AssetType.Material,materials[i],owner);
        var surfaces=AvatarCatalog.LoadTextures(dir,package);
        for(int i=0;i<surfaces.Length;i++)StoreGenesisAsset(AvatarTextureAsset.Id(package.textures![i]),package.textures[i].local_id,(sbyte)AssetType.Texture,surfaces[i],owner);
        var shape=PrimitiveBaseShape.CreateMesh(package.faces.Length,meshId);shape.Scale=ToV(package.scale);
        var te=new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE);
        for(uint f=0;f<package.faces.Length;f++){var c=package.faces[f].color;var face=te.CreateFace(f);face.RGBA=new Color4((float)c[0],(float)c[1],(float)c[2],(float)c[3]);face.TextureID=AvatarCatalog.TextureId(package,(int)f)??Primitive.TextureEntry.WHITE_TEXTURE;}
        shape.TextureEntry=te.GetBytes(package.faces.Length);
        shape.RenderMaterials=new Primitive.RenderMaterials{entries=materialIds.Select((m,i)=>new Primitive.RenderMaterials.RenderMaterialEntry{te_index=(byte)i,id=m}).ToArray()};
        shape.AnimeshEnabled=true;
        ContentCatalog.Require(shape.AnimeshEnabled,"Native animated-mesh flag was not accepted.");
        double yaw=galleryRow![3]*Math.PI/180,step=galleryRow[4]*slot;
        // The row runs along the mannequins' left, so slot 0 is on the viewer's left.
        var position=new Vector3((float)(galleryRow[0]-Math.Sin(yaw)*step),(float)(galleryRow[1]+Math.Cos(yaw)*step),(float)(galleryRow[2]+package.root_height));
        ContentCatalog.Require(position.X>=0&&position.Y>=0&&position.X<SizeX&&position.Y<SizeY,"Gallery slot lies outside the region.");
        var part=new SceneObjectPart(owner,shape,position,Quaternion.Identity,Vector3.Zero){UUID=id,CreatorID=owner,OwnerID=owner,LastOwnerID=owner,
            Name=package.title+" (Genesis gallery)",Description="Finalverse Genesis gallery "+package.package_id+" "+package.version,
            Text=package.package_id.Replace("finalverse.genesis.","")+" "+package.version,Color=System.Drawing.Color.FromArgb(255,214,236,226),
            BaseMask=(uint)Permissions.All,OwnerMask=(uint)Permissions.All,GroupMask=0,EveryoneMask=0,NextOwnerMask=(uint)(Permissions.Copy|Permissions.Modify|Permissions.Transfer)};
        part.RotationOffset=YawRotation(yaw);
        part.PhysicsShapeType=(byte)PhysShapeType.convex;
        part.AddFlag(PrimFlags.Phantom);
        var group=new SceneObjectGroup(part);
        ContentCatalog.Require(scene.AddNewSceneObject(group,false),"Simulator rejected the gallery mannequin.");
        galleryIds.Add(id.ToString());
        string idle=package.animations.TryGetValue("idle",out var v)&&!v.StartsWith("package:")?v:"2408fe9e-df1d-1d7d-f4ff-1384fa7b350f";
        part.AddAnimation(new UUID(idle),"idle");
        group.ScheduleGroupForUpdate(PrimUpdateFlags.FullUpdate);
        log.Info(JsonSerializer.Serialize(new{@event="genesis_gallery_rezzed",package.package_id,package.version,object_id=id.ToString(),position=V(position),persisted=false}));
    }

    void BumpFolders(UUID user,params UUID[] folders)
    {
        foreach(var id in folders.Distinct())
        {
            var folder=scene.InventoryService.GetFolder(user,id);if(folder==null)continue;
            folder.Version++;ContentCatalog.Require(scene.InventoryService.UpdateFolder(folder),"Inventory folder version update failed.");
        }
    }

    void GenesisRollback(List<JsonElement> records,string action)
    {
        if(records.Any(r=>r.GetProperty("kind").GetString()=="wardrobe_rollback"&&r.GetProperty("action_id").GetString()==action))return;
        var intent=records.Single(r=>r.GetProperty("kind").GetString()=="wardrobe_intent"&&r.GetProperty("action_id").GetString()==action).GetProperty("result");
        var result=records.SingleOrDefault(r=>r.GetProperty("kind").GetString()=="wardrobe_result"&&r.GetProperty("action_id").GetString()==action);
        var user=new UUID(intent.GetProperty("user").GetString()!);
        ContentCatalog.Require(scene.GetScenePresence(user)==null,"Roll back Genesis wardrobes while the wearer is offline.");
        var before=new AvatarAppearance((OSDMap)OSDParser.DeserializeJson(intent.GetProperty("appearance_before").GetString()!));
        scene.AvatarService.SetAppearance(user,before);
        if(result.ValueKind==JsonValueKind.Object)
        {
            scene.InventoryService.DeleteItems(user,result.GetProperty("result").GetProperty("cof_links").EnumerateArray().Select(x=>new UUID(x.GetString()!)).ToList());
            var cof=scene.InventoryService.GetFolderForType(user,FolderType.CurrentOutfit);if(cof!=null)BumpFolders(user,cof.ID);
        }
        GenesisRecord(action,"wardrobe_rollback",new{status="restored",user=user.ToString()});
        log.Info(JsonSerializer.Serialize(new{@event="genesis_wardrobe_rolled_back",action_id=action}));
    }
}
