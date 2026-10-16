// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using Npgsql;
using Nini.Config;
using OpenMetaverse;
using MutSea.AccountPortal;
using MutSea.Services.AuthenticationService;
using MutSea.Services.UserAccountService;

// Destructive checks are restricted to a disposable, explicitly named fixture
// database. No live account, appearance, inventory or region is changed.
static class NativeChecks
{
    sealed record Fixture(string ConnectionFile,string AdminConnectionFile,string KeyFile,string PasswordFile);
    public static void Run(string file)
    {
        var f=JsonSerializer.Deserialize<Fixture>(PortalOptions.ReadSecret(file))!;
        string connection=PortalOptions.ReadSecret(f.ConnectionFile),admin=PortalOptions.ReadSecret(f.AdminConnectionFile);
        string database=new NpgsqlConnectionStringBuilder(connection).Database!;
        if(!database.StartsWith("mutsea_accounts_fixture_")||new NpgsqlConnectionStringBuilder(admin).Database!=database)throw new Exception("Refusing a non-fixture database.");
        byte[] key=Convert.FromHexString(PortalOptions.ReadSecret(f.KeyFile));string password=PortalOptions.ReadSecret(f.PasswordFile);
        NativeIdentity.PrepareServices();
        var config=new IniConfigSource();
        foreach(string section in new[]{"DatabaseService","UserAccountService","AuthenticationService","GridUserService"})
        {var c=config.AddConfig(section);c.Set("StorageProvider","MutSea.Data.PGSQL.dll");c.Set("ConnectionString",admin);}
        var users=new UserAccountService(config);var auth=new PasswordAuthenticationService(config);var grid=new GridUserService(config);
        var account=users.GetUserAccount(UUID.Zero,"Portal","Smoke")??users.CreateUser(UUID.Zero,UUID.Random(),"Portal","Smoke",password,"portal@example.invalid");
        if(account==null||!auth.SetPassword(account.PrincipalID,password))throw new Exception("Fixture seed failed.");
        var region=UUID.Random();grid.SetHome(account.PrincipalID.ToString(),region,new Vector3(128,129,25),new Vector3(1,0,0));
        int passed=0;void Check(bool yes,string name){if(!yes)throw new Exception(name);passed++;Console.WriteLine("PASS native "+name);}
        var state=new PgState(connection);state.Revoke(Guid.Parse(account.PrincipalID.ToString()));
        var identity=new NativeIdentity(connection,key,new(){[Guid.Parse(region.ToString())]="Fixture Harbor"});
        var core=new Accounts(identity,state,key);string token=core.Login("Portal Smoke",password,"fixture");
        var signed=core.Require(token);Check(signed.Account.Home?.Name=="Fixture Harbor"&&signed.Account.Home.Position[1]==129,"real home region and position");
        Check(!identity.Verify(signed.Account.Id,"incorrect"),"native wrong password denied");
        var current=state.Profile(signed.Account.Id);var profile=core.SaveProfile(token,"Fixture <text>","portal@example.invalid","en",current.Version);
        var restarted=new Accounts(new NativeIdentity(connection,key,new()),new PgState(connection),key);
        Check(restarted.Require(token).Account.Id==signed.Account.Id,"session survives service recreation");
        Check(restarted.Profile(restarted.Require(token)).Version==profile.Version,"profile survives service recreation");
        var codes=core.Codes(token,password,"fixture");Check(codes.Length==6&&core.CodeCount(signed)==6,"native reauthentication and persisted code digests");
        int successes=0,rejected=0;Parallel.For(0,2,i=>{try{core.Reset("Portal Smoke",codes[0],"FixtureNew123!","fixture-"+i);Interlocked.Increment(ref successes);}catch(PortalFault x)when(x.Status==401){Interlocked.Increment(ref rejected);}});
        Check(successes==1&&rejected==1,"concurrent reset accepts exactly once under PostgreSQL lock");
        Check(core.Session(token)==null,"native reset invalidates persistent sessions");
        Check(!identity.Verify(signed.Account.Id,password)&&identity.Verify(signed.Account.Id,"FixtureNew123!"),"same native password service reads the changed world credential");
        token=core.Login("Portal Smoke","FixtureNew123!","fixture2");core.ChangePassword(token,"FixtureNew123!",password,"fixture2");Check(identity.Verify(signed.Account.Id,password),"native change-password restores fixture password");
        using var db=new NpgsqlConnection(connection);db.Open();
        bool Privilege(string relation,string privilege){using var cmd=new NpgsqlCommand("SELECT has_table_privilege(current_user,@r,@p)",db);cmd.Parameters.AddWithValue("r",relation);cmd.Parameters.AddWithValue("p",privilege);return (bool)cmd.ExecuteScalar()!;}
        Check(!Privilege("public.useraccounts","UPDATE")&&!Privilege("public.useraccounts","INSERT"),"cannot create accounts or change user roles");
        Check(!Privilege("public.prims","SELECT")&&!Privilege("public.prims","UPDATE")&&!Privilege("public.assets","SELECT")&&!Privilege("public.inventoryitems","UPDATE"),"no object, asset or inventory access");
        Check(!Privilege("public.migrations","UPDATE")&&!Privilege("public.auth","DELETE"),"cannot migrate native tables or delete credentials");
        using(var cmd=new NpgsqlCommand("SELECT has_schema_privilege(current_user,'public','CREATE')",db))Check(!(bool)cmd.ExecuteScalar()!,"cannot create native schema objects");
        state.Prune(DateTimeOffset.UtcNow+TimeSpan.FromDays(100));Check(state.Session(Accounts.Hash(token))==null,"expired session retention cleanup");
        Console.WriteLine($"{passed} real native/PostgreSQL checks passed.");
        RegistrationChecks.Run(connection,admin,f.KeyFile);
    }
}
