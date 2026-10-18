// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using Nini.Config;
using OpenMetaverse;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;

namespace Finalverse.World;

// Operator world-template provisioning into an empty region, and terrain export for
// authoring templates from an existing region. Private configuration:
//   [FinalverseWorlds]
//   Enabled = true
//   HostRegion = <target region UUID>
//   Catalog = /config/worlds
//   Provision = world_id@version        ; or, alone: Rollback = <world operation id>
//   ExportRegion = <region UUID>        ; optional: write that region's terrain as R32
//   ExportTerrain = /data/finalverse/<name>.r32
public sealed partial class WorldModule : IWorldTemplateRegion
{
    bool worldsEnabled;string worldsHost="",worldsCatalog="",worldsProvision="",worldsRollback="",exportRegion="",exportTerrain="";
    WorldTemplateKernel? worlds;PlaceKernel? worldPlaces;

    void InitialiseWorlds(IConfigSource source)
    {
        var c=source.Configs["FinalverseWorlds"];
        worldsEnabled=c?.GetBoolean("Enabled",false)??false;
        if(!worldsEnabled)return;
        worldsHost=c!.GetString("HostRegion","");worldsCatalog=c.GetString("Catalog","/config/worlds");
        worldsProvision=c.GetString("Provision","");worldsRollback=c.GetString("Rollback","");
        exportRegion=c.GetString("ExportRegion","");exportTerrain=c.GetString("ExportTerrain","");
        if(worldsHost!=""&&!Guid.TryParse(worldsHost,out _)||worldsProvision!=""&&worldsProvision.Split('@').Length!=2||worldsProvision!=""&&worldsRollback!=""||
           worldsRollback!=""&&!Guid.TryParse(worldsRollback,out _)||exportRegion!=""&&(!Guid.TryParse(exportRegion,out _)||!Path.IsPathRooted(exportTerrain)||!exportTerrain.EndsWith(".r32")))
            throw new InvalidDataException("Worlds need a host region, one provision (id@version) or one rollback, and an absolute .r32 export path.");
    }

    void StartWorlds(ContentCatalog catalog)
    {
        if(!worldsEnabled)return;
        if(library?.Find(RegionId)?.template is {} provision&&RegionId!=worldsHost)
        {worldsHost=RegionId;worldsProvision=provision;worldsRollback="";}
        if(RegionId!=worldsHost)return;
        var placeCatalog=new PlaceCatalog(placesCatalog==""?"/config/places":placesCatalog,catalog);
        worldPlaces=places??new PlaceKernel(this,catalog,placeCatalog,directory,RegionId);
        worlds=new WorldTemplateKernel(this,new WorldTemplateCatalog(worldsCatalog,placeCatalog),directory);
    }

