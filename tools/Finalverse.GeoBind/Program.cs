// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Operator calibration: derive a geographic binding candidate for one MutSea region from one
// immutable SpatialDB publication. Every publication byte is hash-verified before use; the
// heightmap is resampled from the very triangles the renderer draws; nothing is issued here.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Finalverse.World;

var a=new Dictionary<string,string>();
for(int i=0;i+1<args.Length;i+=2)a[args[i].TrimStart('-')]=args[i+1];
string[] required=["publication","root-sha","world-id","region-id","region-name","size","geoid-undulation","geoid-variation","geoid-grid-sha","out"];
if(required.Any(k=>!a.ContainsKey(k))){Console.Error.WriteLine("Usage: GeoBind "+string.Join(" ",required.Select(k=>"--"+k+" <value>"))+" [--revision n --previous <version>]");return 2;}
double D(string k)=>double.Parse(a[k],CultureInfo.InvariantCulture);
string pub=a["publication"],rootSha=a["root-sha"],world=a["world-id"],region=a["region-id"];int S=int.Parse(a["size"]);
double N=D("geoid-undulation"),geoidVariation=D("geoid-variation");
int revision=a.TryGetValue("revision",out var rv)?int.Parse(rv):1;string? previous=a.TryGetValue("previous",out var pv)?pv:null;
var clock=System.Diagnostics.Stopwatch.StartNew();
void Check(bool ok,string why){if(!ok)throw new Exception(why);}

// ---- publication, verified
byte[] Obj(string sha,long? length=null)
{
    var bytes=File.ReadAllBytes(Path.Combine(pub,"objects",sha));
    Check(ContentCatalog.Hash(bytes)==sha&&(length==null||bytes.Length==length),"Object "+sha+" fails its content address.");return bytes;
}
var rootFile=Directory.GetFiles(Path.Combine(pub,"scene-publications"),"*.json").Single(f=>ContentCatalog.Hash(File.ReadAllBytes(f))==rootSha);
var root=JsonDocument.Parse(File.ReadAllBytes(rootFile)).RootElement;
string version=root.GetProperty("version").GetString()!;
Check(Path.GetFileNameWithoutExtension(rootFile)==version&&root.GetProperty("schema_version").GetString()=="spatialdb.scene-graph/v3"&&root.GetProperty("world_binding").ValueKind==JsonValueKind.Null,"Unexpected publication root.");
var placement=root.GetProperty("placement");
Check(placement.GetProperty("kind").GetString()=="local_tangent_plane"&&placement.GetProperty("datum").GetString()=="wgs84"&&placement.GetProperty("unit").GetString()=="metre"
    &&placement.GetProperty("axes").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(GeoBindings.PublicationAxes),"Only WGS84 east-up-south tangent publications are bindable.");
