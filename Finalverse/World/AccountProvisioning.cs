// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using Nini.Config;
using OpenMetaverse;
using MutSea.Region.Framework.Scenes;
using MutSea.Services.Interfaces;
using MutSea.Services.UserAccountService;

namespace Finalverse.World;

// Newcomer provisioning (account -> appearance): an operator creates local accounts through
// the inherited account service, exactly as the console "create user" does (password, home in
// the default region, inventory, starter appearance, activation only after verification), then
// gives each its chosen Genesis template through the wardrobe before the first login.
// Private configuration:
//   [FinalverseAccounts]
//   Enabled = true
//   HostRegion = <region UUID>
//   Newcomers = /config/accounts/newcomers.json   ; owner-only file; passwords are never logged or journaled
//   Home = 128,128                                ; optional arrival (x,y) in the host region; default its centre
// An existing account is never re-keyed or re-levelled; its Genesis assignment stays idempotent. A newcomer
// without a home gets the host arrival (the account service finds no default region while regions load).
// Accounts are not deleted by any rollback: a Genesis outfit is rolled back through the wardrobe.
public sealed partial class WorldModule
{
    bool accountsEnabled;string accountsHost="",accountsFile="";double[] accountsHome=[128,128];
    string AccountsJournal=>Path.Combine(directory,"accounts.newcomers.jsonl");

    void InitialiseAccounts(IConfigSource source)
    {
        var c=source.Configs["FinalverseAccounts"];
        accountsEnabled=c?.GetBoolean("Enabled",false)??false;
        if(!accountsEnabled)return;
        accountsHost=c!.GetString("HostRegion","");accountsFile=c.GetString("Newcomers","");
        if(!Guid.TryParse(accountsHost,out _)||!Path.IsPathRooted(accountsFile))throw new InvalidDataException("Newcomer provisioning needs a host region and an absolute newcomer file.");
        if(c.GetString("Home","") is {Length:>0} home)
        {
            accountsHome=home.Split(',',StringSplitOptions.TrimEntries).Select(x=>double.TryParse(x,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var v)?v:double.NaN).ToArray();
            if(accountsHome.Length!=2||accountsHome.Any(v=>!(v>=1&&v<=255)))throw new InvalidDataException("Newcomer home must be x,y within the region.");
        }
    }

    void AccountsPrimsLoaded(Scene s)
    {
        if(!accountsEnabled||RegionId!=accountsHost)return;
        lock(gate)
        {
            try
            {
                var info=new FileInfo(accountsFile);
                ContentCatalog.Require(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)==0,"Newcomer file is missing or linked.");
                if(!OperatingSystem.IsWindows())ContentCatalog.Require((File.GetUnixFileMode(accountsFile)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))==0,"Newcomer file must be owner-only.");
                var newcomers=NewcomerFile.Parse(File.ReadAllBytes(accountsFile));
                // The standalone local connector registers the inherited service itself as the region's account service.
                var service=scene.RequestModuleInterface<IUserAccountService>() as UserAccountService
                    ??throw new InvalidDataException("Newcomer provisioning needs the local (standalone) account service.");
                foreach(var n in newcomers.accounts)
                {
                    var account=scene.UserAccountService.GetUserAccount(UUID.Zero,n.first_name,n.last_name);
                    bool created=account==null;
                    if(created)
                    {
                        account=service.CreateUser(UUID.Zero,UUID.Random(),n.first_name,n.last_name,n.password,"");
                        // A failed provisioning stays stored and inactive for operator repair; never retried blindly.
                        ContentCatalog.Require(account!=null,"Account provisioning failed for "+n.first_name+" "+n.last_name+"; inspect it before retrying.");
                        AccountRecord("account_created",account!);
                    }
                    else ContentCatalog.Require(account!.UserLevel>=0,"Existing account "+n.first_name+" "+n.last_name+" is not active.");
                    // Only fill a missing home; an account's own home is never moved.
                    bool homed=false;var grid=scene.GridUserService.GetGridUserInfo(account!.PrincipalID.ToString());
                    if(grid==null||grid.HomeRegionID==UUID.Zero)
                    {
                        var at=new Vector3((float)accountsHome[0],(float)accountsHome[1],(float)Height(accountsHome[0],accountsHome[1]));
                        ContentCatalog.Require(scene.GridUserService.SetHome(account.PrincipalID.ToString(),scene.RegionInfo.RegionID,at,Vector3.UnitY),"Newcomer home could not be stored.");
                        homed=true;AccountRecord("home_set",account);
                    }
                    string? package=null;
                    if(n.genesis!=null)
                    {
                        var records=File.Exists(GenesisJournal)?File.ReadAllLines(GenesisJournal).Select(l=>JsonDocument.Parse(l).RootElement.Clone()).ToList():new List<JsonElement>();
                        GenesisProvision(records,account!.PrincipalID,n.genesis,n.genesis_sha256!.ToLowerInvariant());
                        package=Path.GetFileName(n.genesis.TrimEnd('/'));
                    }
                    log.Info(JsonSerializer.Serialize(new{@event="newcomer_ready",account_id=account!.PrincipalID.ToString(),created,home_set=homed,genesis=package}));
                }
            }
            catch(Exception ex){log.Error(JsonSerializer.Serialize(new{@event="newcomer_provisioning_failed",exception_type=ex.GetType().Name,error=ex.Message}));}
        }
    }

    void AccountRecord(string kind,UserAccount account)
    {
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(new{event_id=Guid.NewGuid(),kind,timestamp=DateTimeOffset.UtcNow,account_id=account.PrincipalID.ToString(),name=account.FirstName+" "+account.LastName,
            provisioning_state=account.ServiceURLs!=null&&account.ServiceURLs.TryGetValue("MutSeaProvisioningState",out var state)?state?.ToString():null,user_level=account.UserLevel});
        using var file=new FileStream(AccountsJournal,FileMode.Append,FileAccess.Write,FileShare.Read);file.Write(data);file.WriteByte(10);file.Flush(true);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(AccountsJournal,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }
}
