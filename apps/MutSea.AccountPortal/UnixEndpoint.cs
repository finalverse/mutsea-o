// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Runtime.InteropServices;
namespace MutSea.AccountPortal;
public static class UnixEndpoint
{
    // Linux statx has an architecture-independent 256-byte ABI. Request only
    // file type and do not follow links. Never infer socket type from a failed
    // connect: an ordinary file can also produce ECONNREFUSED.
    [StructLayout(LayoutKind.Explicit,Size=256)]
    struct Statx
    {
        [FieldOffset(0)]public uint Mask;
        [FieldOffset(28)]public ushort Mode;
    }
    [DllImport("libc",EntryPoint="statx",SetLastError=true)]
    static extern int ReadType(int directory,[MarshalAs(UnmanagedType.LPUTF8Str)]string path,int flags,uint mask,out Statx metadata);
    public static bool IsSocket(string path)
    {
        if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException();
        if(ReadType(-100,path,0x100,1,out var metadata)!=0||(metadata.Mask&1)==0)
            throw new IOException("Cannot verify IPC endpoint type.");
        return (metadata.Mode&0xF000)==0xC000;
    }
}
