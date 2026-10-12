// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Offline adapter around the inherited Collada loader and LLMesh serializer.
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using OpenMetaverse;
using OpenMetaverse.ImportExport;
using OpenMetaverse.StructuredData;
using Finalverse.World;

if(args.Length!=2)throw new ArgumentException("Expected validated package directory and NEW output directory.");
var source=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);
if(Directory.Exists(output))throw new InvalidDataException("Preserve existing compiled packages; choose new output.");
var manifest=File.ReadAllBytes(Path.Combine(source,"package.json"));
using var doc=JsonDocument.Parse(manifest);var root=doc.RootElement;
if(root.GetProperty("schema_version").GetInt32()!=1||root.GetProperty("kind").GetString()!="asset")throw new InvalidDataException("Only v1 asset packages supported.");
var dae=root.GetProperty("files").EnumerateArray().Single(f=>f.GetProperty("path").GetString()=="runtime.dae");
var daeBytes=File.ReadAllBytes(Path.Combine(source,"runtime.dae"));
string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
if(Hash(daeBytes)!=dae.GetProperty("sha256").GetString())throw new InvalidDataException("Validated Collada hash changed.");
Settings.LOG_LEVEL=Helpers.LogLevel.Error;
Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"package.json"),manifest);
// The inherited loader skips legal identity-transform nodes without Items.
// Normalize that representation, retaining names as stable package-local IDs.
XNamespace ns="http://www.collada.org/2005/11/COLLADASchema";
List<ModelPrim> LoadDae(byte[] input,string destination)
{
 var xml=XDocument.Parse(System.Text.Encoding.UTF8.GetString(input));
 foreach(var node in xml.Descendants(ns+"node")) {
  node.SetAttributeValue("id",node.Attribute("name")?.Value ?? throw new InvalidDataException("Unnamed node."));
  if(!node.Elements(ns+"matrix").Any())node.AddFirst(new XElement(ns+"matrix","1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1"));
 }
 xml.Save(destination);return new ColladaLoader().Load(destination,false);
}
var prims=LoadDae(daeBytes,Path.Combine(output,"adapted.dae"));
var names=root.GetProperty("entities").EnumerateArray().ToDictionary(e=>e.GetProperty("local_id").GetString()!);
if(prims.Count==0||prims.Count>32||prims.Any(p=>!names.ContainsKey(p.ID)))throw new InvalidDataException("Geometry does not match semantic manifest.");
var triangles=prims.Sum(p=>p.Faces.Sum(f=>f.Indices.Count/3));
if(triangles!=root.GetProperty("budget").GetProperty("triangles").GetInt32()||triangles>4096)throw new InvalidDataException("Triangle budget or manifest mismatch.");
var lowerLods=new Dictionary<string,Dictionary<string,ModelPrim>>();
var lodCounts=new List<int>{triangles};
if(root.GetProperty("definition").TryGetProperty("lods",out var declaredLods))
 foreach(var level in new[]{"medium","low","lowest"})
 {
  var declared=declaredLods.GetProperty(level);string path=declared.GetProperty("path").GetString()!;
  if(Path.GetFileName(path)!=path||path.Contains(".."))throw new InvalidDataException("Invalid LOD path.");
  var file=root.GetProperty("files").EnumerateArray().Single(f=>f.GetProperty("path").GetString()==path);
  var payload=File.ReadAllBytes(Path.Combine(source,path));
  if(Hash(payload)!=file.GetProperty("sha256").GetString())throw new InvalidDataException("LOD payload hash changed.");
  var models=LoadDae(payload,Path.Combine(output,"adapted-"+level+".dae"));
  int count=models.Sum(p=>p.Faces.Sum(f=>f.Indices.Count/3));
  if(models.Count!=prims.Count||models.Select(p=>p.ID).Distinct().Count()!=prims.Count||models.Any(p=>!prims.Any(h=>h.ID==p.ID))||count!=declared.GetProperty("triangles").GetInt32()||count>=lodCounts.Last()||count<=0)throw new InvalidDataException("LOD semantic identity or reduction mismatch.");
  lowerLods.Add(level,models.ToDictionary(p=>p.ID));lodCounts.Add(count);
 }
