// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using OpenMetaverse;
public static class PassiveRekey
{
    // A narrow, explicit compatibility transform, never permission to overwrite native assets.
    // All untouched asset bytes and object geometry are preserved. Inventory is already removed.
    public static void White(string input,string output)
    {
        const string old="5748decc-f629-461c-9a36-a35a221fe21f";
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Preserve previous derivative");
        var report=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input,"report.json"))).RootElement;
        var bytes=File.ReadAllBytes(Path.Combine(input,"staging.oar"));
        if(bytes.Length>64*1024*1024||Hash(bytes)!=report.GetProperty("staging_sha256").GetString())throw new IOException("Expected exact bounded passive derivative");
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes("mutsea.library.asset.v1/"+report.GetProperty("source_sha256").GetString()+"/"+old));
        hash[6]=(byte)((hash[6]&15)|128);hash[8]=(byte)((hash[8]&63)|128);
        string remapped=Guid.ParseExact(Convert.ToHexString(hash.AsSpan(0,16)),"N").ToString();var newId=new UUID(remapped);var oldId=new UUID(old);
        Directory.CreateDirectory(output);string path=Path.Combine(output,"rekeyed.oar");int references=0;bool asset=false;int members=0;long expanded=0;
        using(var source=new GZipStream(new MemoryStream(bytes),CompressionMode.Decompress))using(var reader=new TarReader(source))
        using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write))using(var gzip=new GZipStream(target,CompressionLevel.Optimal))using(var writer=new TarWriter(gzip,TarEntryFormat.Ustar))
        {
            TarEntry? entry;
            while((entry=reader.GetNextEntry())!=null)
            {
                if(++members>10000||entry.Length>8*1024*1024||(expanded+=entry.Length)>256*1024*1024||entry.EntryType!=TarEntryType.RegularFile)throw new IOException("Passive archive bounds");
                using var memory=new MemoryStream();entry.DataStream!.CopyTo(memory);byte[] data=memory.ToArray();string name=entry.Name;
                if(name=="assets/"+old+"_texture.jp2"){name="assets/"+remapped+"_texture.jp2";asset=true;}
                if(name.StartsWith("objects/"))
                {
                    using var xml=XmlReader.Create(new StringReader(Encoding.UTF8.GetString(data)),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});
                    var doc=XDocument.Load(xml,LoadOptions.PreserveWhitespace);
                    foreach(var node in doc.Descendants("TextureEntry"))
                    {
                        var raw=Convert.FromBase64String(node.Value);var te=new Primitive.TextureEntry(raw,0,raw.Length);bool changed=false;
                        if(te.DefaultTexture?.TextureID==oldId){te.DefaultTexture.TextureID=newId;references++;changed=true;}
                        foreach(var face in te.FaceTextures)if(face?.TextureID==oldId){face.TextureID=newId;references++;changed=true;}
                        if(changed)node.Value=Convert.ToBase64String(te.GetBytes());
                    }
                    foreach(var node in doc.Descendants("SculptTexture").SelectMany(n=>n.HasElements?n.Descendants():new[]{n}))if(node.Value.Trim()==old){node.Value=remapped;references++;}
                    using var encoded=new MemoryStream();using(var w=XmlWriter.Create(encoded,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=false}))doc.Save(w);
                    data=encoded.ToArray();
                }
                var next=new UstarTarEntry(TarEntryType.RegularFile,name){DataStream=new MemoryStream(data),Mode=UnixFileMode.UserRead|UnixFileMode.UserWrite,ModificationTime=DateTimeOffset.UnixEpoch};writer.WriteEntry(next);
            }
        }
        if(!asset)throw new IOException("Archive does not include the colliding blank asset");
        var provenance=new{schema_version=1,transform="mutsea.passive-white-namespace/0.1.0",upstream_source_sha256=report.GetProperty("source_sha256").GetString(),upstream_passive_sha256=Hash(bytes),
            input_asset_id=old,output_asset_id=remapped,references_changed=references,output_sha256=Hash(File.ReadAllBytes(path)),
            reason="Keep imported blank texture bytes without overwriting the runtime's existing texture UUID. Source asset bytes and creator attribution retained."};
        File.WriteAllText(Path.Combine(output,"provenance.json"),JsonSerializer.Serialize(provenance,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASSIVE_REKEY references={references} asset_id={remapped}");
    }
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