var o=placement.GetProperty("origin");var origin=new Geodetic(o.GetProperty("longitude_degrees").GetDouble(),o.GetProperty("latitude_degrees").GetDouble(),o.GetProperty("height_metres").GetDouble());
string frameId=placement.GetProperty("frame_id").GetString()!,graphId=root.GetProperty("graph_id").GetString()!,rootEntity=root.GetProperty("root_entity_id").GetString()!;
var cells=new List<JsonElement>();int documents=0;
void Cells(JsonElement list,int depth)
{
    Check(depth<64,"Subtree recursion.");
    foreach(var c in list.EnumerateArray())
    {
        cells.Add(c);
        if(c.TryGetProperty("subtree",out var st)&&st.ValueKind==JsonValueKind.Object)
        {
            var d=st.GetProperty("document");var doc=JsonDocument.Parse(Obj(d.GetProperty("sha256").GetString()!,d.GetProperty("byte_length").GetInt64())).RootElement;documents++;
            Check(doc.GetProperty("graph_id").GetString()==graphId&&doc.GetProperty("frame_id").GetString()==frameId,"Subtree belongs to another graph or frame.");
            Cells(doc.GetProperty("cells"),depth+1);
        }
    }
}
Cells(root.GetProperty("cells"),0);
var byCell=new Dictionary<string,JsonElement>();foreach(var c in cells)byCell[c.GetProperty("cell_id").GetString()!]=c;
string geographySha=root.GetProperty("dataset").GetProperty("source_sha256").GetString()!;
var geography=JsonDocument.Parse(Obj(geographySha)).RootElement;
Check(geography.GetProperty("schema_version").GetString()=="spatialdb.geography/v1"&&geography.GetProperty("graph_id").GetString()==graphId&&geography.GetProperty("frame").GetProperty("frame_id").GetString()==frameId,"Geography evidence differs from the publication.");
var features=geography.GetProperty("features").EnumerateArray().ToArray();
(JsonElement cell,JsonElement rep) Finest(string entity)
{
    var c=byCell.Values.Where(x=>x.GetProperty("entity_id").ValueKind==JsonValueKind.String&&x.GetProperty("entity_id").GetString()==entity).MinBy(x=>x.GetProperty("geometric_error").GetDouble());
    Check(c.ValueKind==JsonValueKind.Object,"Feature "+entity+" has no published cell.");
    var r=c.GetProperty("representations").EnumerateArray().Single(x=>x.GetProperty("family").GetString()=="visual"&&x.GetProperty("format").GetString()=="glb");
    return (c,r);
}
(double[][] pos,int[] idx) Glb(JsonElement rep)
{
    var asset=rep.GetProperty("asset");var b=Obj(asset.GetProperty("sha256").GetString()!,asset.GetProperty("byte_length").GetInt64());
    Check(BitConverter.ToUInt32(b,0)==0x46546C67&&BitConverter.ToUInt32(b,4)==2,"Not a glTF 2 binary.");
    int jl=BitConverter.ToInt32(b,12);var j=JsonDocument.Parse(b.AsMemory(20,jl)).RootElement;int bin=20+jl+8;
    foreach(var n in j.GetProperty("nodes").EnumerateArray())Check(!n.TryGetProperty("matrix",out _)&&!n.TryGetProperty("translation",out _)&&!n.TryGetProperty("rotation",out _)&&!n.TryGetProperty("scale",out _),"Node transforms are not expected in a published cell.");
    var prim=j.GetProperty("meshes")[0].GetProperty("primitives")[0];Check(j.GetProperty("meshes").GetArrayLength()==1&&j.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength()==1&&(!prim.TryGetProperty("mode",out var mode)||mode.GetInt32()==4),"One triangle primitive per cell expected.");
    JsonElement Acc(int k)=>j.GetProperty("accessors")[k];
    int Off(JsonElement acc){var v=j.GetProperty("bufferViews")[acc.GetProperty("bufferView").GetInt32()];return bin+(v.TryGetProperty("byteOffset",out var x)?x.GetInt32():0)+(acc.TryGetProperty("byteOffset",out var y)?y.GetInt32():0);}
    var pa=Acc(prim.GetProperty("attributes").GetProperty("POSITION").GetInt32());Check(pa.GetProperty("componentType").GetInt32()==5126&&pa.GetProperty("type").GetString()=="VEC3","Float positions expected.");
    int count=pa.GetProperty("count").GetInt32(),po=Off(pa);var pos=new double[count][];
    for(int i=0;i<count;i++)pos[i]=[BitConverter.ToSingle(b,po+12*i),BitConverter.ToSingle(b,po+12*i+4),BitConverter.ToSingle(b,po+12*i+8)];
    var ia=Acc(prim.GetProperty("indices").GetInt32());int ic=ia.GetProperty("count").GetInt32(),io=Off(ia),type=ia.GetProperty("componentType").GetInt32();var idx=new int[ic];
    for(int i=0;i<ic;i++)idx[i]=type==5125?(int)BitConverter.ToUInt32(b,io+4*i):type==5123?BitConverter.ToUInt16(b,io+2*i):throw new Exception("Unsupported index type.");
    Check(idx.All(x=>x>=0&&x<count)&&ic%3==0,"Invalid indices.");return (pos,idx);
}

