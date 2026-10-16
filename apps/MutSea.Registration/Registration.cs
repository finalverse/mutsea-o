// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace MutSea.Registration;

public sealed record AvatarChoice(string Id,string Title,string Directory,string Pin);
public sealed record RegistrationSettings(bool Enabled,string KeyFile,Guid HostRegion,int Capacity,AvatarChoice[] Avatars)
{
    public static string ReadPrivate(string file)
    {
        if(!Path.IsPathRooted(file)||!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0||new FileInfo(file).Length>131072)
            throw new InvalidDataException("Expected a bounded regular private file.");
        if(!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(file)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))!=0)
            throw new InvalidDataException("Registration configuration must be owner-only.");
        return File.ReadAllText(file).Trim();
    }
    public static RegistrationSettings Load(string file)
    {
        var s=JsonSerializer.Deserialize<RegistrationSettings>(ReadPrivate(file),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException();
        if(s.HostRegion==Guid.Empty||s.Capacity is <1 or >2000||s.Avatars==null||s.Avatars.Length is <1 or >12||s.Avatars.Select(a=>a.Id).Distinct().Count()!=s.Avatars.Length)
            throw new InvalidDataException("Invalid registration capacity, region or choices.");
        foreach(var a in s.Avatars)
            if(!Regex.IsMatch(a.Id,"^[a-z][a-z0-9-]{0,40}$")||a.Id.Contains("elara-male")||a.Title.Length is <1 or >100||a.Title.Any(char.IsControl)||!Path.IsPathRooted(a.Directory)||a.Pin.Length!=64||!a.Pin.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid pinned avatar choice.");
        return s;
    }
    public AvatarChoice Choice(string id)=>Avatars.SingleOrDefault(a=>a.Id==id)??throw new RegistrationFault(400,"Choose an available starter avatar.");
}
public sealed class RegistrationFault(int status,string message):Exception(message){public int Status=>status;}
public sealed record RegistrationJob(Guid Id,string FirstName,string LastName,string Password,string Email,string Avatar,string Pin);
public sealed record RegistrationStatus(string State,string Name,string? Message);
public interface IRegistrations
{
    object Options();
    string Register(string first,string last,string password,string email,string avatar,string ip,string? previous);
    RegistrationStatus? Status(string? ticket);
}

// The public process only writes a bounded encrypted queue. Only the native
// runtime provisions identity/inventory/appearance, using its existing services.
public sealed class PgRegistrations : IRegistrations
{
    readonly string connection;
    readonly byte[] key;
    public RegistrationSettings Settings {get;}
    public PgRegistrations(string connection,RegistrationSettings settings)
    {
        Settings=settings;
        var b=new NpgsqlConnectionStringBuilder(connection){MaxPoolSize=4,Timeout=5,CommandTimeout=10,IncludeErrorDetail=false};
        this.connection=b.ConnectionString;
        key=Convert.FromHexString(RegistrationSettings.ReadPrivate(settings.KeyFile));
        if(key.Length!=32)throw new InvalidDataException("Registration requires a 256-bit key.");
    }
    NpgsqlConnection Open(){var c=new NpgsqlConnection(connection);c.Open();return c;}
    static NpgsqlCommand Cmd(NpgsqlConnection c,string sql,params object[] args)
    {
        var cmd=new NpgsqlCommand(sql,c);for(int i=0;i<args.Length;i++)cmd.Parameters.AddWithValue("p"+i,args[i]);return cmd;
    }
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    string Digest(string value)=>Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(value)));
    static bool Ticket(string? t)=>t is {Length:64}&&t.All(Uri.IsHexDigit);
    public static void Validate(string first,string last,string password,string email)
    {
        foreach(string part in new[]{first,last})
            if(!Regex.IsMatch(part,"^[A-Za-z][A-Za-z0-9]{0,30}$"))throw new RegistrationFault(400,"Use a first and last name of 1–31 letters or digits, starting with a letter. Do not enter an avatar UUID or email as your name.");
        if(password.Length is <12 or >16||password.Any(c=>c<' '||c>'~'))throw new RegistrationFault(400,"Use 12–16 printable ASCII characters for compatibility with the current viewer.");
        if(email.Length>254||email.Any(char.IsControl))throw new RegistrationFault(400,"Enter a valid email address.");
        if(email!="")try{if(new System.Net.Mail.MailAddress(email).Address!=email)throw new Exception();}catch{throw new RegistrationFault(400,"Enter a valid email address.");}
        if(first.Equals("GRID",StringComparison.OrdinalIgnoreCase)||last.Equals("SERVICES",StringComparison.OrdinalIgnoreCase))throw new RegistrationFault(400,"Choose a different avatar name.");
    }
    string CatalogHash=>Hash(string.Join('\n',Settings.Avatars.OrderBy(a=>a.Id).Select(a=>a.Id+":"+a.Pin)));
    public bool Ready()
    {
        using var c=Open();using var cmd=Cmd(c,"SELECT EXISTS(SELECT 1 FROM mutsea_portal.registration_worker WHERE singleton AND region_id=@p0 AND catalog=@p1 AND seen>now()-interval '60 seconds')",Settings.HostRegion,CatalogHash);
        return (bool)cmd.ExecuteScalar()!;
    }
    public void Heartbeat()
    {
        using var c=Open();using var cmd=Cmd(c,"INSERT INTO mutsea_portal.registration_worker (singleton,region_id,catalog,seen) VALUES (true,@p0,@p1,now()) ON CONFLICT (singleton) DO UPDATE SET region_id=EXCLUDED.region_id,catalog=EXCLUDED.catalog,seen=EXCLUDED.seen",Settings.HostRegion,CatalogHash);cmd.ExecuteNonQuery();
    }
    public object Options()
    {
        bool available=Settings.Enabled&&Ready();
        if(available)
        {
            using var c=Open();using var cmd=Cmd(c,"SELECT count(*) FROM mutsea_portal.registrations WHERE state IN ('queued','processing','ready')");
            available=(long)cmd.ExecuteScalar()!<Settings.Capacity;
        }
        return new{enabled=available,avatars=Settings.Avatars.Select(a=>new{id=a.Id,title=a.Title}),experience="Finalverse starter world",emailVerified=false};
    }
    byte[] Seal(RegistrationJob job)
    {
        byte[] plain=JsonSerializer.SerializeToUtf8Bytes(job),nonce=RandomNumberGenerator.GetBytes(12),tag=new byte[16],cipher=new byte[plain.Length];
        try{using var aes=new AesGcm(key,16);aes.Encrypt(nonce,plain,cipher,tag,job.Id.ToByteArray());return [..nonce,..tag,..cipher];}
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    RegistrationJob Unseal(Guid id,byte[] bytes)
    {
        if(bytes.Length is <29 or >8192)throw new InvalidDataException("Invalid encrypted registration.");
        byte[] clear=new byte[bytes.Length-28];
        try
        {
            using var aes=new AesGcm(key,16);aes.Decrypt(bytes.AsSpan(0,12),bytes.AsSpan(28),bytes.AsSpan(12,16),clear,id.ToByteArray());
            var job=JsonSerializer.Deserialize<RegistrationJob>(clear)??throw new InvalidDataException();
            if(job.Id!=id)throw new InvalidDataException("Registration binding mismatch.");
            Validate(job.FirstName,job.LastName,job.Password,job.Email);
            if(Settings.Choice(job.Avatar).Pin!=job.Pin)throw new InvalidDataException("Starter choice changed.");
            return job;
        }
        finally{CryptographicOperations.ZeroMemory(clear);}
    }
    public string Register(string first,string last,string password,string email,string avatar,string ip,string? previous)
    {
        if(!Settings.Enabled||!Ready())throw new RegistrationFault(503,"The world is not ready to provision new accounts. Please try again shortly.");
        Validate(first,last,password,email);var choice=Settings.Choice(avatar);
        using var c=Open();using var tx=c.BeginTransaction();
        // Serialize capacity/name reservation across processes, not just HTTP threads.
        using(var guard=Cmd(c,"SELECT pg_advisory_xact_lock(724817033)"))guard.ExecuteNonQuery();
        if(Ticket(previous))
        {
            using var old=Cmd(c,"SELECT state,normal_name FROM mutsea_portal.registrations WHERE ticket_hash=@p0",Hash(previous!));
            using var r=old.ExecuteReader();
            if(r.Read()&&r.GetString(0) is "queued" or "processing")
            {
                if(r.GetString(1)!=(first+" "+last).ToLowerInvariant())throw new RegistrationFault(409,"Finish your pending registration before starting another.");
                return previous!; // duplicate submission never re-keys an account
            }
        }
        using(var existing=Cmd(c,"SELECT EXISTS(SELECT 1 FROM public.useraccounts WHERE lower(\"FirstName\"||' '||\"LastName\")=@p0) OR EXISTS(SELECT 1 FROM mutsea_portal.registrations WHERE normal_name=@p0)",(first+" "+last).ToLowerInvariant()))
            if((bool)existing.ExecuteScalar()!)throw new RegistrationFault(409,"That avatar name is unavailable. Choose another first and last name.");
        string ipHash=Digest("registration-ip:"+ip);
        using(var rate=Cmd(c,"SELECT count(*) FROM mutsea_portal.registrations WHERE ip_hash=@p0 AND created>now()-interval '24 hours'",ipHash))
            if((long)rate.ExecuteScalar()!>=3)throw new RegistrationFault(429,"Registration limit reached. Try again tomorrow.");
        using(var capacity=Cmd(c,"SELECT count(*) FROM mutsea_portal.registrations WHERE state IN ('queued','processing','ready')"))
            if((long)capacity.ExecuteScalar()!>=Settings.Capacity)throw new RegistrationFault(503,"The preview registration capacity is full. Please try again later.");
        Guid id=Guid.NewGuid();string ticket=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var job=new RegistrationJob(id,first,last,password,email,choice.Id,choice.Pin);
        using(var insert=Cmd(c,"INSERT INTO mutsea_portal.registrations (id,ticket_hash,normal_name,name,ip_hash,payload,state,created,updated) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,'queued',now(),now())",id,Hash(ticket),(first+" "+last).ToLowerInvariant(),first+" "+last,ipHash,Seal(job)))insert.ExecuteNonQuery();
        tx.Commit();return ticket;
    }
    public RegistrationStatus? Status(string? ticket)
    {
        if(!Ticket(ticket))return null;
        using var c=Open();using var cmd=Cmd(c,"SELECT state,name FROM mutsea_portal.registrations WHERE ticket_hash=@p0 AND created>now()-interval '24 hours'",Hash(ticket!));using var r=cmd.ExecuteReader();
        if(!r.Read())return null;string state=r.GetString(0);
        return new(state,r.GetString(1),state=="failed"?"Your account could not finish provisioning. Contact pilot support; do not reuse another person's account.":null);
    }
    // Called only inside the selected native region, never by an HTTP route.
    public bool ProcessOne(Func<RegistrationJob,Guid> provision)
    {
        if(!Settings.Enabled)return false;
        Guid id;byte[] encrypted;
        using(var c=Open())
        using(var tx=c.BeginTransaction())
        {
            using(var expired=Cmd(c,"UPDATE mutsea_portal.registrations SET state='failed',payload=NULL,updated=now() WHERE state IN ('queued','processing') AND created<now()-interval '1 hour'"))expired.ExecuteNonQuery();
            using var take=Cmd(c,"UPDATE mutsea_portal.registrations SET state='processing',updated=now() WHERE id=(SELECT id FROM mutsea_portal.registrations WHERE state='queued' OR (state='processing' AND updated<now()-interval '10 minutes') ORDER BY created FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id,payload");
            bool found;
            using(var row=take.ExecuteReader())
            {
                found=row.Read();
                id=found?row.GetGuid(0):Guid.Empty;encrypted=found?(byte[])row[1]:[];
            }
            if(!found){tx.Commit();return false;}
            tx.Commit();
        }
        string status="failed";
        try{if(provision(Unseal(id,encrypted))==id)status="ready";}
        catch(Exception){/* Keep any native partial identity inactive; never log payloads/passwords. */}
        finally
        {
            using var c=Open();using var finish=Cmd(c,"UPDATE mutsea_portal.registrations SET state=@p1,payload=NULL,updated=now() WHERE id=@p0",id,status);finish.ExecuteNonQuery();
        }
        return true;
    }
}
