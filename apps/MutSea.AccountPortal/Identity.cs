// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using Nini.Config;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Framework.Console;
using MutSea.Services.AuthenticationService;
using MutSea.Services.Interfaces;
using MutSea.Services.UserAccountService;

namespace MutSea.AccountPortal;
public sealed record WorldAccount(Guid Id,string Name,string Email,long Created,HomeInfo? Home,HomeInfo? LastLocation,bool Online,DateTime? LastLogin);
public sealed record HomeInfo(Guid RegionId,string? Name,float[] Position);
public interface IWorldIdentity
{
    WorldAccount? Find(string name);
    WorldAccount? Get(Guid id);
    bool Verify(Guid id,string password);
    string? CredentialStamp(Guid id);
    bool SetPassword(Guid id,string password);
}

// Invoke the existing native services with a separate, restricted database role.
// No world login/presence session is opened by a web sign-in.
public sealed class NativeIdentity : IWorldIdentity
{
    readonly IUserAccountService users;
    readonly IAuthenticationService auth;
    readonly IGridUserService grid;
    readonly byte[] key;
    readonly Dictionary<Guid,string> regions;
    public NativeIdentity(string connection,byte[] key,Dictionary<Guid,string> regions)
    {
        PrepareServices();
        this.key=key;this.regions=regions;
        var config=new IniConfigSource();
        foreach(string section in new[]{"DatabaseService","UserAccountService","AuthenticationService","GridUserService"})
        {
            var c=config.AddConfig(section);c.Set("StorageProvider","MutSea.Data.PGSQL.dll");c.Set("ConnectionString",connection);
        }
        users=new UserAccountService(config);
        // This overload deliberately has no account-service impersonation fallback.
        auth=new PasswordAuthenticationService(config);
        grid=new GridUserService(config);
    }
    public static void PrepareServices()
    {
        // GridUserService registers commands unconditionally. Supply only its
        // inert registry: no console reader, listener or command dispatcher runs.
        MainConsole.Instance??=new CommandConsole("MutSea accounts");
    }
    static bool Allowed(UserAccount? a)=>a!=null&&a.LocalToGrid&&a.UserLevel>=0&&a.PrincipalID!=Constants.servicesGodAgentID;
    WorldAccount? Map(UserAccount? a)
    {
        if(!Allowed(a))return null;
        var g=grid.GetGridUserInfo(a!.PrincipalID.ToString());
        HomeInfo? Location(UUID id,Vector3 p)=>id==UUID.Zero?null:new(Guid.Parse(id.ToString()),regions.GetValueOrDefault(Guid.Parse(id.ToString())),[p.X,p.Y,p.Z]);
        return new(Guid.Parse(a.PrincipalID.ToString()),a.Name,a.Email??"",a.Created,
            g==null?null:Location(g.HomeRegionID,g.HomePosition),g==null?null:Location(g.LastRegionID,g.LastPosition),
            g?.Online??false,g?.Login.Year>1971?g.Login.ToUniversalTime():null);
    }
    public WorldAccount? Find(string name)
    {
        var parts=Input.Name(name).Split(' ');
        return Map(users.GetUserAccount(UUID.Zero,parts[0],parts[1]));
    }
    public WorldAccount? Get(Guid id)=>Map(users.GetUserAccount(UUID.Zero,new UUID(id.ToString())));
    public bool Verify(Guid id,string password)
    {
        string token=auth.Authenticate(new UUID(id.ToString()),Util.Md5Hash(password),1,out UUID real);
        if(string.IsNullOrEmpty(token))return false;
        try{return real==UUID.Zero;}finally{auth.Release(new UUID(id.ToString()),token);}
    }
    public string? CredentialStamp(Guid id)
    {
        var info=auth.GetAuthInfo(new UUID(id.ToString()));
        return info==null?null:Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(id+":"+info.PasswordHash+":"+info.PasswordSalt)));
    }
    public bool SetPassword(Guid id,string password)=>auth.SetPassword(new UUID(id.ToString()),password);
}

public static class Input
{
    public static string Name(string value)
    {
        var words=value.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        if(words.Length!=2||words.Any(x=>x.Length is <1 or >64||x.Any(char.IsControl)))throw new PortalFault(400,"Enter your avatar's first and last name, separated by a space.");
        return string.Join(' ',words);
    }
    public static void Password(string value)
    {
        // The published viewer's panel_login.xml has max_length_chars="16".
        if(value.Length is <12 or >16||value.Any(c=>c<' '||c>'~'))throw new PortalFault(400,"Use 12–16 printable ASCII characters so the current viewer can enter the same password.");
    }
    public static void Profile(string about,string email,string language)
    {
        if(about.Length>500||about.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\r'&&c!='\t'))throw new PortalFault(400,"About text must be at most 500 characters.");
        if(email.Length>254||email.Any(char.IsControl))throw new PortalFault(400,"Enter a valid contact email.");
        if(email!="")try{if(new System.Net.Mail.MailAddress(email).Address!=email)throw new Exception();}catch{throw new PortalFault(400,"Enter a valid contact email.");}
        if(language is not ("en" or "zh"))throw new PortalFault(400,"Choose a supported language.");
    }
}
public sealed class PortalFault(int status,string message):Exception(message){public int Status=>status;}