// ---- terrain lattice: four published tiles, each an n×n row-major grid
var tiles=new List<(double[][] p,int[] idx,int n,double[] v0,double[] ea,double[] eb,string sha,string cell)>();
foreach(var f in features.Where(f=>f.GetProperty("kind").GetString()=="terrain"))
{
    var (cell,rep)=Finest(f.GetProperty("entity_id").GetString()!);var (p,idx)=Glb(rep);int n=(int)Math.Round(Math.Sqrt(p.Length));Check(n*n==p.Length,"Terrain tile is not square.");
    for(int r=0;r<n-1;r++)for(int c=0;c<n-1;c++)
    {
        int k=(r*(n-1)+c)*6,v=r*n+c;
        Check(idx[k]==v&&idx[k+1]==v+n&&idx[k+2]==v+1&&idx[k+3]==v+1&&idx[k+4]==v+n&&idx[k+5]==v+n+1,"Terrain triangulation differs from the published lattice.");
    }
    double[] ea=Scale(Sub(p[n-1],p[0]),1.0/(n-1)),eb=Scale(Sub(p[n*(n-1)],p[0]),1.0/(n-1));
    tiles.Add((p,idx,n,p[0],ea,eb,rep.GetProperty("asset").GetProperty("sha256").GetString()!,cell.GetProperty("cell_id").GetString()!));
}
Check(tiles.Count==4,"Expected four terrain tiles.");

// Grid axes in the publication frame (x east, z south): +ea is grid east, −eb is grid north.
double[] gridNorth=Unit(Horizontal(tiles.Select(t=>Unit(Horizontal(Scale(t.eb,-1)))).Aggregate(Add)));
double[] gridEast=Unit(Horizontal(tiles.Select(t=>Unit(Horizontal(t.ea))).Aggregate(Add)));
double gridBearing=GeoFrames.Deg(Math.Atan2(gridNorth[0],-gridNorth[2]));
double conformal=Math.Abs(GeoFrames.Deg(Math.Acos(Math.Clamp(Dot(gridNorth,gridEast),-1,1)))-90);
double tileEdge=tiles.Average(t=>Math.Sqrt(Dot(Horizontal(t.ea),Horizontal(t.ea))))*(tiles[0].n-1);

// ---- solve the anchor and heading: publication origin at the region centre, grid north on +Y
var pubToEcef=GeoBindings.PublicationToEcef(origin);
double[] centreEcef=GeoFrames.Ecef(origin),northEcef=GeoFrames.ApplyLinear(pubToEcef,gridNorth);
double zc=origin.height_metres-N;double[] target=[S/2.0,S/2.0,zc];
double heading=gridBearing;Geodetic anchor=origin;
for(int it=0;it<50;it++)
{
    var m=GeoBindings.RegionToEcef(anchor,heading,1);
    var shifted=Sub(centreEcef,GeoFrames.ApplyLinear(m,target));anchor=GeoFrames.FromEcef(shifted);
    m=GeoBindings.RegionToEcef(anchor,heading,1);var v=GeoFrames.ApplyLinear(GeoFrames.InverseSimilarity(m),northEcef);
    double turn=GeoFrames.Deg(Math.Atan2(v[0],v[1]));heading+=turn;
    if(Math.Abs(turn)<1e-13&&GeoFrames.Distance(GeoFrames.Apply(GeoFrames.InverseSimilarity(GeoBindings.RegionToEcef(anchor,heading,1)),centreEcef),target)<1e-9)break;
}
heading=((heading%360)+360)%360;
var M=GeoBindings.RegionToEcef(anchor,heading,1);var Minv=GeoFrames.InverseSimilarity(M);
var R2P=GeoBindings.RegionToPublication(anchor,heading,1,origin);var P2R=GeoFrames.InverseSimilarity(R2P);
double centreResidual=GeoFrames.Distance(GeoFrames.Apply(Minv,centreEcef),target);
double[] yAxisPub=Unit(Horizontal(GeoFrames.ApplyLinear(R2P,[0,1,0]))),xAxisPub=Unit(Horizontal(GeoFrames.ApplyLinear(R2P,[1,0,0])));
double alignment=Math.Max(Math.Abs(GeoFrames.Deg(Math.Acos(Math.Clamp(Dot(yAxisPub,gridNorth),-1,1)))),Math.Abs(GeoFrames.Deg(Math.Acos(Math.Clamp(Dot(xAxisPub,gridEast),-1,1)))));

