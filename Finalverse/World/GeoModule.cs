// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Globalization;
using System.Text.Json;
using Nini.Config;
using OpenMetaverse;
using MutSea.Framework.Servers;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.Framework.Scenes;

namespace Finalverse.World;

// Geographic world binding. Private configuration:
//   [FinalverseGeography]
//   Enabled = true
//   WorldId = <world UUID>                ; explicit and stable; never derived from a grid or region name
//   Candidates = /config/geo              ; operator-reviewed <version>.binding.json, .correspondence.json, .terrain.r32
//   Issue = <region UUID>@<version>       ; optional: issue, or revise to, that candidate in that region
//   Retire = <region UUID>@<version>      ; optional, alone: retire the active binding and restore the pre-issue ground
//   LoginURI = http://127.0.0.1:19070/    ; advertised by discovery; credentials are never part of an entry
// Public discovery (GET, read-only, no identities, objects or capabilities):
//   /finalverse/geo/v1/worlds   /finalverse/geo/v1/entry?latitude=&longitude=   /finalverse/geo/v1/entry?spatialdb_entity=<uuid>
// Session-bound World API (read-only): geo_binding, geo_locate, geo_resolve, geo_entity, geo_enter.
// Live objects still change only through WorldKernel/WorldLine; no binding operation is reachable from a client.
public sealed partial class WorldModule : IGeoRegion
{
    bool geoEnabled;string geoWorld="",geoCandidates="",geoIssue="",geoRetire="",geoLogin="";
    GeoBindingKernel? geo;
    (DateTimeOffset at,bool matches)? geoTerrainCheck;
    static readonly Dictionary<string,WorldModule> geoRegions=new();
    static readonly object geoRegistry=new();
    static bool geoHandlers;static long geoWindow;static int geoWindowCount;
    const string GeoPath="/finalverse/geo/v1";

    void InitialiseGeography(IConfigSource source)
    {
        var c=source.Configs["FinalverseGeography"];
        geoEnabled=c?.GetBoolean("Enabled",false)??false;if(!geoEnabled)return;
        geoWorld=c!.GetString("WorldId","");geoCandidates=c.GetString("Candidates","/config/geo");
        geoIssue=c.GetString("Issue","");geoRetire=c.GetString("Retire","");geoLogin=c.GetString("LoginURI","");
        static bool Pin(string s)=>s==""||s.Split('@') is [var r,var v]&&Guid.TryParse(r,out var g)&&g.ToString()==r&&v.Length==64&&v.All(ch=>ch is >='0' and <='9' or >='a' and <='f');
        if(!Guid.TryParse(geoWorld,out var w)||w==Guid.Empty||w.ToString()!=geoWorld||!Path.IsPathRooted(geoCandidates)||geoIssue!=""&&geoRetire!=""||!Pin(geoIssue)||!Pin(geoRetire)
           ||!Uri.TryCreate(geoLogin,UriKind.Absolute,out var login)||login.Scheme is not ("http" or "https")||login.UserInfo!=""||login.Query!=""||login.Fragment!="")
            throw new InvalidDataException("Geography needs an explicit world UUID, a candidate directory, one Issue or Retire <region UUID>@<version> and the advertised login URI.");
    }

