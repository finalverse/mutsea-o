// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Net.Sockets;
namespace Finalverse.World;
public static class PrivateServiceHttp
{
    public static HttpClient Client(string path,TimeSpan timeout)
    {
        if(!Path.IsPathFullyQualified(path)||path.Length>100||path.Split('/').Contains(".."))throw new InvalidDataException("Invalid private service socket.");
        var handler=new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false,
            ConnectCallback=async (_,token)=>{
                var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
                try{await socket.ConnectAsync(new UnixDomainSocketEndPoint(path),token);return new NetworkStream(socket,true);}
                catch{socket.Dispose();throw;}
            }};
        return new HttpClient(handler){Timeout=timeout};
    }
}
