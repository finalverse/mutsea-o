// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Npgsql;
namespace MutSea.AccountPortal;
public sealed record SessionRow(string Hash,Guid User,string Stamp,DateTimeOffset Created,DateTimeOffset Seen);
public sealed record ProfileRow(string About,string ContactEmail,string Language,long Version);
public interface IPortalState
{
    bool Limit(string key,int max,DateTimeOffset now);
    void AddSession(SessionRow row);
    SessionRow? Session(string hash);
    void Touch(string hash,DateTimeOffset now);
    void RemoveSession(string hash);
    void Revoke(Guid user);
    ProfileRow Profile(Guid user);
    bool SaveProfile(Guid user,ProfileRow profile);
    void ReplaceCodes(Guid user,string stamp,string[] hashes,DateTimeOffset now);
    bool Code(Guid user,string stamp,string hash);
    int CodeCount(Guid user,string stamp);
    T Locked<T>(Guid user,Func<T> action);
    void Audit(Guid? user,string kind,DateTimeOffset now);
}
// The schema is provisioned by an operator, not by the public process.
// Native account/world tables are never migrated by this store.
public sealed class PgState(string connection):IPortalState
{
    public void Prune(DateTimeOffset now)
    {
        using var c=Open();using var cmd=Cmd(c,"DELETE FROM mutsea_portal.sessions WHERE created<@p0 OR seen<@p1; DELETE FROM mutsea_portal.limits WHERE bucket<@p2; DELETE FROM mutsea_portal.audit WHERE at<@p3",now-TimeSpan.FromHours(12),now-TimeSpan.FromMinutes(30),now.ToUnixTimeSeconds()/900-1,now-TimeSpan.FromDays(90));cmd.ExecuteNonQuery();
    }
    NpgsqlConnection Open(){var c=new NpgsqlConnection(connection);c.Open();return c;}
    static NpgsqlCommand Cmd(NpgsqlConnection c,string sql,params object[] args)
    {
        var cmd=new NpgsqlCommand(sql,c);for(int i=0;i<args.Length;i++)cmd.Parameters.AddWithValue("p"+i,args[i]);return cmd;
    }
    public bool Limit(string key,int max,DateTimeOffset now)
    {
        long bucket=now.ToUnixTimeSeconds()/900;
        using var c=Open();using var cmd=Cmd(c,"INSERT INTO mutsea_portal.limits (key,bucket,n) VALUES (@p0,@p1,1) ON CONFLICT (key,bucket) DO UPDATE SET n=mutsea_portal.limits.n+1 RETURNING n",key,bucket);
        return (int)cmd.ExecuteScalar()!<=max;
    }
    public void AddSession(SessionRow r){using var c=Open();using var cmd=Cmd(c,"INSERT INTO mutsea_portal.sessions (hash,user_id,stamp,created,seen) VALUES (@p0,@p1,@p2,@p3,@p4)",r.Hash,r.User,r.Stamp,r.Created,r.Seen);cmd.ExecuteNonQuery();}
    public SessionRow? Session(string hash)
    {
        using var c=Open();using var cmd=Cmd(c,"SELECT user_id,stamp,created,seen FROM mutsea_portal.sessions WHERE hash=@p0",hash);using var r=cmd.ExecuteReader();
        return r.Read()?new(hash,r.GetGuid(0),r.GetString(1),r.GetFieldValue<DateTimeOffset>(2),r.GetFieldValue<DateTimeOffset>(3)):null;
    }
    public void Touch(string hash,DateTimeOffset now){using var c=Open();using var cmd=Cmd(c,"UPDATE mutsea_portal.sessions SET seen=@p1 WHERE hash=@p0",hash,now);cmd.ExecuteNonQuery();}
    public void RemoveSession(string hash){using var c=Open();using var cmd=Cmd(c,"DELETE FROM mutsea_portal.sessions WHERE hash=@p0",hash);cmd.ExecuteNonQuery();}
    public void Revoke(Guid user){using var c=Open();using var cmd=Cmd(c,"DELETE FROM mutsea_portal.sessions WHERE user_id=@p0; DELETE FROM mutsea_portal.recovery WHERE user_id=@p0",user);cmd.ExecuteNonQuery();}
    public ProfileRow Profile(Guid user)
    {
        using var c=Open();using var cmd=Cmd(c,"SELECT about,contact_email,language,version FROM mutsea_portal.profiles WHERE user_id=@p0",user);using var r=cmd.ExecuteReader();return r.Read()?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3)):new("","","en",0);
    }
    public bool SaveProfile(Guid user,ProfileRow p)
    {
        using var c=Open();
        string sql=p.Version==0
            ? "INSERT INTO mutsea_portal.profiles (user_id,about,contact_email,language,version) VALUES (@p0,@p1,@p2,@p3,1) ON CONFLICT (user_id) DO NOTHING"
            : "UPDATE mutsea_portal.profiles SET about=@p1,contact_email=@p2,language=@p3,version=version+1 WHERE user_id=@p0 AND version=@p4";
        using var cmd=Cmd(c,sql,user,p.About,p.ContactEmail,p.Language,p.Version);return cmd.ExecuteNonQuery()==1;
    }
    public void ReplaceCodes(Guid user,string stamp,string[] hashes,DateTimeOffset now)
    {
        using var c=Open();using var tx=c.BeginTransaction();using(var d=Cmd(c,"DELETE FROM mutsea_portal.recovery WHERE user_id=@p0",user))d.ExecuteNonQuery();
        foreach(string hash in hashes){using var cmd=Cmd(c,"INSERT INTO mutsea_portal.recovery (user_id,stamp,hash,created) VALUES (@p0,@p1,@p2,@p3)",user,stamp,hash,now);cmd.ExecuteNonQuery();}tx.Commit();
    }
    public bool Code(Guid user,string stamp,string hash){using var c=Open();using var cmd=Cmd(c,"SELECT count(*) FROM mutsea_portal.recovery WHERE user_id=@p0 AND stamp=@p1 AND hash=@p2",user,stamp,hash);return (long)cmd.ExecuteScalar()! ==1;}
    public int CodeCount(Guid user,string stamp){using var c=Open();using var cmd=Cmd(c,"SELECT count(*) FROM mutsea_portal.recovery WHERE user_id=@p0 AND stamp=@p1",user,stamp);return checked((int)(long)cmd.ExecuteScalar()!);}
    public T Locked<T>(Guid user,Func<T> action)
    {
        using var c=Open();using var tx=c.BeginTransaction();byte[] bytes=user.ToByteArray();using(var cmd=Cmd(c,"SELECT pg_advisory_xact_lock(@p0)",BitConverter.ToInt64(bytes,0)))cmd.ExecuteNonQuery();
        T result=action();tx.Commit();return result;
    }
    public void Audit(Guid? user,string kind,DateTimeOffset now){using var c=Open();using var cmd=Cmd(c,"INSERT INTO mutsea_portal.audit (user_id,kind,at) VALUES (NULLIF(@p0,'')::uuid,@p1,@p2)",user?.ToString()??"",kind,now);cmd.ExecuteNonQuery();}
}
