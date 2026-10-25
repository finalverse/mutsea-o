// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finalverse.World;

// Geographic world binding (mutsea.geographic-binding/v1): MutSea's authoritative, versioned
// statement of where one live region sits on the Earth. SpatialDB owns the publication it is
// calibrated against, MutSea owns the region and its live state, and Earth only consumes the
// projections below. Nothing here is inferred from a name, an advisory anchor or a resource.
//
// Transform, exact in f64:  ECEF(p) = ECEF(anchor) + ENU(anchor)ᵀ · scale · R(heading) · p
//   p        region-local metres: +X, +Y horizontal at the anchor, +Z up
//   anchor   WGS84 position of region-local (0,0,0)
//   heading  clockwise angle from true north at the anchor to region +Y; +X is 90° clockwise of +Y
//   scale    Earth metres per region metre
public sealed record GeoRegion(string region_id,string name,int[] size_metres);
public sealed record GeoTransform(string model,string datum,Geodetic anchor,string[] anchor_frame_axes,string unit,double heading_degrees,double scale,
    double[] region_to_ecef,double[] region_to_publication);
public sealed record GeoVertical(string region_z,string approximates,double geoid_undulation_metres,string geoid_model,string geoid_grid_sha256,double max_deviation_metres);
public sealed record GeoPublication(string authority,string graph_id,string version,string root_sha256,string root_entity_id,string address,
    string frame_id,Geodetic frame_origin,string[] frame_axes,string geography_sha256,string license);
public sealed record GeoControlPoint(string id,string role,Geodetic geographic,double[] publication_frame,double[] region_local);
public sealed record GeoCalibration(string method,double tolerance_metres,double centre_residual_metres,double grid_alignment_residual_degrees,
    double published_corner_offset_metres,GeoControlPoint[] control_points);
public sealed record GeoTerrain(string derivation,string[] source_assets,string r32_sha256,int[] grid,double spacing_metres,double minimum_metres,double maximum_metres,
    double water_height_metres,int extended_samples,double max_extension_metres,double max_surface_residual_metres,double rms_surface_residual_metres,double[][] published_outline);
public sealed record GeoCorrespondencePin(string schema_version,string sha256,int feature_count,string method);
public sealed record GeoEntry(double[] position,double[] look_at);
public sealed record GeoAttribution(string source_id,string provider,string license,string attribution);
public sealed record GeographicBinding(string schema_version,string binding_id,int revision,string? previous_version,string authority,string world_id,
    GeoRegion region,GeoTransform transform,GeoVertical vertical,GeoPublication publication,GeoCalibration calibration,GeoTerrain terrain,
    GeoCorrespondencePin correspondence,GeoEntry entry,GeoAttribution[] attribution,string version);

public static class GeoBindings
{
    public const string Schema="mutsea.geographic-binding/v1",Model="wgs84-local-tangent-similarity/v1",CorrespondenceSchema="mutsea.geographic-correspondence/v1";
    public const double Tolerance=.001;
    public static readonly string[] EnuAxes=["east","north","up"],PublicationAxes=["east","up","south"];

    // The version is SHA-256 over the RFC 8785 bytes of every member except `version`.
    public static string ComputeVersion(GeographicBinding b)
    {
        var node=JsonSerializer.SerializeToNode(b)!.AsObject();node.Remove("version");
        return ContentCatalog.Hash(CanonicalJson.Bytes(JsonSerializer.SerializeToElement(node)));
    }
    public static GeographicBinding Seal(GeographicBinding b)=>b with{version=ComputeVersion(b)};
    public static string TransformSha(GeoTransform t)=>CanonicalJson.Sha256(t);

    // One binding identity per (world, region), stable across every revision.
    public static string BindingId(string world,string region)
    {
        var h=SHA256.HashData(Encoding.UTF8.GetBytes(Schema+"\n"+world.ToLowerInvariant()+"\n"+region.ToLowerInvariant()));
        h[6]=(byte)((h[6]&0x0f)|0x50);h[8]=(byte)((h[8]&0x3f)|0x80);
        return string.Concat(Convert.ToHexString(h,0,4),"-",Convert.ToHexString(h,4,2),"-",Convert.ToHexString(h,6,2),"-",Convert.ToHexString(h,8,2),"-",Convert.ToHexString(h,10,6)).ToLowerInvariant();
    }

