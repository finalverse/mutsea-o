// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Offline adapter around the inherited Collada loader and LLMesh serializer.
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using OpenMetaverse;
using OpenMetaverse.ImportExport;
using OpenMetaverse.StructuredData;

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
var xml=XDocument.Parse(System.Text.Encoding.UTF8.GetString(daeBytes));
XNamespace ns="http://www.collada.org/2005/11/COLLADASchema";
foreach(var node in xml.Descendants(ns+"node")) {
 node.SetAttributeValue("id",node.Attribute("name")?.Value ?? throw new InvalidDataException("Unnamed node."));
 if(!node.Elements(ns+"matrix").Any())node.AddFirst(new XElement(ns+"matrix","1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1"));
}
var adapted=Path.Combine(output,"adapted.dae");xml.Save(adapted);
var prims=new ColladaLoader().Load(adapted,false);
var names=root.GetProperty("entities").EnumerateArray().ToDictionary(e=>e.GetProperty("local_id").GetString()!);
if(prims.Count==0||prims.Count>32||prims.Any(p=>!names.ContainsKey(p.ID)))throw new InvalidDataException("Geometry does not match semantic manifest.");
var triangles=prims.Sum(p=>p.Faces.Sum(f=>f.Indices.Count/3));
if(triangles!=root.GetProperty("budget").GetProperty("triangles").GetInt32()||triangles>4096)throw new InvalidDataException("Triangle budget or manifest mismatch.");
var parts=new List<object>();
foreach(var p in prims){
 if(p.Faces.Count is <1 or >8||p.Faces.Any(f=>f.Material.TextureData!=null||f.Material.Texture!=null))throw new InvalidDataException("This initial adapter supports untextured, unrigged geometry only.");
 // Retain the complete silhouette for these small assets at all distances.
 // All four LODs share the same bounded compressed block, not four copies.
 using var memory=new MemoryStream(p.Asset);var header=(OSDMap)OSDParser.DeserializeLLSDBinary(memory);var offset=(int)memory.Position;
 foreach(var lod in new[]{"medium_lod","low_lod","lowest_lod"})header[lod]=header["high_lod"];
 var h=OSDParser.SerializeLLSDBinary(header,false);var bytes=h.Concat(p.Asset.Skip(offset)).ToArray();
 string file="mesh-"+parts.Count+".llmesh";File.WriteAllBytes(Path.Combine(output,file),bytes);
 var semantic=names[p.ID];
 parts.Add(new {local_id=p.ID,name=semantic.GetProperty("name").GetString(),semantic_type=semantic.GetProperty("semantic_type").GetString(),description=semantic.GetProperty("description").GetString(),
  position=new[]{(double)p.Position.X,p.Position.Y,p.Position.Z},scale=new[]{(double)p.Scale.X,p.Scale.Y,p.Scale.Z},rotation=new[]{(double)p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W},
  faces=p.Faces.Select(f=>new {color=new[]{(double)f.Material.DiffuseColor.R,f.Material.DiffuseColor.G,f.Material.DiffuseColor.B,f.Material.DiffuseColor.A},fullbright=f.Material.ID.Contains("light",StringComparison.OrdinalIgnoreCase)}).ToArray(),
  mesh_path=file,mesh_sha256=Hash(bytes),triangles=p.Faces.Sum(f=>f.Indices.Count/3),semantics=new{tags=semantic.GetProperty("tags"),affordances=semantic.GetProperty("affordances"),relationships=semantic.GetProperty("relationships")}});
}
var compiled=new {schema_version=2,package_id=root.GetProperty("package_id").GetString(),version=root.GetProperty("version").GetString(),title=root.GetProperty("title").GetString(),
 manifest_sha256=Hash(manifest),license=root.GetProperty("license").GetProperty("spdx").GetString(),parts,
 lod_policy="Full geometry shared at all four LODs; bounded to 4096 triangles for initial small-asset gate",collision_policy="Inherited convex bounding hull; static, nonphysical; validate in viewer"};
File.WriteAllText(Path.Combine(output,"compiled.json"),JsonSerializer.Serialize(compiled,new JsonSerializerOptions{WriteIndented=true})+"\n");
Console.WriteLine(JsonSerializer.Serialize(new {compiled=true,compiled.package_id,compiled.version,parts=parts.Count,triangles,bytes=Directory.GetFiles(output).Sum(f=>new FileInfo(f).Length)}));