    void WorldsPrimsLoaded(Scene s)
    {
        if(!worldsEnabled)return;
        lock(gate)
        {
            if(RegionId==exportRegion)
            {
                try{SaveR32(exportTerrain);log.Info(JsonSerializer.Serialize(new{@event="world_terrain_exported",path=exportTerrain,sha256=ContentCatalog.Hash(File.ReadAllBytes(exportTerrain)),water_height=WaterHeight}));}
                catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="world_terrain_export_failed",error=ex.Message}));}
            }
            if(worlds==null)return;
            try
            {
                if(worldsRollback!="")
                {
                    if(!worlds.Entries.Any(e=>e.operation_id==worldsRollback&&e.status=="rolled_back"))
                    {var done=worlds.Rollback(worldsRollback);log.Info(JsonSerializer.Serialize(new{@event="world_rolled_back",done.operation_id,done.world_id,done.version}));}
                    return;
                }
                if(worldsProvision=="")return;
                var at=worldsProvision.Split('@');
                if(worlds.Active(at[0],at[1]) is {} active){log.Info(JsonSerializer.Serialize(new{@event="world_already_provisioned",world_id=at[0],version=at[1],active.operation_id}));return;}
                var result=worlds.Provision(at[0],at[1]);
                log.Info(JsonSerializer.Serialize(new{@event="world_provisioned",result.operation_id,result.world_id,result.version,places=result.place_plans.Length,region=RegionId}));
            }
            catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="world_provisioning_failed",exception_type=ex.GetType().Name,error=ex.Message,recovery_required=worlds.RecoveryRequired}));}
        }
    }

    // ---- IWorldTemplateRegion
    PlaceKernel IWorldTemplateRegion.Places=>worldPlaces??throw new InvalidDataException("World places are unavailable.");
    string IWorldTemplateRegion.Operator=>PlaceOperator;
    bool IWorldTemplateRegion.IsEmpty()=>!scene.GetSceneObjectGroups().Any(g=>!g.IsDeleted&&!g.IsAttachment);
    bool IWorldTemplateRegion.IsEmptyExcept(string[] rootIds)
    {
        var allowed=rootIds.ToHashSet(StringComparer.Ordinal);
        return !scene.GetSceneObjectGroups().Any(g=>!g.IsDeleted&&!g.IsAttachment&&!allowed.Contains(g.UUID.ToString()));
    }
    double IWorldTemplateRegion.Ground(double x,double y)=>Height(x,y);
    string IWorldTemplateRegion.TerrainHashFormat=>"mutsea-variable2dgzip-r32-v1";
    string IWorldTemplateRegion.TerrainHash()
    {
        // TerrainData's inherited Variable2DGzip serializer rounds heights to
        // 3 decimals. Fingerprint the exact persisted representation through
        // its own roundtrip, not raw source floats or an invented tolerance.
        var terrain=scene.Heightmap.GetTerrainData();
        ContentCatalog.Require(terrain.GetDatabaseBlob(out var revision,out var blob)&&blob is byte[],"Native terrain serialization failed.");
        var persisted=MutSea.Framework.TerrainData.CreateFromDatabaseBlobFactory((int)SizeX,(int)SizeY,(int)MutSea.Framework.Constants.RegionHeight,revision,(byte[])blob);
        ContentCatalog.Require(persisted!=null,"Native terrain roundtrip failed.");
        var values=persisted!.GetFloatsSerialized();var bytes=new byte[values.Length*4];
        for(int i=0;i<values.Length;i++)BitConverter.GetBytes(values[i]).CopyTo(bytes,i*4);
        return ContentCatalog.Hash(bytes);
    }
    string IWorldTemplateRegion.SnapshotTerrain(string path){SaveR32(path);return ContentCatalog.Hash(File.ReadAllBytes(path));}
    byte[] TerrainBytes()
    {
        int w=(int)SizeX,h=(int)SizeY;var bytes=new byte[w*h*4];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)BitConverter.GetBytes(scene.Heightmap[x,y]).CopyTo(bytes,(y*w+x)*4);
        return bytes;
    }
    void SaveR32(string path)
    {
        var bytes=TerrainBytes();
        using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write)){f.Write(bytes);f.Flush(true);}
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }
    void IWorldTemplateRegion.LoadTerrain(byte[] r32)
    {
        int w=(int)SizeX,h=(int)SizeY;ContentCatalog.Require(r32.Length==w*h*4,"Terrain size differs from the region.");
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)scene.Heightmap[x,y]=BitConverter.ToSingle(r32,(y*w+x)*4);
        scene.SaveTerrain();scene.RequestModuleInterface<ITerrainModule>()?.TaintTerrain();
    }
    void IWorldTemplateRegion.SetWaterHeight(double height){scene.RegionInfo.RegionSettings.WaterHeight=height;scene.RegionInfo.RegionSettings.Save();}
    void IWorldTemplateRegion.SetLanding(double[] position,double[] lookAt)
    {
        var land=scene.LandChannel?.GetLandObject((float)position[0],(float)position[1]);
        ContentCatalog.Require(land!=null,"World landing parcel is unavailable.");
        land!.LandData.UserLocation=new Vector3((float)position[0],(float)position[1],(float)position[2]);
        var facing=new Vector3((float)(lookAt[0]-position[0]),(float)(lookAt[1]-position[1]),0);
        land.LandData.UserLookAt=Vector3.Normalize(in facing);
        land.LandData.LandingType=(byte)LandingType.LandingPoint;
        scene.SimulationDataService.StoreLandObject(land);
    }

    WorldLanding IWorldTemplateRegion.CaptureLanding(double[] position)
    {
        var land=scene.LandChannel?.GetLandObject((float)position[0],(float)position[1]);
        ContentCatalog.Require(land!=null,"World landing parcel is unavailable.");
        return Landing(land!.LandData);
    }
    static WorldLanding Landing(MutSea.Framework.LandData land)=>new(land.GlobalID.ToString(),
        [land.UserLocation.X,land.UserLocation.Y,land.UserLocation.Z],
        [land.UserLookAt.X,land.UserLookAt.Y,land.UserLookAt.Z],land.LandingType);
    bool IWorldTemplateRegion.MatchesLanding(WorldLanding expected)
    {
        var land=scene.LandChannel?.AllParcels().SingleOrDefault(p=>p.LandData.GlobalID.ToString()==expected.parcel_id);
        return land!=null&&JsonSerializer.Serialize(Landing(land.LandData))==JsonSerializer.Serialize(expected);
    }
    void IWorldTemplateRegion.RestoreLanding(WorldLanding before)
    {
        var land=scene.LandChannel?.AllParcels().SingleOrDefault(p=>p.LandData.GlobalID.ToString()==before.parcel_id);
        ContentCatalog.Require(land!=null,"Original landing parcel is unavailable.");
        land!.LandData.UserLocation=new Vector3((float)before.position[0],(float)before.position[1],(float)before.position[2]);
        land.LandData.UserLookAt=new Vector3((float)before.look_at[0],(float)before.look_at[1],(float)before.look_at[2]);
        land.LandData.LandingType=before.landing_type;
        scene.SimulationDataService.StoreLandObject(land);
    }
}