    public static double[] RegionToEcef(Geodetic anchor,double headingDegrees,double scale)
    {
        double h=GeoFrames.Rad(headingDegrees),c=Math.Cos(h)*scale,s=Math.Sin(h)*scale;
        return GeoFrames.TangentToEcef(anchor,[[c,-s,0],[s,c,0],[0,0,scale]]);
    }
    // SpatialDB's glTF tangent frame: +x east, +y up, +z south.
    public static double[] PublicationToEcef(Geodetic origin)=>GeoFrames.TangentToEcef(origin,[[1,0,0],[0,0,1],[0,-1,0]]);
    public static double[] RegionToPublication(Geodetic anchor,double headingDegrees,double scale,Geodetic publicationOrigin)=>
        GeoFrames.Multiply(GeoFrames.InverseSimilarity(PublicationToEcef(publicationOrigin)),RegionToEcef(anchor,headingDegrees,scale));

    static bool Hex(string? s)=>s is {Length:64}&&s.All(c=>c is >='0' and <='9' or >='a' and <='f');
    static bool Id(string? s)=>Guid.TryParse(s,out var g)&&g!=Guid.Empty&&s==g.ToString();
    static bool Text(string? s,int max)=>s is {Length:>0}&&s.Length<=max&&!s.Any(char.IsControl);
    static bool Same(double[] a,double[] b,double rotation,double translation)=>a.Length==16&&a.All(double.IsFinite)&&
        Enumerable.Range(0,16).All(i=>Math.Abs(a[i]-b[i])<=(i is 12 or 13 or 14?translation:rotation));

