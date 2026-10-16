// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text;
using System.Text.Json;
using MutSea.Registration;
using Npgsql;

static class RegistrationChecks
{
    public static void Run(string connection,string admin,string keyFile)
    {
        string database=new NpgsqlConnectionStringBuilder(connection).Database!;
        if(!database.StartsWith("mutsea_accounts_fixture_")||new NpgsqlConnectionStringBuilder(admin).Database!=database)throw new Exception("Refusing a non-fixture registration database.");
        int passed=0;
        void Check(bool yes,string name){if(!yes)throw new Exception(name);passed++;Console.WriteLine("PASS registration "+name);}
        void Fault(Action action,int code,string name){try{action();throw new Exception("Expected rejection");}catch(RegistrationFault e){Check(e.Status==code,name);}}
        object? Query(string sql,params object[] values)
        {
            using var c=new NpgsqlConnection(admin);c.Open();using var cmd=new NpgsqlCommand(sql,c);for(int i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("p"+i,values[i]);return cmd.ExecuteScalar();
        }
        Query("TRUNCATE mutsea_portal.registrations,mutsea_portal.registration_worker");
        var settings=new RegistrationSettings(true,keyFile,Guid.NewGuid(),50,[new("auren","Auren","/fixture/auren",new string('a',64))]);
        var portal=new PgRegistrations(connection,settings);var worker=new PgRegistrations(admin,settings);
        Check(!portal.Ready(),"no signup before a native worker heartbeat");
        Fault(()=>portal.Register("Queue","Smoke","StarterPass123!","","auren","before",null),503,"offline native runtime refuses enqueue");
        worker.Heartbeat();Check(portal.Ready(),"matching live native catalog enables registration");
        try{portal.Heartbeat();throw new Exception("Public process wrote readiness");}catch(PostgresException e){Check(e.SqlState=="42501","public role cannot forge native readiness");}
        Fault(()=>portal.Register("Portal","Smoke","StarterPass123!","","auren","existing",null),409,"existing native identity cannot be repurposed");
        Fault(()=>portal.Register("portal","smoke","StarterPass123!","","auren","existing",null),409,"native name reservation ignores case");
        Fault(()=>portal.Register("Queue","Smoke","StarterPass123!","","../auren","choice",null),400,"request cannot select an arbitrary package path");
        string ticket=portal.Register("Queue","Smoke","StarterPass123!","","auren","first",null);
        Check(portal.Status(ticket)?.State=="queued"&&portal.Status(new string('B',64))==null,"opaque receipt sees only its own request");
        byte[] bytes=(byte[])Query("SELECT payload FROM mutsea_portal.registrations WHERE name='Queue Smoke'")!;
        Check(!Encoding.UTF8.GetString(bytes).Contains("StarterPass123!"),"pending password is not stored as plaintext");
        Check(portal.Register("Queue","Smoke","OtherPass123!","","auren","first",ticket)==ticket,"duplicate submission preserves its original provisioning request");
        bool inspected=false;
        Check(worker.ProcessOne(job=>{inspected=job.Password=="StarterPass123!"&&job.Avatar=="auren"&&job.Pin==settings.Avatars[0].Pin;return job.Id;}),"native callback consumes a queued request");
        Check(inspected&&portal.Status(ticket)?.State=="ready","native callback receives the authenticated encrypted payload");
        Check(Query("SELECT payload FROM mutsea_portal.registrations WHERE name='Queue Smoke'") is DBNull,"completion removes pending credential ciphertext");
        Check(!worker.ProcessOne(_=>throw new Exception()),"empty queue does not commit with an open reader");
        string broken=portal.Register("Broken","Binding","StarterPass123!","","auren","broken",null);
        Query("UPDATE mutsea_portal.registrations SET payload=set_byte(payload,30,get_byte(payload,30)#1) WHERE name='Broken Binding'");
        bool called=false;worker.ProcessOne(job=>{called=true;return job.Id;});
        Check(!called&&portal.Status(broken)?.State=="failed","tampered ciphertext never reaches the native provisioner");
        Check(Query("SELECT payload FROM mutsea_portal.registrations WHERE name='Broken Binding'") is DBNull,"failed request also removes ciphertext");
        string mismatch=portal.Register("Wrong","Identity","StarterPass123!","","auren","mismatch",null);
        worker.ProcessOne(_=>Guid.NewGuid());Check(portal.Status(mismatch)?.State=="failed","another identity cannot satisfy provisioning completion");
        string failure=portal.Register("Partial","Failure","StarterPass123!","","auren","partial",null);
        worker.ProcessOne(_=>throw new IOException("Fixture native failure"));Check(portal.Status(failure)?.State=="failed","native partial failure never reports readiness");
        string parallel=portal.Register("Single","Worker","StarterPass123!","","auren","parallel",null);int invoked=0;
        Parallel.For(0,2,_=>worker.ProcessOne(job=>{Interlocked.Increment(ref invoked);Thread.Sleep(100);return job.Id;}));
        Check(invoked==1&&portal.Status(parallel)?.State=="ready","parallel workers claim one request exactly once");
        string stale=portal.Register("Lease","Replay","StarterPass123!","","auren","lease",null);
        Query("UPDATE mutsea_portal.registrations SET state='processing',updated=now()-interval '11 minutes' WHERE name='Lease Replay'");
        worker.ProcessOne(job=>job.Id);Check(portal.Status(stale)?.State=="ready","stale worker lease is recoverable through native idempotency");
        string expired=portal.Register("Expired","Request","StarterPass123!","","auren","expiry",null);
        Query("UPDATE mutsea_portal.registrations SET created=now()-interval '2 hours' WHERE name='Expired Request'");
        worker.ProcessOne(_=>throw new Exception());Check(portal.Status(expired)?.State=="failed","expired queued secrets are cleared without creating an account");
        Query("TRUNCATE mutsea_portal.registrations");
        var one=new PgRegistrations(connection,settings with{Capacity=1});one.Register("Capacity","One","StarterPass123!","","auren","capacity-one",null);
        Fault(()=>one.Register("Capacity","Two","StarterPass123!","","auren","capacity-two",null),503,"durable preview capacity is enforced");
        Query("TRUNCATE mutsea_portal.registrations");
        for(int i=0;i<3;i++){portal.Register("Rate","User"+i,"StarterPass123!","","auren","rate-ip",null);worker.ProcessOne(_=>throw new IOException());}
        Fault(()=>portal.Register("Rate","Fourth","StarterPass123!","","auren","rate-ip",null),429,"failed requests still count against the daily IP limit");
        Query("TRUNCATE mutsea_portal.registrations");int accepted=0,conflicts=0;
        Parallel.For(0,8,i=>{try{portal.Register("Concurrent","Name","StarterPass123!","","auren","race-"+i,null);Interlocked.Increment(ref accepted);}catch(RegistrationFault e)when(e.Status==409){Interlocked.Increment(ref conflicts);}});
        Check(accepted==1&&conflicts==7,"concurrent name reservations accept exactly one registration");
        var recreated=new PgRegistrations(connection,settings);Check(recreated.Ready(),"worker readiness survives public service recreation");
        Query("UPDATE mutsea_portal.registration_worker SET seen=now()-interval '2 minutes'");Check(!recreated.Ready(),"stale native heartbeat closes registration");
        Console.WriteLine($"{passed} real PostgreSQL registration queue checks passed.");
    }
}
