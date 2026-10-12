// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenMetaverse;

namespace Finalverse.World;

public sealed record AvatarTexture(string local_id,string asset_path,string asset_sha256,int width,int height);

// Original, offline-authored RGB surfaces only. Native image decoding remains
// in the existing viewer; this is not a public arbitrary image-upload service.
public static class AvatarTextureAsset
{
    public const int MaxBytes=1<<20,MaxDimension=1024,MaxTotalBytes=4<<20;
    public static UUID Id(AvatarTexture t)=>new(SHA256.HashData(Encoding.UTF8.GetBytes("finalverse-avatar:texture:"+t.asset_sha256)).Take(16).ToArray(),0);
    public static void Validate(byte[] bytes,AvatarTexture t)
    {
        void Need(bool value,string message)=>ContentCatalog.Require(value,message);
        Need(bytes.Length is >=45 and <=MaxBytes,"Bounded JPEG2000 codestream required.");
        Need(bytes[0]==0xff&&bytes[1]==0x4f&&bytes[2]==0xff&&bytes[3]==0x51,"Only a native JPEG2000 SOC/SIZ codestream is accepted.");
        int U16(int n)=>BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(n,2));
        uint U32(int n)=>BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(n,4));
        int channels=U16(40),length=U16(4);
        Need(channels==3&&length==38+3*channels&&bytes.Length>=4+length,"Only RGB texture surfaces are accepted.");
        // SIZ fields: canvas X/Y at 8/12, offsets at 16/20, tiles at 24/28,
        // tile offsets at 32/36 and component count at 40.
        Need(U32(8)==(uint)t.width&&U32(12)==(uint)t.height,"Texture dimensions differ from the package.");
        Need(U32(16)==0&&U32(20)==0&&U32(32)==0&&U32(36)==0,"Texture/tile origins must be zero.");
        Need(t.width is >0 and <=MaxDimension&&t.height is >0 and <=MaxDimension&&U32(24)==(uint)t.width&&U32(28)==(uint)t.height,"Only one bounded texture tile is accepted.");
        for(int i=0;i<channels;i++)Need(bytes[42+3*i]==7&&bytes[43+3*i]==1&&bytes[44+3*i]==1,"Texture components must be unsigned 8-bit RGB without subsampling.");
        Need(bytes[^2]==0xff&&bytes[^1]==0xd9,"JPEG2000 end marker missing.");
    }
}
