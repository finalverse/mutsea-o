// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Offline compiler: Genesis AvatarPackage (skinned glTF) -> native skinned LLMesh.
// Encodings follow the viewer decoders: llvolume.cpp (submesh/weights),
// llmodel.cpp LLMeshSkinInfo (skin block) and llmeshrepository.h (header).
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Finalverse.World;

if(args.Length!=3)throw new ArgumentException("Expected: <avatar package dir> <viewer avatar_skeleton.xml> <NEW output dir>");
var source=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[2]);
if(Directory.Exists(output))throw new InvalidDataException("Preserve existing compiled packages; choose a new output.");
string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}

// ---------------------------------------------------------------- skeleton
var skeletonBytes=File.ReadAllBytes(args[1]);
Require(skeletonBytes.Length<(1<<20)&&Hash(skeletonBytes)==AvatarCatalog.SkeletonSha256,"Viewer skeleton differs from the pinned source.");
var nativeParent=new Dictionary<string,string?>();
void Bones(XElement element,string? parent){foreach(var bone in element.Elements("bone")){var name=bone.Attribute("name")!.Value;nativeParent[name]=parent;Bones(bone,name);}}
Bones(XDocument.Parse(Encoding.UTF8.GetString(skeletonBytes)).Root!,null);
Require(nativeParent.Count==133,"Unexpected native skeleton topology.");

// ---------------------------------------------------------------- manifest
var manifestBytes=File.ReadAllBytes(Path.Combine(source,"package.json"));
Require(manifestBytes.Length<(1<<20),"Manifest too large.");
using var manifestDoc=JsonDocument.Parse(manifestBytes,new JsonDocumentOptions{MaxDepth=16});
var m=manifestDoc.RootElement;var def=m.GetProperty("definition");var rig=def.GetProperty("rig");
Require(m.GetProperty("schema_version").GetInt32()==1&&m.GetProperty("kind").GetString()=="avatar","Only v1 avatar packages are supported.");
Require(m.GetProperty("license").GetProperty("spdx").GetString()=="CC0-1.0","Only original CC0 avatars are enabled.");
Require(def.GetProperty("representation").GetString()=="animated_object"&&!def.GetProperty("selectable").GetBoolean(),"Only non-selectable animated-object citizens are enabled.");
Require(def.GetProperty("skeleton").GetProperty("sha256").GetString()==AvatarCatalog.SkeletonSha256,"Package skeleton pin mismatch.");
Require(rig.GetProperty("root_joint").GetString()=="mPelvis"&&rig.GetProperty("lock_scale").GetBoolean(),"Rig must root at mPelvis with scale lock.");
byte[] Payload(string name)
{
    var entry=m.GetProperty("files").EnumerateArray().Single(f=>f.GetProperty("path").GetString()==name);
    Require(Path.GetFileName(name)==name,"Invalid payload path.");
    var info=new FileInfo(Path.Combine(source,name));Require(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)==0&&info.Length<=(16<<20),"Missing, linked or oversized payload.");
    var bytes=File.ReadAllBytes(info.FullName);Require(Hash(bytes)==entry.GetProperty("sha256").GetString(),"Payload hash changed: "+name);return bytes;
}
var glb=Payload("runtime.glb");