// ---- terrain resampling: intersect each region vertical with the published triangles
double[] up=GeoFrames.ApplyLinear(R2P,[0,0,1]);
(double z,bool inside,double[] hit)? Intersect(double x,double y)
{
    var q0=GeoFrames.Apply(R2P,[x,y,0]);
    foreach(var t in tiles)
    {
        double det=t.ea[0]*t.eb[2]-t.eb[0]*t.ea[2],dx=q0[0]-t.v0[0],dz=q0[2]-t.v0[2];
        double c=(dx*t.eb[2]-t.eb[0]*dz)/det,r=(t.ea[0]*dz-dx*t.ea[2])/det;
        if(c<-1||r<-1||c>t.n||r>t.n)continue;
        for(int dr=-1;dr<=1;dr++)for(int dc=-1;dc<=1;dc++)
        {
            int ci=(int)Math.Floor(c)+dc,ri=(int)Math.Floor(r)+dr;if(ci<0||ri<0||ci>=t.n-1||ri>=t.n-1)continue;
            int k=(ri*(t.n-1)+ci)*6;
            for(int s=0;s<6;s+=3)
            {
                var A=t.p[t.idx[k+s]];var B=t.p[t.idx[k+s+1]];var C=t.p[t.idx[k+s+2]];
                var nrm=Cross(Sub(B,A),Sub(C,A));double denom=Dot(up,nrm);if(Math.Abs(denom)<1e-12)continue;
                double tt=Dot(Sub(A,q0),nrm)/denom;var hit=Add(q0,Scale(up,tt));
                if(Barycentric(hit,A,B,C))return (tt,true,hit);
            }
        }
    }
    return null;
}
double[] Clamped(double x,double y,out double extension)
{
    var q0=GeoFrames.Apply(R2P,[x,y,0]);double best=double.MaxValue;double[] point=[0,0,0];
    foreach(var t in tiles)
    {
        double det=t.ea[0]*t.eb[2]-t.eb[0]*t.ea[2],dx=q0[0]-t.v0[0],dz=q0[2]-t.v0[2];
        double c=Math.Clamp((dx*t.eb[2]-t.eb[0]*dz)/det,0,t.n-1),r=Math.Clamp((t.ea[0]*dz-dx*t.ea[2])/det,0,t.n-1);
        int ci=Math.Min((int)c,t.n-2),ri=Math.Min((int)r,t.n-2);double u=c-ci,w=r-ri;
        var p00=t.p[ri*t.n+ci];var p01=t.p[ri*t.n+ci+1];var p10=t.p[(ri+1)*t.n+ci];var p11=t.p[(ri+1)*t.n+ci+1];
        // The published diagonal runs from (r+1,c) to (r,c+1).
        var surface=u+w<=1?Add(p00,Add(Scale(Sub(p01,p00),u),Scale(Sub(p10,p00),w))):Add(p11,Add(Scale(Sub(p10,p11),1-u),Scale(Sub(p01,p11),1-w)));
        var local=GeoFrames.Apply(P2R,surface);double d=Math.Sqrt((local[0]-x)*(local[0]-x)+(local[1]-y)*(local[1]-y));
        if(d<best){best=d;point=local;}
    }
    extension=best;return point;
}
var heights=new float[S*S];int extended=0;double maxExtension=0;
for(int y=0;y<S;y++)for(int x=0;x<S;x++)
{
    double z;var hit=Intersect(x,y);
    if(hit!=null)z=hit.Value.z;else{var p=Clamped(x,y,out var e);z=p[2];extended++;maxExtension=Math.Max(maxExtension,e);}
    float h=MathF.Round((float)z,3,MidpointRounding.AwayFromZero);Check(MathF.Round(h,3,MidpointRounding.AwayFromZero)==h,"Height rounding is not a fixed point.");
    heights[y*S+x]=h;
}
var r32=new byte[S*S*4];for(int i=0;i<heights.Length;i++)BitConverter.GetBytes(heights[i]).CopyTo(r32,i*4);
double minZ=heights.Min(),maxZ=heights.Max();
double Bilinear(double x,double y){int i=Math.Min((int)x,S-2),j=Math.Min((int)y,S-2);double u=x-i,v=y-j;return heights[j*S+i]*(1-u)*(1-v)+heights[j*S+i+1]*u*(1-v)+heights[(j+1)*S+i]*(1-u)*v+heights[(j+1)*S+i+1]*u*v;}
var random=new Random(20261024);double maxResidual=0,sumSq=0;int samples=0;double maxSample=0,maxPlane=0;
while(samples<20000)
{
    double x=random.NextDouble()*(S-1),y=random.NextDouble()*(S-1);var hit=Intersect(x,y);if(hit==null)continue;
    double d=Math.Abs(Bilinear(x,y)-hit.Value.z);maxResidual=Math.Max(maxResidual,d);sumSq+=d*d;samples++;
}
for(int y=0;y<S;y+=7)for(int x=0;x<S;x+=7){var hit=Intersect(x,y);if(hit!=null)maxSample=Math.Max(maxSample,Math.Abs(heights[y*S+x]-hit.Value.z));}
// Tangent-plane height versus h − N over every sample: curvature and tilt of the flat region.
for(int y=0;y<S;y+=4)for(int x=0;x<S;x+=4){var g=GeoFrames.FromEcef(GeoFrames.Apply(M,[x,y,heights[y*S+x]]));maxPlane=Math.Max(maxPlane,Math.Abs(heights[y*S+x]-(g.height_metres-N)));}
double water=Math.Floor((minZ-.25)*100)/100;

