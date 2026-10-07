// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Uses the inherited MutSea serializer; does not bypass simulator import or permissions.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Scenes.Serialization;
using OpenMetaverse;
using PermissionMask = MutSea.Framework.PermissionMask;

if (args.Length != 2 || !UUID.TryParse(args[0], out UUID owner) || owner == UUID.Zero)
{
    Console.Error.WriteLine("Usage: MutSea.StarterScene <estate-owner-uuid> <new-output-directory>");
    return 2;
}
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Output directory must be empty. Existing worlds are never overwritten.");
Directory.CreateDirectory(output);
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var objects = new List<SceneObjectGroup>();
var ids = new HashSet<UUID>();
// White is a built-in asset in the MutSea default asset set (not an external texture).
UUID white = new("5748decc-f629-461c-9a36-a35a221fe21f");
Color4 stone = new(.24f,.28f,.34f,1), purple = new(.40f,.26f,.68f,1), silver = new(.76f,.81f,.88f,1);
Color4 wood = new(.42f,.29f,.19f,1), leaf = new(.18f,.40f,.27f,1);
void Add(string name, string type, Vector3 pos, Vector3 size, Color4 color, string text = "", bool sphere = false)
{
    if (size.X <= 0 || size.Y <= 0 || size.Z <= 0 || pos.X-size.X/2 < 0 || pos.Y-size.Y/2 < 0 || pos.X+size.X/2 > 256 || pos.Y+size.Y/2 > 256)
        throw new InvalidDataException("Object outside the region: " + name);
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("mutsea-harbor-v1/" + name));
    hash[6] = (byte)((hash[6] & 0x0f) | 0x50); hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
    var id = new UUID(new Guid(hash.AsSpan(0, 16)));
    if (!ids.Add(id)) throw new InvalidDataException("Duplicate object identity: " + name);
    PrimitiveBaseShape shape = sphere ? PrimitiveBaseShape.CreateSphere() : PrimitiveBaseShape.CreateBox();
    shape.Scale = size;
    var texture = new Primitive.TextureEntry(white);
    texture.DefaultTexture.RGBA = color;
    shape.TextureEntry = texture.GetBytes();
    var group = new SceneObjectGroup(owner, pos, shape);
    var part = group.RootPart;
    part.UUID = id; part.Name = name; part.Description = "MutSea Harbor v1 | " + type;
    part.CreatorID = owner; part.OwnerID = owner; part.LastOwnerID = owner;
    part.Scale = size; part.Text = text; part.Color = System.Drawing.Color.White;
    part.BaseMask = part.OwnerMask = (uint)PermissionMask.All;
    part.NextOwnerMask = (uint)(PermissionMask.Copy | PermissionMask.Modify | PermissionMask.Transfer);
    part.EveryoneMask = 0; part.GroupMask = 0; part.Flags = PrimFlags.None;
    part.CreationDate = 1791331200; // fixed provenance timestamp for reproducible content
    objects.Add(group);
}
Add("Welcome Plaza", "arrival platform", new(128,128,25.1f), new(28,28,.2f), stone);
Add("Arrival Marker", "spawn marker", new(128,128,25.23f), new(4,4,.06f), purple);
Add("Welcome to MutSea", "welcome sign", new(145,144,27), new(10,.5f,4), purple,
    "MUTSEA HARBOR\nExplore • Create • Meet\nFinalverse world client");
Add("North Promenade", "walking path", new(128,173,25.12f), new(6,54,.24f), silver);
Add("Lake Walk", "walking path", new(162,128,25.12f), new(42,5,.24f), silver);
Add("Workshop Walk", "walking path", new(93,128,25.12f), new(42,5,.24f), silver);
Add("Harbor Pavilion Floor", "meeting place", new(128,192,25.2f), new(24,20,.4f), stone);
foreach (float x in new[]{117f,139f}) foreach(float y in new[]{183f,201f})
    Add($"Pavilion Column {x}-{y}", "architectural support", new(x,y,28.5f), new(.8f,.8f,6.6f), silver);