// --------------------------------------------------------------------- GLB
Require(glb.Length>=28&&BitConverter.ToUInt32(glb,0)==0x46546C67&&BitConverter.ToUInt32(glb,4)==2&&BitConverter.ToUInt32(glb,8)==glb.Length,"Not a glTF 2.0 binary.");
int jsonLength=checked((int)BitConverter.ToUInt32(glb,12));Require(BitConverter.ToUInt32(glb,16)==0x4E4F534A&&20L+jsonLength+8<=glb.Length,"Malformed JSON chunk.");
int binLength=checked((int)BitConverter.ToUInt32(glb,20+jsonLength));Require(BitConverter.ToUInt32(glb,24+jsonLength)==0x004E4942&&28L+jsonLength+binLength==glb.Length,"Malformed BIN chunk.");
using var gltfDoc=JsonDocument.Parse(new ReadOnlyMemory<byte>(glb,20,jsonLength),new JsonDocumentOptions{MaxDepth=32});
var g=gltfDoc.RootElement;int binStart=28+jsonLength;
var accessors=g.GetProperty("accessors");var views=g.GetProperty("bufferViews");
(int count,int width,int component,int offset) Accessor(int index)
{
    var a=accessors[index];var view=views[a.GetProperty("bufferView").GetInt32()];
    Require(!view.TryGetProperty("byteStride",out _)&&!a.TryGetProperty("sparse",out _),"Strided/sparse accessors are not accepted.");
    int width=a.GetProperty("type").GetString() switch{"SCALAR"=>1,"VEC2"=>2,"VEC3"=>3,"VEC4"=>4,"MAT4"=>16,_=>throw new InvalidDataException("Unsupported accessor type.")};
    int component=a.GetProperty("componentType").GetInt32();int size=component switch{5121=>1,5123=>2,5125=>4,5126=>4,_=>throw new InvalidDataException("Unsupported component.")};
    int count=a.GetProperty("count").GetInt32();int offset=view.GetProperty("byteOffset").GetInt32()+(a.TryGetProperty("byteOffset",out var o)?o.GetInt32():0);
    Require(count>0&&offset>=0&&(long)offset+(long)count*width*size<=Math.Min(binLength,view.GetProperty("byteOffset").GetInt32()+(long)view.GetProperty("byteLength").GetInt32()),"Accessor exceeds buffer.");
    return(count,width,component,binStart+offset);
}
float[] Floats(int index,int width)
{
    var (count,w,component,offset)=Accessor(index);Require(w==width&&component==5126,"Expected float accessor.");
    var values=new float[count*w];Buffer.BlockCopy(glb,offset,values,0,values.Length*4);
    Require(values.All(float.IsFinite),"Non-finite geometry.");return values;
}
int[] Ints(int index,int width)
{
    var (count,w,component,offset)=Accessor(index);Require(w==width,"Unexpected integer width.");
    var values=new int[count*w];
    for(int i=0;i<values.Length;i++)values[i]=component switch{5121=>glb[offset+i],5123=>BitConverter.ToUInt16(glb,offset+2*i),5125=>checked((int)BitConverter.ToUInt32(glb,offset+4*i)),_=>throw new InvalidDataException("Expected integer accessor.")};
    return values;
}
// glTF (+X left, +Y up, +Z front) -> native (+X forward, +Y left, +Z up).
Vector3d Native(float[] v,int i)=>new(v[3*i+2],v[3*i],v[3*i+1]);