// ---- published outline: outer lattice corners, in the region frame
var corners=tiles.SelectMany(t=>new[]{t.p[0],t.p[t.n-1],t.p[t.n*(t.n-1)],t.p[t.n*t.n-1]}).Select(p=>GeoFrames.Apply(P2R,p)).ToArray();
double[][] outline=[corners.MinBy(p=>p[0]+p[1])!,corners.MaxBy(p=>p[0]-p[1])!,corners.MaxBy(p=>p[0]+p[1])!,corners.MinBy(p=>p[0]-p[1])!];
double[][] regionCorners=[[0,0],[S,0],[S,S],[0,S]];
double cornerOffset=outline.Zip(regionCorners,(p,c)=>Math.Sqrt((p[0]-c[0])*(p[0]-c[0])+(p[1]-c[1])*(p[1]-c[1]))).Max();

// ---- correspondence over every published feature
double Round(double v)=>Math.Round(v,4,MidpointRounding.AwayFromZero);
var correspondence=new List<GeoFeature>();
foreach(var f in features)
{
    string id=f.GetProperty("entity_id").GetString()!,kind=f.GetProperty("kind").GetString()!;
    var (cell,rep)=Finest(id);var (p,idx)=Glb(rep);var local=p.Select(v=>GeoFrames.Apply(P2R,v)).ToArray();
    var tri=new List<double>();
    if(kind=="terrain")
    {
        int n=(int)Math.Round(Math.Sqrt(p.Length));var q=new[]{local[0],local[n-1],local[n*n-1],local[n*(n-1)]};
        foreach(var (i,j,k) in new[]{(0,1,2),(0,2,3)})tri.AddRange([Round(q[i][0]),Round(q[i][1]),Round(q[j][0]),Round(q[j][1]),Round(q[k][0]),Round(q[k][1])]);
    }
    else for(int t=0;t<idx.Length;t+=3)
    {
        var A=local[idx[t]];var B=local[idx[t+1]];var C=local[idx[t+2]];
        if(Math.Abs((B[0]-A[0])*(C[1]-A[1])-(C[0]-A[0])*(B[1]-A[1]))/2<1e-4)continue;
        tri.AddRange([Round(A[0]),Round(A[1]),Round(B[0]),Round(B[1]),Round(C[0]),Round(C[1])]);
    }
    Check(tri.Count>0,"Feature "+id+" has no footprint.");
    double? height=f.TryGetProperty("height",out var hh)&&hh.ValueKind==JsonValueKind.Object&&hh.TryGetProperty("value_metres",out var hv)&&hv.ValueKind==JsonValueKind.Number?hv.GetDouble():null;
    string S1(string k)=>f.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()!:"unspecified";
    correspondence.Add(new(id,"usap://spatialdb/entity/"+id,kind,S1("label"),S1("source_id"),S1("source_feature_id"),S1("geometry_status"),height,
        [Round(local.Min(v=>v[0])),Round(local.Min(v=>v[1])),Round(local.Min(v=>v[2]))],[Round(local.Max(v=>v[0])),Round(local.Max(v=>v[1])),Round(local.Max(v=>v[2]))],
        tri.ToArray(),cell.GetProperty("cell_id").GetString()!,rep.GetProperty("asset").GetProperty("sha256").GetString()!));
}
correspondence.Sort((x,y)=>string.CompareOrdinal(x.entity_id,y.entity_id));

