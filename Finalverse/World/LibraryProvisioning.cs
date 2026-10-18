// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using OpenMetaverse;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using Permissions=MutSea.Framework.PermissionMask;
namespace Finalverse.World;

// Operator-only admission of exact, previously quarantined and audited CC0 bytes.
// Wraps the native per-region archive module, never the global console command.
// It is a legacy restore receipt, not a claim of atomic WorldKernel rollback.
public sealed partial class WorldModule
{
    bool libraryRecovery,libraryQueued;volatile bool libraryStopped;Guid libraryRequest;LibraryArchive? libraryImport;
    string LibraryReceipt(string id)=>Path.Combine(directory,id+".library-import.json");
    bool LibraryReady(LibraryDestination d)
    {
        if(d.archive==null)return true;
        try
        {
            var f=new FileInfo(LibraryReceipt(d.region_id));if(!f.Exists||f.Length>4096||(f.Attributes&FileAttributes.ReparsePoint)!=0)return false;
            using var doc=JsonDocument.Parse(File.ReadAllBytes(f.FullName));var r=doc.RootElement;
            return r.GetProperty("status").GetString()=="committed"&&r.GetProperty("sha256").GetString()==d.archive.sha256;
        }
        catch{return false;}
    }
    void LibraryRecord(string status,string? error=null)
    {
        var value=new{schema_version=1,kind="operator_passive_oar_restore",region_id=RegionId,request_id=libraryRequest,status,
            archive_id=libraryImport!.id,libraryImport.sha256,libraryImport.source_sha256,libraryImport.expected_parts,
            timestamp=DateTimeOffset.UtcNow,error,rollback="Remove only this new region and restore its pre-import database snapshot. Shared assets are not automatically erased."};
        string path=LibraryReceipt(RegionId),temporary=path+"."+Guid.NewGuid()+".tmp";
        try
        {
            using(var f=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write)){JsonSerializer.Serialize(f,value);f.Flush(true);}
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temporary,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            File.Move(temporary,path,true);
        }
        finally{File.Delete(temporary);}
    }
    void QueueLibraryImport(Scene s)
    {
        if(library?.Find(RegionId) is not {archive:not null} d||LibraryReady(d))
        {s.EventManager.OnFrame-=LibraryImportTick;return;}
        lock(gate){libraryRecovery=true;libraryQueued=true;}
    }
    void LibraryImportTick()
    {
        lock(gate)
        {
            // OnPrimsLoaded precedes SceneManager registration. The inherited
            // archive reader resolves the target through that registry.
            if(!libraryQueued||libraryStopped||!SceneManager.Instance.TryGetScene(new UUID(RegionId),out var registered)||!ReferenceEquals(registered,scene))return;
            libraryQueued=false;scene.EventManager.OnFrame-=LibraryImportTick;
        }
        ThreadPool.QueueUserWorkItem(_=>{if(!libraryStopped)LibraryPrimsLoaded(scene);});
    }
    void LibraryPrimsLoaded(Scene s)
    {
        if(library?.Find(RegionId) is not {archive:not null} d)return;
        lock(gate)
        {
            libraryImport=d.archive;
            if(LibraryReady(d))return;
            libraryRecovery=true;
            try
            {
                ContentCatalog.Require(!File.Exists(LibraryReceipt(RegionId)),"An earlier import needs operator recovery; it will not replay.");
                ContentCatalog.Require(libraryScriptsDisabled,"Both native script engines must be disabled for library archive regions.");
                ContentCatalog.Require(!scene.GetSceneObjectGroups().Any(g=>!g.IsDeleted&&!g.IsAttachment),"Library restore requires an empty new region.");
                string folder=Path.Combine(libraryRoot,d.archive.id);
                byte[] Read(string name,int max)
                {
                    var f=new FileInfo(Path.Combine(folder,name));ContentCatalog.Require(f.Exists&&f.Length is >0&&f.Length<=max&&(f.Attributes&FileAttributes.ReparsePoint)==0,"Missing or oversized approved import file.");return File.ReadAllBytes(f.FullName);
                }
                var bytes=Read("staging.oar",64*1024*1024);var receipt=Read("report.json",16*1024*1024);
                ContentCatalog.Require(ContentCatalog.Hash(bytes)==d.archive.sha256&&ContentCatalog.Hash(receipt)==d.archive.report_sha256,"Approved import bytes changed.");
                using var report=JsonDocument.Parse(receipt);var root=report.RootElement;
                ContentCatalog.Require(root.GetProperty("staging_sha256").GetString()==d.archive.sha256&&root.GetProperty("source_sha256").GetString()==d.archive.source_sha256&&root.GetProperty("scene_parts").GetInt32()==d.archive.expected_parts,"Import receipt differs from admission.");
                foreach(var m in root.GetProperty("members").EnumerateArray())
                {
                    if(!m.TryGetProperty("staging_path",out var p)||!p.GetString()!.StartsWith("assets/",StringComparison.Ordinal))continue;
                    string id=p.GetString()![7..].Split('_')[0];
                    ContentCatalog.Require(Guid.TryParseExact(id,"D",out _),"Invalid imported asset identity.");
                    var existing=scene.AssetService.Get(id);
                    ContentCatalog.Require(existing==null||ContentCatalog.Hash(existing.Data)==m.GetProperty("staging_sha256").GetString(),"An imported asset UUID conflicts with existing world data. Admission refused.");
                }
                var archiver=scene.RequestModuleInterface<IRegionArchiverModule>()??throw new InvalidDataException("Native region archive module is unavailable.");
                libraryRequest=Guid.NewGuid();LibraryRecord("pending");
                // The inherited stream overload expects an uncompressed seekable TAR.
                using var tar=PassiveArchive.OpenTar(bytes);
                archiver.DearchiveRegion(tar,libraryRequest,new Dictionary<string,object>{{"default-user",new UUID(PlaceOperator)}});
            }
            catch(Exception ex)
            {
                if(libraryRequest!=Guid.Empty)LibraryRecord("failed",ex.GetType().Name);
                log.Error(JsonSerializer.Serialize(new{@event="library_import_refused",region_id=RegionId,error=ex.Message,recovery_required=true}));
            }
        }
    }
    void LibraryOarLoaded(Guid request,List<UUID> loaded,string message)
    {
        if(libraryRequest==Guid.Empty||request!=libraryRequest)return;
        lock(gate)
        {
            try
            {
                ContentCatalog.Require(string.IsNullOrEmpty(message)&&loaded.Count==1&&loaded[0].ToString()==RegionId,"Native archive restore did not confirm exactly this region.");
                var groups=scene.GetSceneObjectGroups().Where(g=>!g.IsDeleted&&!g.IsAttachment).ToArray();
                ContentCatalog.Require(groups.Sum(g=>g.PrimCount)==libraryImport!.expected_parts,"Restored scene part count differs.");
                foreach(var group in groups)
                {
                    foreach(var part in group.Parts)
                    {
                        ContentCatalog.Require(part.Inventory.GetInventoryItems().Count==0&&part.OwnerID.ToString()==PlaceOperator,"Imported inventory or ownership differs from admission.");
                        // CC0 samples can be copied, never edited in place by visitors.
                        // Creator attribution/CreatorID and original license sidecars are retained.
                        part.BaseMask=(uint)Permissions.All;part.OwnerMask=(uint)Permissions.All;
                        part.EveryoneMask=(uint)Permissions.Copy;part.GroupMask=0;
                        part.ObjectSaleType=0;part.SalePrice=0;
                        if(part.ClickAction is (byte)ClickAction.Buy or (byte)ClickAction.Pay)part.ClickAction=(byte)ClickAction.Touch;
                        part.NextOwnerMask=(uint)(Permissions.Copy|Permissions.Modify|Permissions.Transfer);
                    }
                    group.HasGroupChanged=true;scene.SimulationDataService.StoreObject(group,new UUID(RegionId));
                }
                foreach(var parcel in scene.LandChannel.AllParcels())
                {
                    var land=parcel.LandData;land.OwnerID=new UUID(PlaceOperator);land.GroupID=UUID.Zero;land.IsGroupOwned=false;
                    land.Flags&=~(uint)(ParcelFlags.CreateObjects|ParcelFlags.CreateGroupObjects|ParcelFlags.AllowTerraform);
                    scene.SimulationDataService.StoreLandObject(parcel);
                }
                LibraryRecord("committed");libraryRecovery=false;
                log.Info(JsonSerializer.Serialize(new{@event="library_import_committed",region_id=RegionId,libraryImport.id,libraryImport.expected_parts}));
            }
            catch(Exception ex){LibraryRecord("failed",ex.GetType().Name);log.Error(JsonSerializer.Serialize(new{@event="library_import_failed",region_id=RegionId,error=ex.Message,loaded_regions=loaded.Select(x=>x.ToString()).ToArray(),native_message_present=!string.IsNullOrEmpty(message),recovery_required=true}));}
        }
    }
}
