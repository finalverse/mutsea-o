// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
namespace MutSea.AccountPortal;
public sealed record SignedIn(SessionRow Row,WorldAccount Account);
public sealed class Accounts(IWorldIdentity identity,IPortalState state,byte[] key,Func<DateTimeOffset>? clock=null)
{
    DateTimeOffset Now=>(clock??(()=>DateTimeOffset.UtcNow))();
    public static string RandomToken()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static bool Equal(string? a,string? b)=>a!=null&&b!=null&&CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a),Encoding.UTF8.GetBytes(b));
    string Bucket(string kind,string value)=>Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(kind+":"+value.ToLowerInvariant())));
    public void Throttle(string name,string ip,string action="login")
    {
        if(!state.Limit(Bucket(action+"-ip",ip),40,Now)||!state.Limit(Bucket(action+"-name",name),10,Now))
            throw new PortalFault(429,"Too many attempts. Wait 15 minutes and try again.");
    }
    public string Login(string name,string password,string ip)
    {
        name=Input.Name(name);Throttle(name,ip);
        var account=identity.Find(name);
        string? before=account==null?null:identity.CredentialStamp(account.Id);
        if(account==null||before==null||!identity.Verify(account.Id,password)||!Equal(before,identity.CredentialStamp(account.Id)))
        {state.Audit(null,"login_failed",Now);throw new PortalFault(401,"The avatar name or password is incorrect.");}
        string token=RandomToken();
        state.AddSession(new(Hash(token),account.Id,before,Now,Now));state.Audit(account.Id,"login",Now);return token;
    }
    public SignedIn? Session(string? token)
    {
        if(token==null||token.Length!=64||!token.All(Uri.IsHexDigit))return null;
        var row=state.Session(Hash(token));if(row==null)return null;
        var account=identity.Get(row.User);
        if(account==null||Now-row.Created>=TimeSpan.FromHours(12)||Now-row.Seen>=TimeSpan.FromMinutes(30)||!Equal(row.Stamp,identity.CredentialStamp(row.User)))
        {state.RemoveSession(row.Hash);return null;}
        state.Touch(row.Hash,Now);return new(row,account);
    }
    public SignedIn Require(string? token)=>Session(token)??throw new PortalFault(401,"Sign in again to continue.");
    public void Logout(string? token){if(token is {Length:64})state.RemoveSession(Hash(token));}
    public ProfileRow Profile(SignedIn session)=>state.Profile(session.Row.User);
    public ProfileRow SaveProfile(string? token,string about,string email,string language,long version)
    {
        Input.Profile(about,email,language);if(version<0)throw new PortalFault(400,"Invalid profile version.");var session=Require(token);
        return state.Locked(session.Row.User,()=>{
            session=Require(token);if(!state.SaveProfile(session.Row.User,new(about,email,language,version)))throw new PortalFault(409,"Your profile changed in another session. Reload before saving.");
            state.Audit(session.Row.User,"profile_updated",Now);return state.Profile(session.Row.User);
        });
    }
    static string CodeHash(Guid id,string code)
    {
        string normalized=code.Trim().Replace("-","").Replace(" ","").ToUpperInvariant();
        if(normalized.Length!=32||!normalized.All(Uri.IsHexDigit))throw new PortalFault(401,"The avatar name or recovery code is incorrect.");
        return Hash("recovery:"+id+":"+normalized);
    }
    public int CodeCount(SignedIn s)=>state.CodeCount(s.Row.User,s.Row.Stamp);
    public string[] Codes(string? token,string password,string ip)
    {
        var s=Require(token);Throttle(s.Account.Name,ip,"security");
        return state.Locked(s.Row.User,()=>{
            s=Require(token);
            if(!identity.Verify(s.Row.User,password)||!Equal(s.Row.Stamp,identity.CredentialStamp(s.Row.User)))throw new PortalFault(401,"Your current password is incorrect. Sign in again if it changed.");
            var codes=Enumerable.Range(0,6).Select(_=>Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).Select(v=>string.Join('-',Enumerable.Range(0,4).Select(i=>v.Substring(i*8,8)))).ToArray();
            state.ReplaceCodes(s.Row.User,s.Row.Stamp,codes.Select(v=>CodeHash(s.Row.User,v)).ToArray(),Now);state.Audit(s.Row.User,"recovery_codes_replaced",Now);return codes;
        });
    }
    void Change(Guid id,string password)
    {
        if(!identity.SetPassword(id,password))throw new PortalFault(503,"The password could not be saved. Please try signing in before retrying.");
        // A changed native credential stamp invalidates all web sessions/codes
        // even if metadata cleanup fails after the native password commit.
        state.Revoke(id);state.Audit(id,"password_changed",Now);
    }
    public void ChangePassword(string? token,string current,string password,string ip)
    {
        Input.Password(password);var s=Require(token);Throttle(s.Account.Name,ip,"security");
        state.Locked(s.Row.User,()=>{
            s=Require(token);
            if(!identity.Verify(s.Row.User,current)||!Equal(s.Row.Stamp,identity.CredentialStamp(s.Row.User)))throw new PortalFault(401,"Your current password is incorrect. Sign in again if it changed.");
            Change(s.Row.User,password);return true;
        });
    }
    public void Reset(string name,string code,string password,string ip)
    {
        name=Input.Name(name);Throttle(name,ip,"reset");Input.Password(password);
        var a=identity.Find(name);if(a==null)throw new PortalFault(401,"The avatar name or recovery code is incorrect.");
        state.Locked(a.Id,()=>{
            string? stamp=identity.CredentialStamp(a.Id);
            if(identity.Get(a.Id)==null||stamp==null||!state.Code(a.Id,stamp,CodeHash(a.Id,code)))throw new PortalFault(401,"The avatar name or recovery code is incorrect.");
            Change(a.Id,password);return true;
        });
    }
}