var nodes=g.GetProperty("nodes");var skin=g.GetProperty("skins")[0];
var jointNodes=skin.GetProperty("joints").EnumerateArray().Select(e=>e.GetInt32()).ToArray();
var joints=jointNodes.Select(n=>nodes[n].GetProperty("name").GetString()!).ToArray();
Require(joints.Length is >0 and <=AvatarCatalog.MaxJoints&&joints.Distinct().Count()==joints.Length,"Joint list empty, duplicated or over the native per-mesh limit.");
var declared=rig.GetProperty("joints").EnumerateArray().Select(e=>e.GetString()!).ToArray();
Require(declared.SequenceEqual(joints),"Skin joints differ from the manifest.");
var nodeParent=new Dictionary<int,int>();
for(int i=0;i<nodes.GetArrayLength();i++)if(nodes[i].TryGetProperty("children",out var kids))foreach(var k in kids.EnumerateArray())nodeParent[k.GetInt32()]=i;
var world=new Dictionary<string,Vector3d>();var local=new Dictionary<string,Vector3d>();
var declaredPositions=rig.GetProperty("joint_positions");
foreach(var (name,node) in joints.Zip(jointNodes))
{
    Require(nativeParent.ContainsKey(name),"Unknown native joint: "+name);
    var parent=nativeParent[name];
    var gltfParent=nodeParent.TryGetValue(node,out var p)&&jointNodes.Contains(p)?nodes[p].GetProperty("name").GetString():null;
    Require(parent==gltfParent,"Joint hierarchy differs from the native skeleton at "+name);
    Require(!nodes[node].TryGetProperty("rotation",out _)&&!nodes[node].TryGetProperty("scale",out _)&&!nodes[node].TryGetProperty("matrix",out _),"Native rest joints are translation-only.");
    var t=nodes[node].GetProperty("translation").EnumerateArray().Select(x=>(float)x.GetDouble()).ToArray();
    local[name]=Native(t,0);world[name]=parent==null?local[name]:world[parent]+local[name];
    var d=declaredPositions.GetProperty(name).EnumerateArray().Select(x=>x.GetDouble()).ToArray();
    Require(Math.Abs(d[0]-world[name].X)<1e-5&&Math.Abs(d[1]-world[name].Y)<1e-5&&Math.Abs(d[2]-world[name].Z)<1e-5,"Joint position differs from the manifest at "+name);
}
var pelvis=world["mPelvis"];var rootHeight=rig.GetProperty("root_height").GetDouble();
Require(Math.Abs(pelvis.X)<1e-6&&Math.Abs(pelvis.Y)<1e-6&&Math.Abs(pelvis.Z-rootHeight)<1e-6,"The runtime root (pelvis) must sit on the vertical axis at root_height.");
var ibm=Floats(skin.GetProperty("inverseBindMatrices").GetInt32(),16);
for(int j=0;j<joints.Length;j++)
{
    // Column-major: translation at 12..14 (glTF axes); rotation/scale must be identity.
    for(int r=0;r<3;r++)for(int c=0;c<3;c++)Require(Math.Abs(ibm[16*j+4*c+r]-(r==c?1:0))<1e-6,"Inverse bind must be translation-only.");
    var tr=Native(new[]{ibm[16*j+12],ibm[16*j+13],ibm[16*j+14]},0);
    Require((tr+world[joints[j]]).Length()<1e-4,"Inverse bind does not match the rest joint at "+joints[j]);
}

// ------------------------------------------------------------- materials
var materialDefs=def.GetProperty("materials").Deserialize<ContentMaterialFactors[]>(ContentCatalog.Json)!;
Require(materialDefs.Length is >0 and <=8,"An avatar mesh supports 1-8 material faces.");
var gltfMaterials=g.GetProperty("materials").EnumerateArray().Select(x=>x.GetProperty("name").GetString()).ToArray();
Require(gltfMaterials.SequenceEqual(materialDefs.Select(x=>x.local_id)),"glTF materials differ from the manifest order.");
foreach(var f in materialDefs)ContentCatalog.ValidateMaterial(f);

// ------------------------------------------------------------------ LODs
var lodNames=new[]{"LOD0","LOD1","LOD2","LOD3"};var meshes=g.GetProperty("meshes");
var lods=new List<Sub[]>();var declaredLods=def.GetProperty("lods");
foreach(var lodName in lodNames)
{
    var mesh=meshes.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==lodName);var subs=new List<Sub>();
    var prims=mesh.GetProperty("primitives").EnumerateArray().ToArray();
    Require(prims.Length==materialDefs.Length&&prims.Select((pr,i)=>pr.GetProperty("material").GetInt32()==i).All(x=>x),lodName+" must carry every material face in order.");
    foreach(var prim in prims)
    {
        Require(!prim.TryGetProperty("mode",out var mode)||mode.GetInt32()==4,"Only triangle lists are accepted.");
        var at=prim.GetProperty("attributes");
        var pos=Floats(at.GetProperty("POSITION").GetInt32(),3);var nrm=Floats(at.GetProperty("NORMAL").GetInt32(),3);
        var uv=Floats(at.GetProperty("TEXCOORD_0").GetInt32(),2).Select(x=>(double)x).ToArray();
        var jix=Ints(at.GetProperty("JOINTS_0").GetInt32(),4);var wts=Floats(at.GetProperty("WEIGHTS_0").GetInt32(),4);
        var idx=Ints(prim.GetProperty("indices").GetInt32(),1);
        int n=pos.Length/3;
        Require(n is >0 and <65536&&nrm.Length==pos.Length&&uv.Length==2*n&&jix.Length==4*n&&wts.Length==4*n,"Inconsistent vertex streams.");
        Require(idx.Length%3==0&&idx.Length>=3&&idx.All(i=>i>=0&&i<n),"Invalid triangle indices.");
        var weights=new (int,double)[n][];
        for(int v=0;v<n;v++)
        {
            var list=Enumerable.Range(0,4).Where(k=>wts[4*v+k]>0).Select(k=>(jix[4*v+k],(double)wts[4*v+k])).ToArray();
            Require(list.Length>0&&list.All(x=>x.Item1<joints.Length&&x.Item2<=1.0001)&&Math.Abs(list.Sum(x=>x.Item2)-1)<1e-3&&list.Select(x=>x.Item1).Distinct().Count()==list.Length,"Invalid skin weights.");
            weights[v]=list.OrderByDescending(x=>x.Item2).ToArray();
        }
        var normals=Enumerable.Range(0,n).Select(i=>Native(nrm,i)).ToArray();
        Require(normals.All(x=>Math.Abs(x.Length()-1)<1e-3),"Normals must be unit length.");
        subs.Add(new(Enumerable.Range(0,n).Select(i=>Native(pos,i)).ToArray(),normals,uv,idx,weights));
    }
    int triangles=subs.Sum(s=>s.tri.Length/3);
    Require(triangles==declaredLods.GetProperty(lodName).GetInt32(),lodName+" triangle count differs from the manifest.");
    lods.Add(subs.ToArray());
}
var lodTriangles=lods.Select(l=>l.Sum(s=>s.tri.Length/3)).ToArray();
Require(lodTriangles[0]<=AvatarCatalog.MaxHighTriangles&&lodTriangles.Zip(lodTriangles.Skip(1)).All(p=>p.First>p.Second),"LODs must strictly reduce within the avatar budget.");