byte[] Block(byte[] asset,string name)
{
 using var stream=new MemoryStream(asset);var header=(OSDMap)OSDParser.DeserializeLLSDBinary(stream);var entry=(OSDMap)header[name];
 return asset.Skip((int)stream.Position+entry["offset"].AsInteger()).Take(entry["size"].AsInteger()).ToArray();
}
var collisionDefinitions=root.GetProperty("definition").TryGetProperty("collision",out var declaredCollision)?declaredCollision.Deserialize<Dictionary<string,ContentCollision>>(ContentCatalog.Json)!:new();
byte[] Mesh(ModelPrim high)
{
 using var stream=new MemoryStream(high.Asset);var header=(OSDMap)OSDParser.DeserializeLLSDBinary(stream);
 var blocks=new List<(string name,byte[] data)>{("high_lod",Block(high.Asset,"high_lod"))};
 foreach(var level in new[]{"medium","low","lowest"})
 {
  if(!lowerLods.TryGetValue(level,out var models)) {header[level+"_lod"]=header["high_lod"];continue;}
  var low=models[high.ID];
  if(low.Faces.Count!=high.Faces.Count||!low.Faces.Select(f=>f.Material.ID).SequenceEqual(high.Faces.Select(f=>f.Material.ID))||low.Faces.Any(f=>f.Indices.Count<3))throw new InvalidDataException("LOD face/material identity changed.");
  var lowRange=low.BoundMax-low.BoundMin;var highRange=high.BoundMax-high.BoundMin;
  foreach(var face in low.Faces)
   for(int i=0;i<face.Vertices.Count;i++)
   {
    var vertex=face.Vertices[i];var p=vertex.Position;
    var absolute=new Vector3((p.X+.5f)*lowRange.X+low.BoundMin.X,(p.Y+.5f)*lowRange.Y+low.BoundMin.Y,(p.Z+.5f)*lowRange.Z+low.BoundMin.Z);
    vertex.Position=new Vector3((absolute.X-high.BoundMin.X)/highRange.X-.5f,(absolute.Y-high.BoundMin.Y)/highRange.Y-.5f,(absolute.Z-high.BoundMin.Z)/highRange.Z-.5f);
    if(vertex.Position.X is < -.5001f or > .5001f||vertex.Position.Y is < -.5001f or > .5001f||vertex.Position.Z is < -.5001f or > .5001f)throw new InvalidDataException("Lower LOD exceeds the high mesh bounds.");
    face.Vertices[i]=vertex;
   }
  low.CreateAsset(UUID.Zero);blocks.Add((level+"_lod",Block(low.Asset,"high_lod")));
 }
 var physics=Block(high.Asset,"physics_convex");
 if(collisionDefinitions.TryGetValue(high.ID,out var collision))
 {
  ContentCatalog.ValidateCollision(collision);
  if(collision.shape=="box")
  {
   using var hull=new MemoryStream();using var writer=new BinaryWriter(hull);
   for(int corner=0;corner<8;corner++)for(int axis=0;axis<3;axis++)
   {
    double value=(corner&(1<<axis))==0?collision.minimum![axis]:collision.maximum![axis];
    writer.Write((ushort)Math.Round((value+.5)*65535));
   }
   physics=Helpers.ZCompressOSD(new OSDMap { ["Min"]=OSD.FromVector3(new Vector3(-.5f,-.5f,-.5f)),["Max"]=OSD.FromVector3(new Vector3(.5f,.5f,.5f)),["BoundingVerts"]=OSD.FromBinary(hull.ToArray())});
  }
 }
 blocks.Add(("physics_convex",physics));
 int offset=0;
 foreach(var block in blocks){header[block.name]=new OSDMap { ["offset"]=OSD.FromInteger(offset),["size"]=OSD.FromInteger(block.data.Length)};offset+=block.data.Length;}
 return OSDParser.SerializeLLSDBinary(header,false).Concat(blocks.SelectMany(b=>b.data)).ToArray();
}
var parts=new List<object>();
var materialDefinitions=root.GetProperty("definition").TryGetProperty("materials",out var declaredMaterials)
 ? declaredMaterials.Deserialize<ContentMaterialFactors[]>(ContentCatalog.Json)! : Array.Empty<ContentMaterialFactors>();