    // Full structural, numeric and self-consistency validation against the live region.
    public static GeoTransformer Validate(GeographicBinding b,string regionId,double sizeX,double sizeY)
    {
        ContentCatalog.Require(b!=null&&b.schema_version==Schema&&b.authority=="mutsea","Unsupported geographic binding.");
        ContentCatalog.Require(Id(b!.world_id)&&Id(b.region.region_id)&&b.region.region_id==regionId&&b.binding_id==BindingId(b.world_id,regionId),"Binding identity does not match this world and region.");
        ContentCatalog.Require(b.revision is >=1 and <=100000&&(b.revision==1?b.previous_version==null:Hex(b.previous_version)),"Invalid binding revision chain.");
        ContentCatalog.Require(Text(b.region.name,64)&&b.region.size_metres is [var sx,var sy]&&sx==sizeX&&sy==sizeY,"Binding region size differs from the live region.");
        var t=b.transform;
        ContentCatalog.Require(t.model==Model&&t.datum=="wgs84"&&t.unit=="metre"&&t.anchor_frame_axes.SequenceEqual(EnuAxes),"Binding requires the explicit WGS84 ENU similarity model.");
        GeoFrames.Validate(t.anchor);
        ContentCatalog.Require(double.IsFinite(t.heading_degrees)&&t.heading_degrees>=0&&t.heading_degrees<360&&double.IsFinite(t.scale)&&t.scale>0&&t.scale<=1000000,"Binding heading or scale is outside its contract.");
        var p=b.publication;
        ContentCatalog.Require(p.authority=="spatialdb"&&Id(p.graph_id)&&Hex(p.version)&&Hex(p.root_sha256)&&Hex(p.geography_sha256)&&Id(p.root_entity_id)
            &&p.address=="usap://spatialdb/entity/"+p.root_entity_id&&Text(p.frame_id,160)&&p.frame_axes.SequenceEqual(PublicationAxes)&&Text(p.license,120),"Binding must pin an immutable SpatialDB publication.");
        GeoFrames.Validate(p.frame_origin);
        ContentCatalog.Require(Same(t.region_to_ecef,RegionToEcef(t.anchor,t.heading_degrees,t.scale),1e-9,1e-4)
            &&Same(t.region_to_publication,RegionToPublication(t.anchor,t.heading_degrees,t.scale,p.frame_origin),1e-9,1e-4),"Binding matrices disagree with its anchor, heading and scale.");
        var transformer=new GeoTransformer(b);
        var v=b.vertical;
        ContentCatalog.Require(Text(v.region_z,200)&&Text(v.approximates,120)&&Text(v.geoid_model,80)&&Hex(v.geoid_grid_sha256)&&double.IsFinite(v.geoid_undulation_metres)
            &&Math.Abs(v.geoid_undulation_metres)<=150&&double.IsFinite(v.max_deviation_metres)&&v.max_deviation_metres>=0&&v.max_deviation_metres<=1,"Invalid vertical reference.");
        var c=b.calibration;
        ContentCatalog.Require(Text(c.method,400)&&c.tolerance_metres is >0 and <=.05&&c.control_points is {Length:>=5 and <=64}&&c.control_points.Select(x=>x.id).Distinct().Count()==c.control_points.Length,"Calibration needs 5–64 uniquely identified control points.");
        foreach(var cp in c.control_points)
        {
            ContentCatalog.Require(Text(cp.id,80)&&cp.role is "region_centre" or "region_corner" or "published_corner" or "feature" or "terrain"&&cp.region_local is {Length:3}&&cp.publication_frame is {Length:3},"Invalid control point.");
            GeoFrames.Validate(cp.geographic);
            ContentCatalog.Require(GeoFrames.Distance(transformer.Local(cp.geographic),cp.region_local)<=Tolerance&&GeoFrames.Distance(transformer.Publication(cp.region_local),cp.publication_frame)<=Tolerance,
                "Control point "+cp.id+" does not reproduce within 1 mm.");
        }
        var centre=c.control_points.SingleOrDefault(x=>x.role=="region_centre");
        ContentCatalog.Require(centre!=null&&Math.Abs(centre.region_local[0]-sizeX/2)<=c.tolerance_metres&&Math.Abs(centre.region_local[1]-sizeY/2)<=c.tolerance_metres
            &&GeoFrames.Distance(centre.publication_frame,[0,centre.publication_frame[1],0])<=c.tolerance_metres&&c.centre_residual_metres<=c.tolerance_metres,"The publication origin must sit at the region centre.");
        ContentCatalog.Require(double.IsFinite(c.grid_alignment_residual_degrees)&&Math.Abs(c.grid_alignment_residual_degrees)<=.01&&double.IsFinite(c.published_corner_offset_metres)&&c.published_corner_offset_metres>=0&&c.published_corner_offset_metres<=16,"Calibration residuals exceed the binding contract.");
        var g=b.terrain;
        ContentCatalog.Require(Text(g.derivation,400)&&Hex(g.r32_sha256)&&g.grid is [var gx,var gy]&&gx==sizeX&&gy==sizeY&&g.spacing_metres==1&&g.source_assets is {Length:>0 and <=64}&&g.source_assets.All(Hex),"Terrain must be a pinned 1 m heightmap of the whole region.");
        ContentCatalog.Require(double.IsFinite(g.minimum_metres)&&double.IsFinite(g.maximum_metres)&&g.minimum_metres>=0&&g.maximum_metres<=1000&&g.minimum_metres<=g.maximum_metres&&g.water_height_metres>=0&&g.water_height_metres<g.minimum_metres
            &&g.extended_samples>=0&&g.extended_samples<=sizeX*sizeY*.05&&g.max_extension_metres is >=0 and <=16
            // A 1 m heightmap cannot follow a sub-metre slope break of the published triangles; bound it, never hide it.
            &&g.max_surface_residual_metres is >=0 and <=.25&&g.rms_surface_residual_metres is >=0 and <=.02,"Terrain statistics are outside the binding contract.");
        ContentCatalog.Require(g.published_outline is {Length:>=3 and <=64}&&g.published_outline.All(q=>q is [var x,var y]&&double.IsFinite(x)&&double.IsFinite(y)&&x>=-16&&y>=-16&&x<=sizeX+16&&y<=sizeY+16),"Invalid published outline.");
        ContentCatalog.Require(b.correspondence.schema_version==CorrespondenceSchema&&Hex(b.correspondence.sha256)&&b.correspondence.feature_count is >0 and <=100000&&Text(b.correspondence.method,400),"Invalid correspondence pin.");
        ContentCatalog.Vector(b.entry.position,3,1,Math.Max(sizeX,sizeY)-1);ContentCatalog.Vector(b.entry.look_at,3,0,Math.Max(sizeX,sizeY));
        ContentCatalog.Require(b.entry.position[0]<sizeX-1&&b.entry.position[1]<sizeY-1&&b.entry.position[2]>g.water_height_metres,"Binding entry must stand inside the region above water.");
        ContentCatalog.Require(b.attribution is {Length:>0 and <=16}&&b.attribution.All(a=>Text(a.source_id,80)&&Text(a.provider,160)&&Text(a.license,80)&&Text(a.attribution,512)),"Published sources require attribution.");
        ContentCatalog.Require(Hex(b.version)&&b.version==ComputeVersion(b),"Binding version is not the digest of its content.");
        return transformer;
    }