// Symmetric domain about the runtime root so unrigged bounds match the rest pose.
var extent=new double[3];
foreach(var s in lods.SelectMany(l=>l))foreach(var v in s.pos){var r=v-pelvis;extent[0]=Math.Max(extent[0],Math.Abs(r.X));extent[1]=Math.Max(extent[1],Math.Abs(r.Y));extent[2]=Math.Max(extent[2],Math.Abs(r.Z));}
var scale=extent.Select(e=>Math.Round(2*e+.002,4)).ToArray();
Vector3d Normalize(Vector3d v)=>new((v.X-pelvis.X)/scale[0],(v.Y-pelvis.Y)/scale[1],(v.Z-pelvis.Z)/scale[2]);

ushort Q(double value,double min,double max)=>(ushort)Math.Clamp(Math.Round(max>min?(value-min)/(max-min)*65535:0),0,65535);
byte[] LodBlock(Sub[] subs)
{
    var faces=new OSDArray();
    foreach(var s in subs)
    {
        var p=s.pos.Select(Normalize).ToArray();
        var min=new Vector3d(p.Min(v=>v.X),p.Min(v=>v.Y),p.Min(v=>v.Z));var max=new Vector3d(p.Max(v=>v.X),p.Max(v=>v.Y),p.Max(v=>v.Z));
        Require(min.X>=-.5&&min.Y>=-.5&&min.Z>=-.5&&max.X<=.5&&max.Y<=.5&&max.Z<=.5,"Vertices exceed the normalized domain.");
        double u0=s.uv.Where((_,i)=>i%2==0).Min(),u1=s.uv.Where((_,i)=>i%2==0).Max(),v0=s.uv.Where((_,i)=>i%2==1).Min(),v1=s.uv.Where((_,i)=>i%2==1).Max();
        using var position=new MemoryStream();using var normal=new MemoryStream();using var texture=new MemoryStream();using var weights=new MemoryStream();using var indices=new MemoryStream();
        void U16(Stream o,ushort x){o.WriteByte((byte)(x&0xff));o.WriteByte((byte)(x>>8));}
        for(int i=0;i<p.Length;i++)
        {
            U16(position,Q(p[i].X,min.X,max.X));U16(position,Q(p[i].Y,min.Y,max.Y));U16(position,Q(p[i].Z,min.Z,max.Z));
            U16(normal,Q(s.nrm[i].X,-1,1));U16(normal,Q(s.nrm[i].Y,-1,1));U16(normal,Q(s.nrm[i].Z,-1,1));
            U16(texture,Q(s.uv[2*i],u0,u1));U16(texture,Q(s.uv[2*i+1],v0,v1));
            // LLModel/LLVolume weights: <=4 x (U8 joint, U16 LE influence), 0xFF terminator when fewer.
            foreach(var (joint,weight) in s.weights[i]){weights.WriteByte((byte)joint);U16(weights,(ushort)Math.Clamp(Math.Round(weight*65535),1,65535));}
            if(s.weights[i].Length<4)weights.WriteByte(0xFF);
        }
        foreach(var x in s.tri)U16(indices,(ushort)x);
        faces.Add(new OSDMap{
            ["Position"]=OSD.FromBinary(position.ToArray()),["Normal"]=OSD.FromBinary(normal.ToArray()),
            ["TexCoord0"]=OSD.FromBinary(texture.ToArray()),["TriangleList"]=OSD.FromBinary(indices.ToArray()),
            ["Weights"]=OSD.FromBinary(weights.ToArray()),
            ["PositionDomain"]=new OSDMap{["Min"]=OSD.FromVector3(new Vector3((float)min.X,(float)min.Y,(float)min.Z)),["Max"]=OSD.FromVector3(new Vector3((float)max.X,(float)max.Y,(float)max.Z))},
            ["TexCoord0Domain"]=new OSDMap{["Min"]=OSD.FromVector2(new Vector2((float)u0,(float)v0)),["Max"]=OSD.FromVector2(new Vector2((float)u1,(float)v1))}});
    }
    return Helpers.ZCompressOSD(faces);
}
OSDArray Matrix(double sx,double sy,double sz,Vector3d t)=>new(new[]{sx,0,0,0, 0,sy,0,0, 0,0,sz,0, t.X,t.Y,t.Z,1}.Select(x=>(OSD)OSD.FromReal(x)).ToList());
var skinBlock=Helpers.ZCompressOSD(new OSDMap{
    ["joint_names"]=new OSDArray(joints.Select(x=>(OSD)OSD.FromString(x)).ToList()),
    // LLMatrix4 row-vector layout, index j*4+k: translation at 12..14.
    ["inverse_bind_matrix"]=new OSDArray(joints.Select(x=>(OSD)Matrix(1,1,1,-world[x])).ToList()),
    ["bind_shape_matrix"]=Matrix(scale[0],scale[1],scale[2],pelvis),
    // Translation = local joint position override (pelvis rides the root, SL-315).
    ["alt_inverse_bind_matrix"]=new OSDArray(joints.Select(x=>(OSD)Matrix(1,1,1,x=="mPelvis"?Vector3d.Zero:local[x])).ToList()),
    ["pelvis_offset"]=OSD.FromReal(0),["lock_scale_if_joint_position"]=OSD.FromBoolean(true)});

