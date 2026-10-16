// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using System.Net.Sockets;
using MutSea.AccountPortal;
try
{
    if(args.Length!=1)throw new InvalidDataException("Supply the private configuration file.");
    var options=JsonSerializer.Deserialize<PortalOptions>(PortalOptions.ReadSecret(args[0]),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException();
    options.Validate();string connection=PortalOptions.ReadSecret(options.DatabaseFile);byte[] key=Convert.FromHexString(PortalOptions.ReadSecret(options.KeyFile));if(key.Length!=32)throw new InvalidDataException("Expected a 256-bit service key.");
    var state=new PgState(connection);state.Prune(DateTimeOffset.UtcNow);
    var app=PortalApp.Build(options,new Accounts(new NativeIdentity(connection,key,options.Regions),state,key));
    using var maintenance=new PeriodicTimer(TimeSpan.FromMinutes(15));
    using var stopped=new CancellationTokenSource();
    var cleanup=Task.Run(async()=>{
        try{while(await maintenance.WaitForNextTickAsync(stopped.Token))try{state.Prune(DateTimeOffset.UtcNow);}catch(Exception e){Console.Error.WriteLine("Account retention cleanup failed: "+e.GetType().Name);}}
        catch(OperationCanceledException){}
    });
    if(options.Socket!=null)
    {
        if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException();
        // Keep a single-writer lock; recover only a stale Unix socket, never
        // a regular file, symlink or another live server's endpoint.
        using var owner=new FileStream(options.Socket+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        File.SetUnixFileMode(options.Socket+".lock",UnixFileMode.UserRead|UnixFileMode.UserWrite);
        if(File.Exists(options.Socket))
        {
            if(!UnixEndpoint.IsSocket(options.Socket))throw new InvalidDataException("IPC path is not a Unix socket.");
            using var probe=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
            try{probe.Connect(new UnixDomainSocketEndPoint(options.Socket));throw new InvalidDataException("IPC endpoint is already active.");}
            catch(SocketException e) when(e.SocketErrorCode==SocketError.ConnectionRefused){File.Delete(options.Socket);}
        }
        await app.StartAsync();File.SetUnixFileMode(options.Socket,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead|UnixFileMode.GroupWrite);
        Console.WriteLine("MutSea account service ready on private IPC.");await app.WaitForShutdownAsync();File.Delete(options.Socket);
    }
    else await app.RunAsync();
    stopped.Cancel();await cleanup;
}
catch(Exception e){Console.Error.WriteLine("Account service startup failed: "+e.GetType().Name);Environment.ExitCode=1;}
