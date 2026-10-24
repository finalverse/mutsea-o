// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;

namespace Finalverse.World;

// Entity correspondence: the published SpatialDB features expressed in one binding's region
// frame. It is a read-only index over representations SpatialDB owns. MutSea never turns a
// published building into a live object; a live object can only be found standing on one.
// Footprints are the published triangles projected onto the region plane, so "on Rue
// Saint-Paul" means inside the very triangles the renderer draws.
public sealed record GeoFeature(string entity_id,string address,string kind,string label,string source_id,string source_feature_id,string geometry_status,
    double? height_metres,double[] region_min,double[] region_max,double[] footprint,string cell_id,string asset_sha256);
public sealed record GeoCorrespondenceIndex(string schema_version,string region_id,string binding_transform_sha256,string publication_version,
    string publication_root_sha256,GeoFeature[] features);

public sealed class GeoCorrespondence
{
    const double Cell=8;
    readonly Dictionary<string,GeoFeature> byId=new(StringComparer.Ordinal);
    readonly Dictionary<(int,int),List<(GeoFeature feature,int triangle)>> grid=new();
    public GeoCorrespondenceIndex Index{get;}
    public int Count=>byId.Count;

    public GeoCorrespondence(GeoCorrespondenceIndex index,GeographicBinding binding)
    {
        ContentCatalog.Require(index.schema_version==GeoBindings.CorrespondenceSchema&&index.region_id==binding.region.region_id
            &&index.binding_transform_sha256==GeoBindings.TransformSha(binding.transform)&&index.publication_version==binding.publication.version
            &&index.publication_root_sha256==binding.publication.root_sha256,"Correspondence belongs to another binding or publication.");
        ContentCatalog.Require(index.features is {Length:>0}&&index.features.Length==binding.correspondence.feature_count,"Correspondence feature count differs from its pin.");
        double sx=binding.region.size_metres[0],sy=binding.region.size_metres[1];
        foreach(var f in index.features)
        {
            ContentCatalog.Require(Guid.TryParse(f.entity_id,out var g)&&g!=Guid.Empty&&f.address=="usap://spatialdb/entity/"+f.entity_id&&byId.TryAdd(f.entity_id,f),"Invalid or duplicate corresponding entity.");
            ContentCatalog.Require(f.kind is "building" or "road" or "terrain"&&f.label is {Length:>0 and <=200}&&f.footprint.Length is >=6 and <=600000&&f.footprint.Length%6==0
                &&f.footprint.All(double.IsFinite)&&f.region_min is {Length:3}&&f.region_max is {Length:3}&&f.cell_id.Length==36&&f.asset_sha256.Length==64,"Invalid corresponding feature "+f.entity_id+".");
            for(int t=0;t<f.footprint.Length;t+=6)
            {
                double x0=Math.Min(f.footprint[t],Math.Min(f.footprint[t+2],f.footprint[t+4])),x1=Math.Max(f.footprint[t],Math.Max(f.footprint[t+2],f.footprint[t+4]));
                double y0=Math.Min(f.footprint[t+1],Math.Min(f.footprint[t+3],f.footprint[t+5])),y1=Math.Max(f.footprint[t+1],Math.Max(f.footprint[t+3],f.footprint[t+5]));
                ContentCatalog.Require(x0>=-64&&y0>=-64&&x1<=sx+64&&y1<=sy+64,"Corresponding footprint lies outside the bound region.");
                for(int i=(int)Math.Floor(x0/Cell);i<=(int)Math.Floor(x1/Cell);i++)for(int j=(int)Math.Floor(y0/Cell);j<=(int)Math.Floor(y1/Cell);j++)
                {
                    if(!grid.TryGetValue((i,j),out var list))grid[(i,j)]=list=new();
                    list.Add((f,t));
                }
            }
        }
        Index=index;
    }

    public GeoFeature? Get(string id)=>byId.TryGetValue(id,out var f)?f:null;