var collision=def.GetProperty("collision");
var boxMin=collision.GetProperty("minimum").EnumerateArray().Select((x,i)=>Math.Round(x.GetDouble()/scale[i],4)).ToArray();
var boxMax=collision.GetProperty("maximum").EnumerateArray().Select((x,i)=>Math.Round(x.GetDouble()/scale[i],4)).ToArray();
var hull=new ContentCollision("box",boxMin,boxMax);ContentCatalog.ValidateCollision(hull);
byte[] physics;
using(var corners=new MemoryStream())
{
    for(int corner=0;corner<8;corner++)for(int axis=0;axis<3;axis++)
    {double value=(corner&(1<<axis))==0?boxMin[axis]:boxMax[axis];ushort q=(ushort)Math.Round((value+.5)*65535);corners.WriteByte((byte)(q&0xff));corners.WriteByte((byte)(q>>8));}
    physics=Helpers.ZCompressOSD(new OSDMap{["Min"]=OSD.FromVector3(new Vector3(-.5f,-.5f,-.5f)),["Max"]=OSD.FromVector3(new Vector3(.5f,.5f,.5f)),["BoundingVerts"]=OSD.FromBinary(corners.ToArray())});
}
var blocks=new List<(string name,byte[] data)>{("skin",skinBlock),("physics_convex",physics)};
foreach(var (name,lod) in new[]{"high_lod","medium_lod","low_lod","lowest_lod"}.Zip(lods))blocks.Add((name,LodBlock(lod)));
var header=new OSDMap{["version"]=OSD.FromInteger(1)};int offset=0;
foreach(var b in blocks){header[b.name]=new OSDMap{["offset"]=OSD.FromInteger(offset),["size"]=OSD.FromInteger(b.data.Length)};offset+=b.data.Length;}
var meshBytes=OSDParser.SerializeLLSDBinary(header,false).Concat(blocks.SelectMany(b=>b.data)).ToArray();
Require(meshBytes.Length<=AvatarCatalog.MaxMeshBytes,"Compiled avatar mesh exceeds its byte budget.");