    public static string Address(string id)=>"usap://mutsea/entity/"+id;

    // earth.world-binding/v1, exactly the members Earth Core validates.
    public static object CoreBinding(GeographicBinding b)=>new{schema_version="earth.world-binding/v1",address=Address(b.region.region_id),authority="mutsea",
        world_id=b.world_id,region_id=b.region.region_id,anchor=b.transform.anchor,frame=new{axes=EnuAxes,unit="metre"},
        heading_degrees=b.transform.heading_degrees,scale=b.transform.scale,version=b.version};
    // earth.world-entry/v1: discovery, never admission. The native login is the authorization.
    public static object CoreEntry(GeographicBinding b,Geodetic at,string? spatialAddress=null)=>new{schema_version="earth.world-entry/v1",
        address=spatialAddress??b.publication.address,authority="mutsea",world_id=b.world_id,region_id=b.region.region_id,anchor=at,
        authorization="required",binding_relation="geographic_region"};
    public static object Destination(Geodetic at)=>new{schema_version="earth.navigation/v1",kind="geographic",position=at};
}

public sealed class GeoTransformer
{
    public GeographicBinding Binding{get;}
    readonly double[] toEcef,fromEcef,toPublication,fromPublication;
    public GeoTransformer(GeographicBinding b)
    {
        Binding=b;var t=b.transform;
        toEcef=GeoBindings.RegionToEcef(t.anchor,t.heading_degrees,t.scale);fromEcef=GeoFrames.InverseSimilarity(toEcef);
        toPublication=GeoBindings.RegionToPublication(t.anchor,t.heading_degrees,t.scale,b.publication.frame_origin);fromPublication=GeoFrames.InverseSimilarity(toPublication);
    }
    public double SizeX=>Binding.region.size_metres[0];
    public double SizeY=>Binding.region.size_metres[1];
    public double[] Ecef(double[] local)=>GeoFrames.Apply(toEcef,local);
    public double[] LocalFromEcef(double[] ecef)=>GeoFrames.Apply(fromEcef,ecef);
    public Geodetic Geographic(double[] local)=>GeoFrames.FromEcef(Ecef(local));
    public double[] Local(Geodetic g)=>LocalFromEcef(GeoFrames.Ecef(g));
    public double[] Publication(double[] local)=>GeoFrames.Apply(toPublication,local);
    public double[] LocalFromPublication(double[] p)=>GeoFrames.Apply(fromPublication,p);
    public bool Inside(double x,double y)=>x>=0&&y>=0&&x<SizeX&&y<SizeY;

    // A geographic point without a trusted height resolves onto live ground: the vertical
    // through (lat, lon) is not the region's +Z away from the anchor, so iterate to the surface.
    public double[] OnGround(double latitude,double longitude,Func<double,double,double> ground)
    {
        double h=Binding.transform.anchor.height_metres;var local=Local(new(longitude,latitude,h));
        for(int i=0;i<8&&Inside(local[0],local[1]);i++)
        {
            double dz=ground(local[0],local[1])-local[2];
            if(Math.Abs(dz)<1e-6)break;
            h+=dz;local=Local(new(longitude,latitude,h));
        }
        return local;
    }
}