var materialBindings=materialDefinitions.Length>0?root.GetProperty("definition").GetProperty("material_bindings").Deserialize<Dictionary<string,string[]>>(ContentCatalog.Json)!:null;
var seats=root.GetProperty("definition").TryGetProperty("seating",out var declaredSeats)?declaredSeats.Deserialize<Dictionary<string,ContentSeat>>(ContentCatalog.Json)!:new();
var lights=root.GetProperty("definition").TryGetProperty("lighting",out var declaredLights)?declaredLights.Deserialize<Dictionary<string,ContentLight>>(ContentCatalog.Json)!:new();
var materials=new List<ContentMaterial>();
foreach(var definition in materialDefinitions)
{
 var bytes=ContentMaterialAsset.Build(definition);string file="material-"+materials.Count+".llmaterial";File.WriteAllBytes(Path.Combine(output,file),bytes);
 materials.Add(new(definition.local_id,file,Hash(bytes),definition));
}
foreach(var p in prims){
 if(p.Faces.Count is <1 or >8||p.Faces.Any(f=>f.Material.TextureData!=null||f.Material.Texture!=null))throw new InvalidDataException("This initial adapter supports untextured, unrigged geometry only.");
 var bytes=Mesh(p);
 string file="mesh-"+parts.Count+".llmesh";File.WriteAllBytes(Path.Combine(output,file),bytes);
 var semantic=names[p.ID];
 var bindings=materialBindings?[p.ID];
 if(bindings!=null&&(bindings.Length!=p.Faces.Count||!bindings.SequenceEqual(p.Faces.Select(f=>f.Material.ID))))throw new InvalidDataException("Native mesh material names contradict declared bindings.");
 parts.Add(new {local_id=p.ID,name=semantic.GetProperty("name").GetString(),semantic_type=semantic.GetProperty("semantic_type").GetString(),description=semantic.GetProperty("description").GetString(),
  position=new[]{(double)p.Position.X,p.Position.Y,p.Position.Z},scale=new[]{(double)p.Scale.X,p.Scale.Y,p.Scale.Z},rotation=new[]{(double)p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W},
  faces=p.Faces.Select((f,i)=>new {color=new[]{(double)f.Material.DiffuseColor.R,f.Material.DiffuseColor.G,f.Material.DiffuseColor.B,f.Material.DiffuseColor.A},fullbright=bindings==null&&f.Material.ID.Contains("light",StringComparison.OrdinalIgnoreCase),material_id=bindings?[i]}).ToArray(),
  mesh_path=file,mesh_sha256=Hash(bytes),triangles=p.Faces.Sum(f=>f.Indices.Count/3),semantics=new{tags=semantic.GetProperty("tags"),affordances=semantic.GetProperty("affordances"),relationships=semantic.GetProperty("relationships")},seat=seats.GetValueOrDefault(p.ID),collision=collisionDefinitions.GetValueOrDefault(p.ID),light=lights.GetValueOrDefault(p.ID)});
}
var compiled=new {schema_version=materials.Count>0?3:2,package_id=root.GetProperty("package_id").GetString(),version=root.GetProperty("version").GetString(),title=root.GetProperty("title").GetString(),
 manifest_sha256=Hash(manifest),license=root.GetProperty("license").GetProperty("spdx").GetString(),parts,materials=materials.Count>0?materials.ToArray():null,
 lod_policy=lowerLods.Count>0?"Independent reduced LODs in shared high bounds; live transition acceptance pending":"Legacy shared high geometry; production LOD acceptance pending",lod_triangles=lowerLods.Count>0?lodCounts.ToArray():null,
 collision_policy=collisionDefinitions.Count>0?"Pinned normalized small-part box hulls / native no-collision children; static; live walking acceptance pending":"Inherited convex bounding hull per small part; static, nonphysical; validate in viewer"};
var serialized=JsonSerializer.Serialize(compiled,new JsonSerializerOptions{WriteIndented=true});
ContentCatalog.Validate(JsonSerializer.Deserialize<ContentPackage>(serialized,ContentCatalog.Json)!);
File.WriteAllText(Path.Combine(output,"compiled.json"),serialized+"\n");
Console.WriteLine(JsonSerializer.Serialize(new {compiled=true,compiled.package_id,compiled.version,parts=parts.Count,triangles,bytes=Directory.GetFiles(output).Sum(f=>new FileInfo(f).Length)}));
