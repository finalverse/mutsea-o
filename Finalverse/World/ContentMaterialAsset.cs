// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Finalverse.World;

// Native AT_MATERIAL asset wrapper understood by LLGLTFMaterialList. This
// static content gate remains factor-only. Avatar schema 3 may bind an
// original bounded native texture UUID; no external URL, extension or script.
public static class ContentMaterialAsset
{
    public static string Gltf(ContentMaterialFactors f)
    {
        ContentCatalog.ValidateMaterial(f);
        return JsonSerializer.Serialize(new {asset=new {version="2.0",generator="MutSea Starter Library"},
            materials=new[]{new {name=f.local_id,pbrMetallicRoughness=new {baseColorFactor=f.base_color_linear,
                metallicFactor=f.metallic_factor,roughnessFactor=f.roughness_factor},emissiveFactor=f.emissive_linear,
                alphaMode=f.alpha_mode,alphaCutoff=f.alpha_cutoff,doubleSided=f.double_sided}}});
    }
    public static byte[] Build(ContentMaterialFactors f)=>OSDParser.SerializeLLSDBinary(new OSDMap {
        ["version"]=OSD.FromString("1.1"),["type"]=OSD.FromString("GLTF 2.0"),["data"]=OSD.FromString(Gltf(f))},true);
    // Optional avatar surface: a content-derived native UUID, never a remote URI.
    // Existing factor-only serialization stays byte-identical.
    public static string Gltf(ContentMaterialFactors f,UUID? texture)
    {
        if(texture==null)return Gltf(f);
        ContentCatalog.Require(texture!=UUID.Zero,"A texture UUID must be nonzero.");
        var data=JsonNode.Parse(Gltf(f))!;
        data["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]=new JsonObject{["index"]=0,["texCoord"]=0};
        data["images"]=new JsonArray(new JsonObject{["uri"]=texture.Value.ToString()});
        data["textures"]=new JsonArray(new JsonObject{["source"]=0});
        return data.ToJsonString();
    }
    public static byte[] Build(ContentMaterialFactors f,UUID? texture)=>texture==null?Build(f):OSDParser.SerializeLLSDBinary(new OSDMap {
        ["version"]=OSD.FromString("1.1"),["type"]=OSD.FromString("GLTF 2.0"),["data"]=OSD.FromString(Gltf(f,texture))},true);
    public static bool Matches(byte[] bytes,ContentMaterialFactors f)
    {
        try{var map=(OSDMap)OSDParser.DeserializeLLSDBinary(bytes);
            return map.Count==3&&map["version"].AsString()=="1.1"&&map["type"].AsString()=="GLTF 2.0"&&map["data"].AsString()==Gltf(f);}
        catch{return false;}
    }
    public static bool Matches(byte[] bytes,ContentMaterialFactors f,UUID? texture)
    {
        if(texture==null)return Matches(bytes,f);
        try{var map=(OSDMap)OSDParser.DeserializeLLSDBinary(bytes);
            return map.Count==3&&map["version"].AsString()=="1.1"&&map["type"].AsString()=="GLTF 2.0"&&map["data"].AsString()==Gltf(f,texture);}
        catch{return false;}
    }
    public static UUID Id(ContentPlan plan,string localId)=>new UUID(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(plan.plan_id+":material:"+localId)).Take(16).ToArray(),0);
    public static void Bind(MutSea.Framework.PrimitiveBaseShape shape,ContentPlan plan,ContentPart part)
    {
        if(plan.package.schema_version!=3)return;
        shape.RenderMaterials=new Primitive.RenderMaterials {
            entries=part.faces.Select((face,i)=>new Primitive.RenderMaterials.RenderMaterialEntry {
                te_index=(byte)i,id=Id(plan,face.material_id!)}).ToArray()};
    }
}
