// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Scenes.Serialization;
using OpenMetaverse;
public static class PassiveRekeyTests
{
    public static void Run()
    {
        void Check(bool ok){if(!ok)throw new Exception("Rekey acceptance failed");}
        string root=Path.Combine(Path.GetTempPath(),"mutsea-rekey-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var owner=UUID.Random();var shape=PrimitiveBaseShape.CreateBox();var te=new Primitive.TextureEntry(new UUID("5748decc-f629-461c-9a36-a35a221fe21f"));te.DefaultTexture.RGBA=new Color4(.2f,.4f,.6f,1);shape.TextureEntry=te.GetBytes();
            var part=new SceneObjectPart(owner,shape,new Vector3(120,130,25),Quaternion.Identity,Vector3.Zero){Name="Source bench",Description="Creator attribution retained"};
            var group=new SceneObjectGroup(part);string original=SceneObjectSerializer.ToXml2Format(group);
            using(var file=File.Create(Path.Combine(root,"staging.oar")))using(var gzip=new GZipStream(file,CompressionLevel.Optimal))using(var writer=new TarWriter(gzip,TarEntryFormat.Ustar))
            {
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile,"objects/bench.xml"){DataStream=new MemoryStream(Encoding.UTF8.GetBytes(original))});
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile,"assets/5748decc-f629-461c-9a36-a35a221fe21f_texture.jp2"){DataStream=new MemoryStream(new byte[]{1,2,3})});
            }
            string digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"staging.oar")))) .ToLowerInvariant();
            File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(new{source_sha256=new string('a',64),staging_sha256=digest}));
            string output=Path.Combine(root,"first");PassiveRekey.White(root,output);PassiveRekey.White(root,Path.Combine(root,"second"));
            Check(File.ReadAllBytes(Path.Combine(output,"rekeyed.oar")).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"second/rekeyed.oar"))));
            var provenance=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"provenance.json"))).RootElement;var id=new UUID(provenance.GetProperty("output_asset_id").GetString());
            using(var gzip=new GZipStream(File.OpenRead(Path.Combine(output,"rekeyed.oar")),CompressionMode.Decompress))using(var reader=new TarReader(gzip))
            {
                TarEntry? entry;bool asset=false,model=false;
                while((entry=reader.GetNextEntry())!=null)
                {
                    if(entry.Name.StartsWith("assets/")){using var bytes=new MemoryStream();entry.DataStream!.CopyTo(bytes);Check(bytes.ToArray().SequenceEqual(new byte[]{1,2,3})&&entry.Name.Contains(id.ToString()));asset=true;}
                    else
                    {
                        using var text=new StreamReader(entry.DataStream!);var restored=SceneObjectSerializer.FromXml2Format(text.ReadToEnd())!;
                        Check(restored.RootPart.Name==part.Name&&restored.RootPart.Description==part.Description&&restored.AbsolutePosition==group.AbsolutePosition&&restored.RootPart.CreatorID==part.CreatorID);
                        Check(restored.RootPart.Shape.Textures.DefaultTexture.TextureID==id&&restored.RootPart.Shape.Textures.DefaultTexture.RGBA==part.Shape.Textures.DefaultTexture.RGBA);model=true;
                    }
                }
                Check(asset&&model);
            }
            Console.WriteLine("3 PASSIVE_REKEY tests passed: deterministic bytes; reference/geometry/credit preservation; texture payload preservation");
        }
        finally{Directory.Delete(root,true);}
    }
}