// ---- assemble and seal
var transform=new GeoTransform(GeoBindings.Model,"wgs84",anchor,GeoBindings.EnuAxes,"metre",heading,1,M,R2P);
var index=new GeoCorrespondenceIndex(GeoBindings.CorrespondenceSchema,region,GeoBindings.TransformSha(transform),version,rootSha,correspondence.ToArray());
var indexBytes=JsonSerializer.SerializeToUtf8Bytes(index);
var transformer=new GeoTransformer(new GeographicBinding(GeoBindings.Schema,"",0,null,"mutsea",world,new(region,a["region-name"],[S,S]),transform,null!,
    new("spatialdb",graphId,version,rootSha,rootEntity,"usap://spatialdb/entity/"+rootEntity,frameId,origin,GeoBindings.PublicationAxes,geographySha,"x"),null!,null!,null!,null!,[],""));
var points=new List<GeoControlPoint>();
void Point(string id,string role,double[] local)=>points.Add(new(id,role,transformer.Geographic(local),transformer.Publication(local),local));
points.Add(new("publication-origin","region_centre",origin,[0,0,0],GeoFrames.Apply(Minv,centreEcef)));
foreach(var (id,c) in new[]{("region-sw",regionCorners[0]),("region-se",regionCorners[1]),("region-ne",regionCorners[2]),("region-nw",regionCorners[3])})Point(id,"region_corner",[c[0],c[1],zc]);
foreach(var (id,c) in new[]{("published-sw",outline[0]),("published-se",outline[1]),("published-ne",outline[2]),("published-nw",outline[3])})Point(id,"published_corner",c);
var near=correspondence.Where(f=>f.kind=="building").MinBy(f=>{var c=GeoCorrespondence.Centroid(f);return (c[0]-S/2.0)*(c[0]-S/2.0)+(c[1]-S/2.0)*(c[1]-S/2.0);})!;
var nc=GeoCorrespondence.Centroid(near);Point("feature-"+near.entity_id,"feature",[nc[0],nc[1],near.region_min[2]]);
foreach(var (x,y) in new[]{(128,128),(384,128),(384,384),(128,384)}){var hit=Intersect(x,y);if(hit!=null)Point($"terrain-{x}-{y}","terrain",[x,y,hit.Value.z]);}
var corr=new GeoCorrespondence(index,new GeographicBinding(GeoBindings.Schema,"",0,null,"mutsea",world,new(region,a["region-name"],[S,S]),transform,null!,
    new("spatialdb",graphId,version,rootSha,rootEntity,"",frameId,origin,GeoBindings.PublicationAxes,geographySha,"x"),null!,null!,new(GeoBindings.CorrespondenceSchema,"",correspondence.Count,""),null!,[],""));
double Ground(double x,double y)=>Bilinear(Math.Clamp(x,0,S-1.000001),Math.Clamp(y,0,S-1.000001));
double[] entry=[S/2.0,S/2.0,0];
if(corr.At(entry[0],entry[1]).Any(f=>f.kind=="building"))
{
    entry=Enumerable.Range(1,40).SelectMany(r=>Enumerable.Range(0,32).Select(k=>new[]{S/2.0+r*Math.Cos(k*Math.PI/16),S/2.0+r*Math.Sin(k*Math.PI/16),0}))
        .First(p=>!corr.At(p[0],p[1]).Any(f=>f.kind=="building"));
}
entry[2]=Ground(entry[0],entry[1]);
var attribution=geography.GetProperty("sources").EnumerateArray().Select(s=>new GeoAttribution(s.GetProperty("source_id").GetString()!,s.GetProperty("provider").GetString()!,
    s.GetProperty("license").GetString()!,s.GetProperty("attribution").GetString()!)).ToArray();
