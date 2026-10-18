// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Nini.Config;
using OpenMetaverse;
namespace Finalverse.World;

public sealed partial class WorldModule
{
    WorldLibrary? library;string libraryRoot="";bool libraryScriptsDisabled;
    void InitialiseLibrary(IConfigSource source)
    {
        string path=source.Configs["MutSeaLibrary"]?.GetString("Catalog","")??"";
        if(path=="")return;
        var file=new FileInfo(path);
        if(!Path.IsPathRooted(path)||!file.Exists||(file.Attributes&FileAttributes.ReparsePoint)!=0||file.Length>65536)
            throw new InvalidDataException("World Library requires a bounded, local operator catalog.");
        libraryRoot=Path.Combine(Path.GetDirectoryName(path)!,"imports");
        libraryScriptsDisabled=source.Configs["YEngine"]?.GetBoolean("Enabled",true)==false&&source.Configs["XEngine"]?.GetBoolean("Enabled",true)==false;
        library=new WorldLibrary(File.ReadAllBytes(path));
    }
    object HumanDestinations(string actor)=>new{
        schema_version=1,region_id=RegionId,actor_id=actor,
        destinations=(library?.Destinations??[]).Select(d=>{
            var region=scene.GridService.GetRegionByUUID(scene.RegionInfo.ScopeID,new UUID(d.region_id));
            int flags=region==null?0:scene.GridService.GetRegionFlags(scene.RegionInfo.ScopeID,new UUID(d.region_id));
            return new{d.id,d.title,d.description,d.region_id,d.spawn,d.license,d.attribution,d.source,d.remix_policy,
                online=region!=null&&flags>=0&&(flags&4)!=0&&(flags&(32|128|512))==0&&LibraryReady(d),
                global_origin=region==null?null:new[]{region.RegionLocX,region.RegionLocY},
                workspace=d.workspace!=null};
        }).ToArray()
    };
    object? WorkspaceContext()=>library?.Find(RegionId) is {workspace:not null} d?new{
        id=d.id,title=d.title,zones=d.workspace,
        privacy="Only your explicit AI request and the inspected world context are sent to the configured planner. Nearby chat is not captured.",
        authority="AI proposes; your authenticated world permissions and reviewed WorldAction govern changes."
    }:null;
}
