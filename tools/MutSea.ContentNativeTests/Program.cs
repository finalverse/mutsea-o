// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Scenes.Serialization;
using OpenMetaverse.StructuredData;
using System.Text.Json;

int passed = 0;
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
void Assert(bool ok) { if (!ok) throw new Exception("Assertion failed."); }
var owner = UUID.Random();
SceneObjectPart Part(string name) => new(owner, PrimitiveBaseShape.CreateMesh(1, UUID.Random()),
    new Vector3(10, 10, 25), Quaternion.Identity, Vector3.Zero) { Name = name };
var root = Part("root"); root.LocalId = 1;
var group = new SceneObjectGroup(root);
var child = Part("child"); child.OffsetPosition = new Vector3(0, 0, 1);
child.RotationOffset = Quaternion.Identity * Quaternion.Inverse(Quaternion.Identity);
group.AddPart(child); child.ParentID = root.LocalId;
var hash = ContentFingerprint.Hash(group);
Test("signed zero from native quaternion math survives persistence", () => {
    child.RotationOffset = Quaternion.Identity;
    Assert(ContentFingerprint.Hash(group) == hash);
});
Test("native XML persistence preserves canonical fingerprint", () => {
    var restored = SceneObjectSerializer.FromXml2Format(SceneObjectSerializer.ToXml2Format(group));
    Assert(restored != null && ContentFingerprint.Hash(restored) == hash);
});
Test("link numbering does not change persisted content identity", () => {
    child.LinkNum = 9; Assert(ContentFingerprint.Hash(group) == hash); child.LinkNum = 2;
});
Test("later transform edits block undo", () => {
    child.OffsetPosition = new Vector3(0, 0, 1.01f);
    Assert(!ContentFingerprint.Matches(group, hash)); child.OffsetPosition = new Vector3(0, 0, 1);
});
Test("later permissions and ownership edits block undo", () => {
    var mask = child.OwnerMask; child.OwnerMask = 0; Assert(!ContentFingerprint.Matches(group, hash)); child.OwnerMask = mask;
    child.OwnerID = UUID.Random(); Assert(!ContentFingerprint.Matches(group, hash)); child.OwnerID = owner;
});
Test("later mesh and material edits block undo", () => {
    var mesh = child.Shape.SculptTexture; child.Shape.SculptTexture = UUID.Random(); Assert(!ContentFingerprint.Matches(group, hash)); child.Shape.SculptTexture = mesh;
    var texture = child.Shape.TextureEntry;
    var te = new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE); te.DefaultTexture.RGBA = new Color4(.2f, .3f, .4f, 1);
    child.Shape.TextureEntry = te.GetBytes(); Assert(!ContentFingerprint.Matches(group, hash)); child.Shape.TextureEntry = texture;
});
var factors=new ContentMaterialFactors("wood",[.31,.12,.04,1],0,.72,[0,0,0],"OPAQUE",.5,false);
Test("native material wrapper carries linear factors and no external resource",()=>{
    var bytes=ContentMaterialAsset.Build(factors);
    Assert(ContentMaterialAsset.Matches(bytes,factors));
    var wrapper=(OSDMap)OSDParser.DeserializeLLSDBinary(bytes);
    using var json=JsonDocument.Parse(wrapper["data"].AsString());
    var material=json.RootElement.GetProperty("materials")[0];
    Assert(material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[0].GetDouble()==.31);
    Assert(!json.RootElement.TryGetProperty("textures",out _)&&!json.RootElement.TryGetProperty("images",out _));
    Assert(!ContentMaterialAsset.Matches(bytes,factors with{roughness_factor=.1}));
    wrapper["data"]=OSD.FromString(wrapper["data"].AsString().Replace("\"name\":\"wood\"","\"name\":\"wood\",\"extensions\":{}"));
    Assert(!ContentMaterialAsset.Matches(OSDParser.SerializeLLSDBinary(wrapper,true),factors));
});
Test("PBR face bindings persist and later binding edits block undo",()=>{
    var id=UUID.Random();
    child.Shape.RenderMaterials=new Primitive.RenderMaterials {entries=[new(){te_index=0,id=id}]};
    var boundHash=ContentFingerprint.Hash(group);
    var restored=SceneObjectSerializer.FromXml2Format(SceneObjectSerializer.ToXml2Format(group));
    Assert(restored!=null&&ContentFingerprint.Hash(restored)==boundHash);
    Assert(restored!.GetPart(child.UUID).Shape.RenderMaterials.entries[0].id==id);
    child.Shape.RenderMaterials.entries[0].id=UUID.Random();
    Assert(!ContentFingerprint.Matches(group,boundHash));
    child.Shape.RenderMaterials=null;
});
Test("native sit target persists and later seat edits block undo",()=>{
    child.SitTargetPosition=new Vector3(0,0,.7f);child.SitTargetOrientation=Quaternion.Identity;
    var seatHash=ContentFingerprint.Hash(group);
    var restored=SceneObjectSerializer.FromXml2Format(SceneObjectSerializer.ToXml2Format(group));
    Assert(restored!=null&&ContentFingerprint.Hash(restored)==seatHash);
    child.SitTargetPosition=new Vector3(0,0,.8f);
    Assert(!ContentFingerprint.Matches(group,seatHash));
    child.SitTargetPosition=Vector3.Zero;
});
Test("noncolliding native child persists and physics edits block undo",()=>{
    // Set the shape before native linking, as the executor does. Changing a
    // linked part needs a live physics scene, absent from this serialization test.
    var foliage=Part("foliage");foliage.PhysicsShapeType=(byte)PhysShapeType.none;
    var plant=new SceneObjectGroup(Part("trunk"));plant.RootPart.LocalId=1;
    plant.AddPart(foliage);foliage.ParentID=1;
    var collisionHash=ContentFingerprint.Hash(plant);var xml=SceneObjectSerializer.ToXml2Format(plant);
    var restored=SceneObjectSerializer.FromXml2Format(xml);
    Assert(restored!=null&&restored.GetPart(foliage.UUID).PhysicsShapeType==(byte)PhysShapeType.none&&ContentFingerprint.Hash(restored)==collisionHash);
    var edited=SceneObjectSerializer.FromXml2Format(xml.Replace("<PhysicsShapeType>1</PhysicsShapeType>","<PhysicsShapeType>2</PhysicsShapeType>"));
    Assert(edited!=null&&!ContentFingerprint.Matches(edited,collisionHash));
});
if(args.Length is not (0 or 1 or 3))throw new ArgumentException("Expected catalog, optionally package ID and version.");
Test("native linear point light persists and later light edits block undo",()=>{
    ContentLightAdapter.Bind(child.Shape,new([1,.7454,.4179],.8,3.5,1));
    var lightHash=ContentFingerprint.Hash(group);
    var restored=SceneObjectSerializer.FromXml2Format(SceneObjectSerializer.ToXml2Format(group));
    Assert(restored!=null&&ContentFingerprint.Hash(restored)==lightHash);
    var observed=restored!.GetPart(child.UUID).Shape;
    Assert(observed.LightEntry&&Math.Abs(observed.LightColorG-.7454)<.0001&&Math.Abs(observed.LightIntensity-.8)<.0001&&observed.LightRadius==3.5f);
    var wire=new PrimitiveBaseShape();wire.ReadInExtraParamsBytes(child.Shape.ExtraParamsToBytes());
    Assert(wire.LightEntry&&Math.Abs(wire.LightColorG-.7454)<1d/255&&Math.Abs(wire.LightIntensity-.8)<1d/255&&wire.LightRadius==3.5f);
    child.Shape.LightRadius=3;
    Assert(!ContentFingerprint.Matches(group,lightHash));child.Shape.LightEntry=false;
});
if(args.Length>0)
{
    var catalog=new ContentCatalog(args[0],ContentMaterialAsset.Matches);
    var compiled=catalog.Get(args.Length==3?args[1]:"mutsea.starter.bench",args.Length==3?args[2]:"0.1.6");
    Test("actual compiled package material and reduced LOD contract",()=>{
        Assert(compiled.package.schema_version==3&&compiled.materials.Length>0);
        if(args.Length==1)
        {
            Assert(compiled.package.parts.Length==13&&compiled.materials.Length==2);
            Assert(compiled.package.parts[0].seat is {capacity:1});
        }
        var counts=new int[4];var levels=new[]{"high_lod","medium_lod","low_lod","lowest_lod"};
        foreach(var bytes in compiled.meshes)
        {
            using var stream=new MemoryStream(bytes);var header=(OSDMap)OSDParser.DeserializeLLSDBinary(stream);int start=(int)stream.Position;
            Assert(levels.Select(level=>((OSDMap)header[level])["offset"].AsInteger()).Distinct().Count()==4);
            for(int i=0;i<4;i++)
            {
                var range=(OSDMap)header[levels[i]];
                var faces=(OSDArray)Helpers.ZDecompressOSD(bytes.Skip(start+range["offset"].AsInteger()).Take(range["size"].AsInteger()).ToArray());
                foreach(OSDMap face in faces)
                {
                    var indices=face["TriangleList"].AsBinary();Assert(indices.Length>0&&indices.Length%6==0);
                    counts[i]+=indices.Length/6;
                    Assert(face["Position"].AsBinary().Length==face["Normal"].AsBinary().Length);
                }
            }
        }
        Assert(counts.SequenceEqual(compiled.package.lod_triangles!));
        Console.WriteLine("Verified native LOD triangles: "+string.Join(" / ",counts));
    });
    if(compiled.package.parts.Any(p=>p.collision!=null))Test("native explicit hull uses the pinned normalized bounds",()=>{
        for(int part=0;part<compiled.package.parts.Length;part++)
        {
            var collision=compiled.package.parts[part].collision;if(collision?.shape!="box")continue;
            var bytes=compiled.meshes[part];using var stream=new MemoryStream(bytes);
            var header=(OSDMap)OSDParser.DeserializeLLSDBinary(stream);var range=(OSDMap)header["physics_convex"];
            var hull=(OSDMap)Helpers.ZDecompressOSD(bytes.Skip((int)stream.Position+range["offset"].AsInteger()).Take(range["size"].AsInteger()).ToArray());
            Assert(hull["Min"].AsVector3()==new Vector3(-.5f,-.5f,-.5f)&&hull["Max"].AsVector3()==new Vector3(.5f,.5f,.5f));
            var points=hull["BoundingVerts"].AsBinary();Assert(points.Length==48);
            for(int corner=0;corner<8;corner++)for(int axis=0;axis<3;axis++)
            {
                double normalized=BitConverter.ToUInt16(points,corner*6+axis*2)/65535.0-.5;
                double expected=(corner&(1<<axis))==0?collision.minimum![axis]:collision.maximum![axis];
                Assert(Math.Abs(normalized-expected)<=1.0/65535.0);
            }
        }
    });
    if(compiled.package.parts.Any(p=>p.collision?.shape=="box"))Test("compiled collision cannot contradict its original manifest",()=>{
        var temp=Path.Combine(Path.GetTempPath(),"mutsea-collision-"+Guid.NewGuid());Directory.CreateDirectory(temp);
        try
        {
            var folder=Path.Combine(temp,"package");Directory.CreateDirectory(folder);
            var original=Directory.GetDirectories(args[0]).Single(dir=>File.ReadAllText(Path.Combine(dir,"compiled.json")).Contains(compiled.package.manifest_sha256,StringComparison.Ordinal));
            foreach(var file in Directory.GetFiles(original))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)));
            var parts=compiled.package.parts.ToArray();int index=Array.FindIndex(parts,p=>p.collision?.shape=="box");
            var bounds=parts[index].collision!.minimum!.ToArray();bounds[0]+=.001;
            parts[index]=parts[index] with{collision=parts[index].collision! with{minimum=bounds}};
            File.WriteAllText(Path.Combine(folder,"compiled.json"),JsonSerializer.Serialize(compiled.package with{parts=parts}));
            try{new ContentCatalog(temp,ContentMaterialAsset.Matches);throw new Exception("Accepted altered collision.");}catch(InvalidDataException){}
        }
        finally{Directory.Delete(temp,true);}
    });
    if(compiled.package.parts.Any(p=>p.light!=null))Test("compiled light cannot contradict its original manifest",()=>{
        var temp=Path.Combine(Path.GetTempPath(),"mutsea-light-"+Guid.NewGuid());Directory.CreateDirectory(temp);
        try
        {
            var folder=Path.Combine(temp,"package");Directory.CreateDirectory(folder);
            var original=Directory.GetDirectories(args[0]).Single(dir=>File.ReadAllText(Path.Combine(dir,"compiled.json")).Contains(compiled.package.manifest_sha256,StringComparison.Ordinal));
            foreach(var file in Directory.GetFiles(original))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)));
            var parts=compiled.package.parts.ToArray();int index=Array.FindIndex(parts,p=>p.light!=null);
            parts[index]=parts[index] with{light=parts[index].light! with{radius=3.4}};
            File.WriteAllText(Path.Combine(folder,"compiled.json"),JsonSerializer.Serialize(compiled.package with{parts=parts}));
            try{new ContentCatalog(temp,ContentMaterialAsset.Matches);throw new Exception("Accepted altered lighting.");}catch(InvalidDataException){}
        }
        finally{Directory.Delete(temp,true);}
    });
    Test("PBR catalog requires native payload validation",()=>{
        try{new ContentCatalog(args[0]);throw new Exception("Accepted without native validator.");}catch(InvalidDataException){}
        try{new ContentCatalog(args[0],(_,_)=>false);throw new Exception("Accepted mismatched native material.");}catch(InvalidDataException){}
    });
}
Console.WriteLine($"{passed} native content fingerprint tests passed");
