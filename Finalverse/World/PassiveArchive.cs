// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.IO.Compression;
namespace Finalverse.World;
public static class PassiveArchive
{
    // The inherited stream overload expects an uncompressed, seekable TAR.
    // The path overload opens gzip itself; passing gzip bytes to the stream
    // overload is not equivalent. Keep the quarantine's expansion bound here.
    public static MemoryStream OpenTar(byte[] compressed,int expandedLimit=256*1024*1024)
    {
        if(compressed.Length is <1 or >64*1024*1024||expandedLimit<1)throw new InvalidDataException("Passive archive bounds.");
        var tar=new MemoryStream();
        try
        {
            using var gzip=new GZipStream(new MemoryStream(compressed,false),CompressionMode.Decompress);
            byte[] buffer=new byte[65536];int n;
            while((n=gzip.Read(buffer,0,buffer.Length))>0)
            {if(tar.Length+n>expandedLimit)throw new InvalidDataException("Expanded passive archive limit.");tar.Write(buffer,0,n);}
            tar.Position=0;return tar;
        }
        catch{tar.Dispose();throw;}
    }
}