    // Most specific first: a building over the road over the ground it stands on.
    public GeoFeature[] At(double x,double y)
    {
        if(!grid.TryGetValue(((int)Math.Floor(x/Cell),(int)Math.Floor(y/Cell)),out var list))return [];
        return list.Where(e=>Contains(e.feature.footprint,e.triangle,x,y)).Select(e=>e.feature).Distinct()
            .OrderBy(f=>f.kind switch{"building"=>0,"road"=>1,_=>2}).ThenBy(f=>f.entity_id,StringComparer.Ordinal).ToArray();
    }
    public bool Covers(GeoFeature f,double x,double y)
    {
        for(int t=0;t<f.footprint.Length;t+=6)if(Contains(f.footprint,t,x,y))return true;
        return false;
    }

    static bool Contains(double[] p,int t,double x,double y)
    {
        double ax=p[t],ay=p[t+1],bx=p[t+2],by=p[t+3],cx=p[t+4],cy=p[t+5];
        double d=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);if(Math.Abs(d)<1e-12)return false;
        double u=((by-cy)*(x-cx)+(cx-bx)*(y-cy))/d,v=((cy-ay)*(x-cx)+(ax-cx)*(y-cy))/d;
        return u>=-1e-9&&v>=-1e-9&&u+v<=1+1e-9;
    }

    // Area-weighted centroid of the published footprint.
    public static double[] Centroid(GeoFeature f)
    {
        double a=0,x=0,y=0;var p=f.footprint;
        for(int t=0;t<p.Length;t+=6)
        {
            double w=Math.Abs((p[t+2]-p[t])*(p[t+5]-p[t+1])-(p[t+4]-p[t])*(p[t+3]-p[t+1]))/2;
            a+=w;x+=w*(p[t]+p[t+2]+p[t+4])/3;y+=w*(p[t+1]+p[t+3]+p[t+5])/3;
        }
        return a>0?[x/a,y/a]:[(f.region_min[0]+f.region_max[0])/2,(f.region_min[1]+f.region_max[1])/2];
    }

    // A place to stand next to a feature: buildings are SpatialDB representations, not live
    // walls, so an arrival never lands inside one. Prefers an adjacent road, then open ground.
    public double[]? StandingNear(GeoFeature f,double sizeX,double sizeY,Func<double,double,double> ground,double water)
    {
        var c=Centroid(f);
        double radius=f.kind=="building"?Math.Sqrt(Math.Pow(f.region_max[0]-f.region_min[0],2)+Math.Pow(f.region_max[1]-f.region_min[1],2))/2+2:0;
        (double x,double y,int rank,double distance)? best=null;
        for(double r=radius;r<=radius+24;r+=2)for(int k=0;k<(r==0?1:32);k++)
        {
            double a=k*Math.PI/16,x=c[0]+r*Math.Cos(a),y=c[1]+r*Math.Sin(a);
            if(x<2||y<2||x>sizeX-2||y>sizeY-2)continue;
            var at=At(x,y);if(at.Any(e=>e.kind=="building"))continue;
            double z=ground(x,y);if(!(z>water+.1))continue;
            int rank=at.Any(e=>e.kind=="road")?0:1;double d=Math.Sqrt((x-c[0])*(x-c[0])+(y-c[1])*(y-c[1]));
            if(best==null||rank<best.Value.rank||rank==best.Value.rank&&d<best.Value.distance)best=(x,y,rank,d);
            if(best is {rank:0})return [best.Value.x,best.Value.y,ground(best.Value.x,best.Value.y)];
        }
        return best==null?null:[best.Value.x,best.Value.y,ground(best.Value.x,best.Value.y)];
    }

    public static object Summary(GeoFeature f)=>new{f.entity_id,f.address,f.kind,f.label,f.source_id,f.source_feature_id,f.geometry_status,f.height_metres};
}