Add("Pavilion Roof", "shelter", new(128,192,32), new(26,22,.6f), purple);
for (int i=0;i<3;i++)
{
    Add($"Pavilion Bench {i+1}", "seat", new(122+i*6,197,25.8f), new(4,1,.45f), wood);
    Add($"Pavilion Bench Back {i+1}", "seat back", new(122+i*6,197.45f,26.25f), new(4,.15f,1), wood);
}
Add("Creation Practice Pad", "building practice area", new(67,128,25.1f), new(28,28,.2f), stone);
Add("Workshop Sign", "information sign", new(67,144,27), new(10,.4f,4), purple,
    "CREATION PRACTICE\nBuild with your developer account\nAI tools arrive in a later milestone");
Add("Practice Cube", "editable demonstration object", new(64,128,26.3f), new(2,2,2), purple);
Add("Practice Sphere", "editable demonstration object", new(70,128,26.3f), new(2,2,2), silver, sphere:true);
Add("Lake Overlook", "viewpoint", new(184,128,25.25f), new(12,16,.5f), wood);
Add("Lake Sign", "information sign", new(184,137,27), new(8,.4f,3), purple, "HARBOR LAKE\nA quiet place to meet");
// An open, collision-safe plaza and footpaths remain clear of vegetation.
foreach (var p in new[]{(100f,100f),(155f,101f),(91f,173f),(164f,182f),(81f,202f),(205f,187f),(54f,92f),(154f,217f)})
{
    Add($"Tree Trunk {p.Item1}-{p.Item2}", "tree trunk", new(p.Item1,p.Item2,27), new(.9f,.9f,4), wood);
    Add($"Tree Crown {p.Item1}-{p.Item2}", "tree canopy", new(p.Item1,p.Item2,30), new(6,6,6), leaf, sphere:true);
}
string sceneFile = Path.Combine(output,"harbor.xml");
using (var writer = new XmlTextWriter(sceneFile, Encoding.UTF8) { Formatting = Formatting.Indented })
{
    writer.WriteStartElement("scene");
    foreach(var obj in objects)
    {
        SceneObjectSerializer.SOGToXml2(writer,obj,new Dictionary<string,object>());
    }
    writer.WriteEndElement();
}
// Exercise the real loader format before producing a release artifact.
var doc = new XmlDocument(); doc.Load(sceneFile);
int count = 0;
foreach(XmlNode node in doc.DocumentElement!.ChildNodes)
{
    var restored = SceneObjectSerializer.FromXml2Format(node.OuterXml);
    if (restored == null || !ids.Contains(restored.UUID) || restored.RootPart.OwnerID != owner || restored.RootPart.Flags.HasFlag(PrimFlags.Physics))
        throw new InvalidDataException("Simulator serialization round-trip failed.");
    count++;
}
if(count != objects.Count) throw new InvalidDataException("Object count changed on import.");
using(var terrain = new BinaryWriter(File.Create(Path.Combine(output,"harbor.r32"))))
    for(int y=0;y<256;y++) for(int x=0;x<256;x++)
    {
        // Flat walkable core at 25m; lake in east, sea around the island.
        float edge = Math.Min(Math.Min(x,255-x),Math.Min(y,255-y));
        float height = 18 + 7 * Math.Clamp(edge/28,0,1);
        float lake = MathF.Sqrt(MathF.Pow((x-214)/23f,2)+MathF.Pow((y-125)/34f,2));
        if(lake < 1.25f) height = Math.Min(height,17 + 8*Math.Clamp((lake-.7f)/.55f,0,1));
        terrain.Write(height);
    }
File.WriteAllText(Path.Combine(output,"manifest.json"), JsonSerializer.Serialize(new {
    format="mutsea-harbor-v1", name="MutSea Harbor", owner=owner.ToString(), objects=count,
    landing=new[]{128,128,27}, water_height=20,
    source="MutSea.StarterScene using MutSea SceneObjectSerializer", ai_actions_enabled=false,
    files=new[]{"harbor.xml","harbor.r32"}.Select(f=>new {path=f,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output,f)))).ToLowerInvariant()})
},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Created MutSea Harbor: {count} objects; real XML2 round-trip passed; output {output}");
return 0;
