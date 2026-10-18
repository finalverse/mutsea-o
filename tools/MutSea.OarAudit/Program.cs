// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Offline native deserialization of a previously quarantined derivative; no scene or service starts.
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MutSea.Region.Framework.Scenes.Serialization;
using OpenMetaverse;
if(args.Length==1&&args[0]=="--self-test"){PassiveRekeyTests.Run();return;}
if(args.Length==3&&args[2]=="--rekey-white"){PassiveRekey.White(args[0],args[1]);return;}
if(args.Length!=2)throw new ArgumentException("Expected passive directory and a new audit receipt");
string root=Path.GetFullPath(args[0]);if(File.Exists(args[1]))throw new IOException("Preserve previous audit");
var report=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"report.json"))).RootElement;
var f=new FileInfo(Path.Combine(root,"staging.oar"));if(!f.Exists||f.Length>64*1024*1024||f.LinkTarget!=null)throw new IOException("Passive derivative size/link limit");
byte[] archive=File.ReadAllBytes(f.FullName);
if(Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant()!=report.GetProperty("staging_sha256").GetString())throw new IOException("Passive hash differs");
var assets=new HashSet<UUID>();var textures=new HashSet<UUID>();var geometry=new HashSet<UUID>();int groups=0,parts=0,members=0;long expanded=0;
using(var stream=new GZipStream(new MemoryStream(archive),CompressionMode.Decompress))using(var tar=new TarReader(stream))
{
    TarEntry? entry;
    while((entry=tar.GetNextEntry())!=null)
    {
        if(++members>10000||entry.Length>8*1024*1024||(expanded+=entry.Length)>256*1024*1024)throw new IOException("Passive member bounds");
        if(entry.EntryType!=TarEntryType.RegularFile||entry.Name.Contains(".."))throw new IOException("Unexpected passive member");
        if(entry.Name.StartsWith("assets/"))assets.Add(new UUID(Path.GetFileName(entry.Name).Split('_')[0]));
        if(!entry.Name.StartsWith("objects/"))continue;
        using var reader=new StreamReader(entry.DataStream!,Encoding.UTF8);
        var group=SceneObjectSerializer.FromXml2Format(reader.ReadToEnd())??throw new IOException("Native object decode failed");groups++;
        foreach(var part in group.Parts)
        {
            parts++;if(part.Inventory.GetInventoryItems().Count!=0)throw new IOException("Task inventory survived quarantine");
            var te=part.Shape.Textures;
            if(te.DefaultTexture!=null)textures.Add(te.DefaultTexture.TextureID);
            foreach(var face in te.FaceTextures)if(face!=null)textures.Add(face.TextureID);
            if(part.Shape.SculptEntry)geometry.Add(part.Shape.SculptTexture);
        }
    }
}
textures.Remove(UUID.Zero);geometry.Remove(UUID.Zero);
if(parts!=report.GetProperty("scene_parts").GetInt32()||groups!=report.GetProperty("object_groups").GetInt32())throw new IOException("Native counts differ from quarantine");
var receipt=new{schema_version=1,staging_sha256=report.GetProperty("staging_sha256").GetString(),groups,parts,assets=assets.Count,
    referenced_textures=textures.Select(x=>x.ToString()).Order().ToArray(),missing_textures=textures.Except(assets).Select(x=>x.ToString()).Order().ToArray(),
    missing_geometry=geometry.Except(assets).Select(x=>x.ToString()).Order().ToArray(),scripts_enabled=false,
    limitations="Unresolved references may be native built-ins; verify against runtime assets. Decode, topology, rendered quality and native world streaming are separate gates."};
using(var output=new FileStream(args[1],FileMode.CreateNew,FileAccess.Write)){JsonSerializer.Serialize(output,receipt,new JsonSerializerOptions{WriteIndented=true});output.Flush(true);}
Console.WriteLine($"NATIVE_AUDIT groups={groups} parts={parts} unresolved_textures={textures.Except(assets).Count()} unresolved_geometry={geometry.Except(assets).Count()}");
