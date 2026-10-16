// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using Npgsql;
using Nini.Config;
using OpenMetaverse;
using MutSea.AccountPortal;
using MutSea.Registration;
using MutSea.Services.AvatarService;
using MutSea.Services.InventoryService;
using MutSea.Services.UserAccountService;

// Operator-run acceptance against a disposable clone with the real region
// module running. No fake provisioning callback or production database allowed.
static class WorldRegistrationChecks
{
    sealed record Receipt(string Ticket,string Name);
    public static async Task Run(string mode,string directory)
    {
        string connection=PortalOptions.ReadSecret(Path.Combine(directory,"database"));
        string admin=PortalOptions.ReadSecret(Path.Combine(directory,"admin-database"));
        string database=new NpgsqlConnectionStringBuilder(connection).Database!;
        if(!database.StartsWith("mutsea_accounts_fixture_worldreg_")||new NpgsqlConnectionStringBuilder(admin).Database!=database)
            throw new Exception("Refusing a non-world-fixture database.");
        var settings=RegistrationSettings.Load(Path.Combine(directory,"registration.json"));
        var queue=new PgRegistrations(connection,settings);
        string password=PortalOptions.ReadSecret(Path.Combine(directory,"password"));
        string receiptFile=Path.Combine(directory,"receipt.json");
        if(mode=="queue")
        {
            if(File.Exists(receiptFile))throw new Exception("Fixture already submitted; verify or use a fresh fixture.");
            string first="Arrival"+Guid.NewGuid().ToString("N")[..8];
            string ticket=queue.Register(first,"Test",password,"","auren","world-fixture",null);
            File.WriteAllText(receiptFile,JsonSerializer.Serialize(new Receipt(ticket,first+" Test")));
            File.SetUnixFileMode(receiptFile,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            Console.WriteLine("PASS real native region accepted a queued registration.");return;
        }
        var receipt=JsonSerializer.Deserialize<Receipt>(PortalOptions.ReadSecret(receiptFile))!;
        RegistrationStatus? status=null;
        for(int i=0;i<60;i++)
        {
            status=queue.Status(receipt.Ticket);
            if(status?.State is "ready" or "failed")break;
            await Task.Delay(1000);
        }
        int passed=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);passed++;Console.WriteLine("PASS world "+label);}
        Check(status?.State=="ready","real region worker completed provisioning");
        NativeIdentity.PrepareServices();
        var identity=new NativeIdentity(connection,new byte[32],new());var user=identity.Find(receipt.Name);
        Check(user!=null&&identity.Verify(user.Id,password),"native password authenticates the registered name");
        Check(user!.Home?.RegionId==settings.HostRegion&&user.Home.Position[2]>0,"persistent home belongs to the selected starter world");
        var config=new IniConfigSource();foreach(string section in new[]{"DatabaseService","AvatarService","InventoryService","UserAccountService"})
        {var c=config.AddConfig(section);c.Set("StorageProvider","MutSea.Data.PGSQL.dll");c.Set("ConnectionString",admin);}
        var id=new UUID(user.Id.ToString());var account=new UserAccountService(config).GetUserAccount(UUID.Zero,id);
        Check(account.UserLevel==0&&account.UserFlags==0,"ordinary user activated without elevated role");
        var appearance=new AvatarService(config).GetAppearance(id);var inventory=new XInventoryService(config);
        int wearables=0;bool valid=true;
        for(int t=0;t<appearance.Wearables.Length;t++)for(int n=0;n<appearance.Wearables[t].Count;n++)
        {
            var worn=appearance.Wearables[t][n];var item=inventory.GetItem(id,worn.ItemID);
            valid&=item!=null&&item.Owner==id&&item.AssetID==worn.AssetID;wearables++;
        }
        Check(wearables>=7&&valid,"default wearables and Genesis alpha resolve to owned inventory");
        var attached=appearance.GetAttachments();
        Check(attached.Count>0&&attached.All(a=>inventory.GetItem(id,a.ItemID)?.Owner==id),"Genesis mesh attachments resolve to owned inventory");
        var cof=inventory.GetFolderForType(id,FolderType.CurrentOutfit);var contents=cof==null?null:inventory.GetFolderContent(id,cof.ID);
        Check(contents!=null&&contents.Items.Count>=wearables+attached.Count,"current outfit contains wearable and mesh links");
        using var db=new NpgsqlConnection(admin);db.Open();
        using var cmd=new NpgsqlCommand("SELECT payload IS NULL FROM mutsea_portal.registrations WHERE name=@name",db);cmd.Parameters.AddWithValue("name",receipt.Name);
        Check((bool)cmd.ExecuteScalar()!,"queued credential ciphertext removed after native activation");
        Console.WriteLine($"{passed} real running-world registration checks passed.");
    }
}