var binding=GeoBindings.Seal(new GeographicBinding(GeoBindings.Schema,GeoBindings.BindingId(world,region),revision,previous,"mutsea",world,new(region,a["region-name"],[S,S]),transform,
    new("Region +Z: metres above the anchor tangent plane, offset so z equals CGVD2013 orthometric height at the publication origin.","CGVD2013 orthometric height",N,"CGG2013a",a["geoid-grid-sha"],Math.Round(maxPlane+geoidVariation,4)),
    new("spatialdb",graphId,version,rootSha,rootEntity,"usap://spatialdb/entity/"+rootEntity,frameId,origin,GeoBindings.PublicationAxes,geographySha,root.GetProperty("provenance").GetProperty("license").GetString()!),
    new("Publication origin fixed at the region centre with z = h - N; region +Y aligned to the published terrain grid north (mean of four tiles); scale 1 (true metres); solved by fixed-point iteration in f64.",
        .001,centreResidual,alignment,cornerOffset,points.ToArray()),
    new("Each 1 m region vertical intersected with the finest published SpatialDB terrain triangles (exact f64 frame composition), rounded to MutSea's persisted 1 mm; samples outside the published lattice take the nearest published surface point.",
        tiles.Select(t=>t.sha).ToArray(),ContentCatalog.Hash(r32),[S,S],1,minZ,maxZ,water,extended,Math.Round(maxExtension,4),Math.Round(Math.Max(maxResidual,maxSample),5),Math.Round(Math.Sqrt(sumSq/samples),5),outline.Select(p=>new[]{Round(p[0]),Round(p[1])}).ToArray()),
    new(GeoBindings.CorrespondenceSchema,ContentCatalog.Hash(indexBytes),correspondence.Count,"Finest published representation triangles of every geography feature projected onto the region plane (terrain tiles by lattice outline); slivers under 1e-4 m² omitted."),
    new(entry,[entry[0],Math.Min(S-1,entry[1]+12),entry[2]+1.5]),attribution,""));
Directory.CreateDirectory(a["out"]);
string Out(string suffix)=>Path.Combine(a["out"],binding.version+suffix);
var report=new{binding.version,binding.binding_id,publication_root=rootSha,cells=cells.Count,subtree_documents=documents,features=features.Length,
    grid=new{bearing_degrees=gridBearing,conformal_residual_degrees=conformal,tile_edge_true_metres=tileEdge,projected_scale_factor=256/tileEdge},
    solve=new{anchor,heading_degrees=heading,centre_residual_metres=centreResidual,alignment_residual_degrees=alignment},
    terrain=new{minimum=minZ,maximum=maxZ,water,extended_samples=extended,max_extension_metres=maxExtension,bilinear_residual=new{max=maxResidual,rms=Math.Sqrt(sumSq/samples),samples},integer_sample_residual_max=maxSample,tangent_plane_vs_h_minus_N_max=maxPlane},
    outline,corner_offset_metres=cornerOffset,entry=binding.entry,elapsed_ms=clock.ElapsedMilliseconds};
File.WriteAllText(Out(".calibration-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
GeoBindings.Validate(binding,region,S,S);
File.WriteAllBytes(Out(".binding.json"),JsonSerializer.SerializeToUtf8Bytes(binding,new JsonSerializerOptions{WriteIndented=true}));
File.WriteAllBytes(Out(".correspondence.json"),indexBytes);File.WriteAllBytes(Out(".terrain.r32"),r32);
return 0;

static double[] Sub(double[] a,double[] b)=>[a[0]-b[0],a[1]-b[1],a[2]-b[2]];
static double[] Add(double[] a,double[] b)=>[a[0]+b[0],a[1]+b[1],a[2]+b[2]];
static double[] Scale(double[] a,double s)=>[a[0]*s,a[1]*s,a[2]*s];
static double Dot(double[] a,double[] b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
static double[] Cross(double[] a,double[] b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
static double[] Horizontal(double[] v)=>[v[0],0,v[2]];
static double[] Unit(double[] v){double l=Math.Sqrt(Dot(v,v));return [v[0]/l,v[1]/l,v[2]/l];}
static bool Barycentric(double[] p,double[] a,double[] b,double[] c)
{
    var v0=Sub(b,a);var v1=Sub(c,a);var v2=Sub(p,a);
    double d00=Dot(v0,v0),d01=Dot(v0,v1),d11=Dot(v1,v1),d20=Dot(v2,v0),d21=Dot(v2,v1),den=d00*d11-d01*d01;
    double v=(d11*d20-d01*d21)/den,w=(d00*d21-d01*d20)/den;return v>=-1e-9&&w>=-1e-9&&v+w<=1+1e-9;
}