// Package motions: strictly validated native keyframes on pinned-skeleton joints, before any output.
var validatedMotions=new List<(string id,byte[] bytes,NativeKeyframe.Summary summary)>();
if(def.TryGetProperty("animation_assets",out var declaredMotions))
    foreach(var motion in declaredMotions.EnumerateArray())
    {
        string id=motion.GetProperty("local_id").GetString()!;var bytes=Payload(motion.GetProperty("path").GetString()!);
        var summary=NativeKeyframe.Validate(bytes,nativeParent.Keys.ToHashSet());
        Require(Math.Abs(summary.duration-motion.GetProperty("duration").GetDouble())<1e-3&&summary.loop==motion.GetProperty("loop").GetBoolean(),"Motion metadata differs from its payload: "+id);
        validatedMotions.Add((id,bytes,summary));
    }
Directory.CreateDirectory(output);
File.WriteAllBytes(Path.Combine(output,"package.json"),manifestBytes);
File.WriteAllBytes(Path.Combine(output,"avatar.llmesh"),meshBytes);
var materials=new List<ContentMaterial>();
foreach(var f in materialDefs)
{
    var bytes=ContentMaterialAsset.Build(f);var file="material-"+materials.Count+".llmaterial";
    File.WriteAllBytes(Path.Combine(output,file),bytes);materials.Add(new(f.local_id,file,Hash(bytes),f));
}
var animations=def.GetProperty("animations").Deserialize<Dictionary<string,string>>()!;
var motions=new List<AvatarAnimationAsset>();
foreach(var (id,bytes,summary) in validatedMotions)
{
    var file="motion-"+motions.Count+".anim";File.WriteAllBytes(Path.Combine(output,file),bytes);
    motions.Add(new(id,file,Hash(bytes),summary.duration,summary.loop));
}
var compiled=new AvatarCompiled(motions.Count>0?2:1,"avatar",m.GetProperty("package_id").GetString()!,m.GetProperty("version").GetString()!,m.GetProperty("title").GetString()!,
    Hash(manifestBytes),"CC0-1.0","animated_object",AvatarCatalog.SkeletonSha256,joints.Length,rootHeight,scale,"avatar.llmesh",Hash(meshBytes),lodTriangles,
    materialDefs.Select(f=>new AvatarFace(f.local_id,new[]{1d,1,1,1})).ToArray(),materials.ToArray(),animations,hull,
    "Four independent surface LODs sharing one skin and normalized domain; live transition acceptance pending",motions.Count>0?motions.ToArray():null);
AvatarCatalog.Validate(compiled);
File.WriteAllText(Path.Combine(output,"compiled.json"),JsonSerializer.Serialize(compiled,new JsonSerializerOptions{WriteIndented=true,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull})+"\n");
AvatarCatalog.Load(output);
Console.WriteLine(JsonSerializer.Serialize(new{compiled=true,compiled.package_id,compiled.version,joints=joints.Length,lod_triangles=lodTriangles,scale,mesh_bytes=meshBytes.Length,mesh_sha256=compiled.mesh_sha256}));

sealed record Sub(Vector3d[] pos,Vector3d[] nrm,double[] uv,int[] tri,(int joint,double weight)[][] weights);
readonly record struct Vector3d(double X,double Y,double Z)
{
    public static readonly Vector3d Zero=new(0,0,0);
    public static Vector3d operator+(Vector3d a,Vector3d b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static Vector3d operator-(Vector3d a,Vector3d b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static Vector3d operator-(Vector3d a)=>new(-a.X,-a.Y,-a.Z);
    public double Length()=>Math.Sqrt(X*X+Y*Y+Z*Z);
}