    void StartGeography()
    {
        if(!geoEnabled)return;
        try{geo=new GeoBindingKernel(this,directory,geoWorld);}
        catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="geo_binding_unavailable",region_id=RegionId,exception_type=ex.GetType().Name,error=ex.Message}));return;}
        lock(geoRegistry)
        {
            geoRegions[RegionId]=this;
            if(!geoHandlers)
            {
                MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler(GeoPath+"/worlds",GeoDiscovery));
                MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler(GeoPath+"/entry",GeoDiscovery));
                geoHandlers=true;
            }
        }
        if(geo.Active is {} a)log.Info(JsonSerializer.Serialize(new{@event="geo_binding_active",region_id=RegionId,a.binding.revision,a.binding.version,recovery_required=geo.RecoveryRequired}));
    }

    void StopGeography(){lock(geoRegistry)if(geoRegions.TryGetValue(RegionId,out var m)&&ReferenceEquals(m,this))geoRegions.Remove(RegionId);}

    void GeographyPrimsLoaded(Scene s)
    {
        if(geo==null)return;
        lock(gate)
        {
            try
            {
                if(geoRetire.Split('@') is [var retireRegion,var retireVersion]&&retireRegion==RegionId)
                {
                    if(geo.Active?.binding.version!=retireVersion)return;
                    var retired=geo.Retire(retireVersion);
                    log.Info(JsonSerializer.Serialize(new{@event="geo_binding_retired",region_id=RegionId,retired.operation_id,retired.version,active=geo.Active?.binding.version}));
                    return;
                }
                if(geoIssue.Split('@') is not [var region,var version]||region!=RegionId)return;
                if(geo.Active?.binding.version==version){log.Info(JsonSerializer.Serialize(new{@event="geo_binding_already_active",region_id=RegionId,version}));return;}
                byte[] Candidate(string suffix)
                {
                    var info=new FileInfo(Path.Combine(geoCandidates,version+suffix));
                    ContentCatalog.Require(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)==0&&info.Length<=64*1024*1024,"Binding candidate "+suffix+" is missing, linked or too large.");
                    return File.ReadAllBytes(info.FullName);
                }
                var issued=geo.Issue(Candidate(".binding.json"),Candidate(".correspondence.json"),Candidate(".terrain.r32"));
                geoTerrainCheck=null;
                log.Info(JsonSerializer.Serialize(new{@event="geo_binding_issued",region_id=RegionId,issued.operation_id,issued.revision,issued.version,issued.previous_version,terrain_changed=issued.terrain_before_sha256!=ContentCatalog.Hash(TerrainBytes())}));
            }
            catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="geo_binding_failed",region_id=RegionId,exception_type=ex.GetType().Name,error=ex.Message,recovery_required=geo.RecoveryRequired}));}
        }
    }

    // ---- session-bound World API
    object HumanGeography(JsonElement data,UUID agent,Guid session,string op)
    {
        lock(gate)
        {
            ContentCatalog.Require(HasWorldSession(agent,session),"Your region session is no longer active.");
            if(lastExperienceRequest.TryGetValue(agent,out var last)&&DateTimeOffset.UtcNow-last<TimeSpan.FromMilliseconds(100))throw new InvalidDataException("Please wait briefly before the next request.");
            lastExperienceRequest[agent]=DateTimeOffset.UtcNow;
            var a=geo?.Active;ContentCatalog.Require(a!=null,"This region has no geographic binding.");
            string actor=agent.ToString();
            switch(op)
            {
                case "geo_binding":
                    GeoFields(data);
                    return new{schema_version="mutsea.geographic-binding-view/v1",binding=a!.binding,
                        earth=new{world_binding=GeoBindings.CoreBinding(a.binding),world_entry=GeoBindings.CoreEntry(a.binding,a.transform.Geographic(a.binding.entry.position))},
                        terrain_matches_issued=GeoTerrainMatches(),recovery_required=geo!.RecoveryRequired,history=geo.History(),correspondence_features=a.correspondence.Count};
                case "geo_locate":
                {
                    GeoFields(data,"entity_id");
                    if(GeoText(data,"entity_id") is not {} id)return GeoPlace(a!,Avatar(actor),actor);
                    ContentCatalog.Require(Guid.TryParse(id,out _)&&Find(id,actor) is {},"No live object with that identity in this region.");
                    return GeoPlace(a!,Find(id,actor)!.position,id);
                }
                case "geo_resolve":
                    GeoFields(data,"latitude","longitude","spatialdb_entity");
                    return GeoTarget(a!,data);
                case "geo_entity":
                {
                    GeoFields(data,"entity_id");string id=GeoText(data,"entity_id")??"";
                    ContentCatalog.Require(Guid.TryParse(id,out var g)&&g.ToString()==id,"Expected an entity UUID.");
                    if(Find(id,actor) is {} live)return GeoPlace(a!,live.position,id);
                    var f=a!.correspondence.Get(id);ContentCatalog.Require(f!=null,"Neither a live object here nor a published feature of this binding.");
                    var standing=a.correspondence.StandingNear(f!,SizeX,SizeY,GeoGround,WaterHeight);
                    return new{authority="spatialdb",feature=GeoCorrespondence.Summary(f!),binding_version=a.binding.version,publication_version=a.binding.publication.version,
                        region_centroid=GeoCorrespondence.Centroid(f!),region_min=f!.region_min,region_max=f.region_max,
                        geographic_centroid=a.transform.Geographic([..GeoCorrespondence.Centroid(f),GeoGround(GeoCorrespondence.Centroid(f)[0],GeoCorrespondence.Centroid(f)[1])]),
                        standing,live_objects_on=Entities(actor).Where(e=>a.correspondence.Covers(f,e.position[0],e.position[1])).Select(e=>new{e.id,address=GeoBindings.Address(e.id),e.name,e.owner_id}).Take(64).ToArray()};
                }
                case "geo_enter":
                {
                    GeoFields(data,"binding_version","latitude","longitude","spatialdb_entity");
                    ContentCatalog.Require(GeoText(data,"binding_version")==a!.binding.version,"The geographic binding changed since discovery. Discover again.");
                    var target=GeoResolve(a,data);ContentCatalog.Require(target.inside,"That place is outside this region.");
                    var at=Avatar(actor);double error=Math.Sqrt(Math.Pow(at[0]-target.local[0],2)+Math.Pow(at[1]-target.local[1],2));
                    var receipt=new{schema_version="mutsea.geographic-entry-receipt/v1",status=error<=3?"arrived":"elsewhere",actor_id=actor,region_id=RegionId,binding_version=a.binding.version,
                        target=new{geographic=a.transform.Geographic(target.local),region_local=target.local,spatialdb_entity=target.feature?.entity_id},
                        arrival=new{region_local=at,geographic=a.transform.Geographic(at)},horizontal_error_metres=error};
                    var record=geo!.RecordEntry(actor,receipt);
                    log.Info(JsonSerializer.Serialize(new{@event="geo_entry",region_id=RegionId,actor_id=actor,receipt.status,record.operation_id,horizontal_error_metres=Math.Round(error,3)}));
                    return new{receipt,record.operation_id};
                }
                default:throw new InvalidDataException("Unknown World API operation.");
            }
        }
    }

    object GeoPlace(GeoActive a,double[] local,string subject)
    {
        var g=a.transform.Geographic(local);
        return new{authority="mutsea",entity_id=subject,address=GeoBindings.Address(subject),binding_version=a.binding.version,region_id=RegionId,region_local=local,geographic=g,
            publication_frame=a.transform.Publication(local),ground_metres=GeoGround(local[0],local[1]),on=a.correspondence.At(local[0],local[1]).Select(GeoCorrespondence.Summary).ToArray(),
            return_destination=GeoBindings.Destination(g)};
    }

    (bool inside,double[] local,GeoFeature? feature) GeoResolve(GeoActive a,JsonElement data)
    {
        if(GeoText(data,"spatialdb_entity") is {} id)
        {
            ContentCatalog.Require(!data.TryGetProperty("latitude",out _)&&!data.TryGetProperty("longitude",out _),"Resolve either a feature or a position.");
            var f=a.correspondence.Get(id);ContentCatalog.Require(f!=null,"That published feature is not part of this binding.");
            var stand=a.correspondence.StandingNear(f!,SizeX,SizeY,GeoGround,WaterHeight);ContentCatalog.Require(stand!=null,"No dry place to stand beside that feature.");
            return (true,stand!,f);
        }
        double lat=data.GetProperty("latitude").GetDouble(),lon=data.GetProperty("longitude").GetDouble();
        GeoFrames.Validate(new(lon,lat,0));
        var local=a.transform.OnGround(lat,lon,GeoGround);
        return (a.transform.Inside(local[0],local[1])&&local[0]>=1&&local[1]>=1&&local[0]<=SizeX-1&&local[1]<=SizeY-1,local,null);
    }

    object GeoTarget(GeoActive a,JsonElement data)
    {
        var (inside,local,feature)=GeoResolve(a,data);
        if(!inside)return new{inside=false,binding_version=a.binding.version,region_local=local};
        var g=a.transform.Geographic(local);
        return new{inside=true,binding_version=a.binding.version,region_local=local,geographic=g,feature=feature==null?null:GeoCorrespondence.Summary(feature),
            on=a.correspondence.At(local[0],local[1]).Select(GeoCorrespondence.Summary).ToArray(),above_water=local[2]>WaterHeight+.1,
            world_entry=GeoBindings.CoreEntry(a.binding,g,feature?.address),login=new{login_uri=geoLogin,start=GeoStart(local)}};
    }

    // Bilinear over the authoritative heightmap; the kernel's floor sampling is kept for its own policy.
    double GeoGround(double x,double y)
    {
        x=Math.Clamp(x,0,SizeX-1.000001);y=Math.Clamp(y,0,SizeY-1.000001);int i=(int)x,j=(int)y;double u=x-i,v=y-j;var h=scene.Heightmap;
        return h[i,j]*(1-u)*(1-v)+h[i+1,j]*u*(1-v)+h[i,j+1]*(1-u)*v+h[i+1,j+1]*u*v;
    }

    // The native login start location: the session, not this string, carries authority.
    string GeoStart(double[] local)=>string.Create(CultureInfo.InvariantCulture,$"uri:{RegionName}&{(int)Math.Round(local[0])}&{(int)Math.Round(local[1])}&{(int)Math.Ceiling(local[2]+1)}");

    bool GeoTerrainMatches()
    {
        if(geoTerrainCheck is {} c&&DateTimeOffset.UtcNow-c.at<TimeSpan.FromSeconds(30))return c.matches;
        bool matches=geo?.TerrainMatchesIssued()==true;geoTerrainCheck=(DateTimeOffset.UtcNow,matches);return matches;
    }

    static void GeoFields(JsonElement data,params string[] allowed)=>ContentCatalog.Require(data.EnumerateObject().All(p=>p.Name=="op"||allowed.Contains(p.Name)),"Unexpected geographic request fields.");
    static string? GeoText(JsonElement data,string name)=>data.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;

    // ---- public discovery
    object? GeoWorld()
    {
        lock(gate)
        {
            var a=geo?.Active;if(a==null)return null;var b=a.binding;
            return new{world_binding=GeoBindings.CoreBinding(b),binding_id=b.binding_id,binding_version=b.version,revision=b.revision,region_name=RegionName,
                world_entry=GeoBindings.CoreEntry(b,a.transform.Geographic(b.entry.position)),login=new{login_uri=geoLogin,start=GeoStart(b.entry.position)},
                coverage=new[]{new[]{0d,0},[SizeX,0],[SizeX,SizeY],[0,SizeY]}.Select(p=>a.transform.Geographic([p[0],p[1],0])).ToArray(),
                publication=new{b.publication.graph_id,b.publication.version,b.publication.address,b.publication.frame_id},
                region_to_ecef=b.transform.region_to_ecef,region_to_publication=b.transform.region_to_publication,attribution=b.attribution,terrain_matches_issued=GeoTerrainMatches()};
        }
    }

    static void GeoDiscovery(IOSHttpRequest request,IOSHttpResponse response)
    {
        response.ContentType="application/json";response.AddHeader("Access-Control-Allow-Origin","*");response.AddHeader("Cache-Control","no-cache");
        int status=200;object body;
        try
        {
            if(request.HttpMethod=="OPTIONS"){response.AddHeader("Access-Control-Allow-Methods","GET");response.StatusCode=204;response.RawBuffer=[];return;}
            if(request.HttpMethod!="GET"){response.StatusCode=405;response.RawBuffer=JsonSerializer.SerializeToUtf8Bytes(new{ok=false,error="Discovery is read-only."});return;}
            WorldModule[] regions;
            lock(geoRegistry)
            {
                long second=Environment.TickCount64/1000;if(second!=geoWindow){geoWindow=second;geoWindowCount=0;}
                if(++geoWindowCount>50){response.StatusCode=429;response.RawBuffer=JsonSerializer.SerializeToUtf8Bytes(new{ok=false,error="Discovery is busy; retry shortly."});return;}
                regions=geoRegions.Values.OrderBy(r=>r.RegionId,StringComparer.Ordinal).ToArray();
            }
            if(request.UriPath.TrimEnd('/').EndsWith("/worlds",StringComparison.Ordinal))
                body=new{ok=true,schema_version="mutsea.geographic-discovery/v1",worlds=regions.Select(r=>r.GeoWorld()).Where(w=>w!=null).ToArray()};
            else
            {
                var q=request.QueryAsDictionary;
                ContentCatalog.Require(q.Keys.All(k=>k is "latitude" or "longitude" or "spatialdb_entity")&&(q.ContainsKey("spatialdb_entity")?q.Count==1:q.ContainsKey("latitude")&&q.ContainsKey("longitude")&&q.Count==2),
                    "Ask for latitude and longitude, or one spatialdb_entity.");
                bool byEntity=q.TryGetValue("spatialdb_entity",out var id);
                object input=byEntity?(object)new{spatialdb_entity=id}:new{latitude=GeoNumber(q["latitude"]),longitude=GeoNumber(q["longitude"])};
                var data=JsonSerializer.SerializeToElement(input);object? found=null;
                foreach(var r in regions)
                {
                    lock(r.gate)
                    {
                        if(r.geo?.Active is not {} a)continue;
                        if(byEntity&&a.correspondence.Get(id!)==null)continue;
                        var (inside,local,_)=r.GeoResolve(a,data);if(!inside)continue;
                        found=new{ok=true,schema_version="mutsea.geographic-entry/v1",region_name=r.RegionName,binding_id=a.binding.binding_id,binding_version=a.binding.version,
                            world_binding=GeoBindings.CoreBinding(a.binding),target=r.GeoTarget(a,data)};
                    }
                    if(found!=null)break;
                }
                if(found==null){status=404;body=new{ok=false,error="No bound world covers that place."};}
                else body=found;
            }
        }
        catch(Exception ex) when(ex is InvalidDataException or FormatException or KeyNotFoundException or InvalidOperationException or JsonException)
        {status=400;body=new{ok=false,error=ex.Message};}
        catch(Exception){status=500;body=new{ok=false,error="Geographic discovery failed."};}
        response.StatusCode=status;response.RawBuffer=JsonSerializer.SerializeToUtf8Bytes(body);
    }

    static double GeoNumber(string s)=>double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v)?v:throw new InvalidDataException("Coordinates must be finite decimal degrees.");

    // ---- IGeoRegion
    bool IGeoRegion.HasLiveObjects()=>scene.GetSceneObjectGroups().Any(g=>!g.IsDeleted&&!g.IsAttachment);
    string IGeoRegion.TerrainHashFormat=>((IWorldTemplateRegion)this).TerrainHashFormat;
    string IGeoRegion.TerrainHash()=>((IWorldTemplateRegion)this).TerrainHash();
    string IGeoRegion.SnapshotTerrain(string path)=>((IWorldTemplateRegion)this).SnapshotTerrain(path);
    byte[] IGeoRegion.TerrainR32()=>TerrainBytes();
    void IGeoRegion.LoadTerrain(byte[] r32)=>((IWorldTemplateRegion)this).LoadTerrain(r32);
    void IGeoRegion.SetWaterHeight(double height)=>((IWorldTemplateRegion)this).SetWaterHeight(height);
}
