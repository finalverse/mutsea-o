// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using Nini.Config;
using MutSea.Region.Framework.Scenes;
using MutSea.Registration;

namespace Finalverse.World;
public sealed partial class WorldModule
{
    PgRegistrations? registrations;
    System.Threading.Timer? registrationTimer;
    DateTimeOffset registrationErrorAt;
    void InitialiseRegistration(IConfigSource source)
    {
        var config=source.Configs["FinalverseRegistration"];
        if(!(config?.GetBoolean("Enabled",false)??false))return;
        var settings=RegistrationSettings.Load(config!.GetString("Configuration",""));
        ContentCatalog.Require(accountsEnabled&&settings.HostRegion.ToString()==accountsHost,"Registration must use the configured newcomer host and home.");
        registrations=new PgRegistrations(source.Configs["DatabaseService"]?.GetString("ConnectionString","")??"",settings);
    }
    void RegistrationPrimsLoaded(Scene s)
    {
        if(registrations==null||registrations.Settings.HostRegion.ToString()!=RegionId)return;
        foreach(var choice in registrations.Settings.Avatars)AvatarCatalog.LoadWardrobe(choice.Directory,choice.Pin,out _);
        registrationTimer=new System.Threading.Timer(_=>PollRegistrations(),null,TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3));
    }
    void StopRegistration(){registrationTimer?.Dispose();registrationTimer=null;}
    void PollRegistrations()
    {
        if(!Monitor.TryEnter(gate))return;
        try
        {
            if(registrations==null||WorldRecoveryRequired)return;
            // An independent native journal budget bounds creation even if the
            // public queue process is compromised. No role or world-owner input
            // is accepted from registration requests.
            var activated=new HashSet<Guid>();
            if(File.Exists(AccountsJournal))
            {
                ContentCatalog.Require(new FileInfo(AccountsJournal).Length<=32*1024*1024,"Registration journal needs operator maintenance.");
                foreach(string line in File.ReadLines(AccountsJournal))
                {
                    using var d=JsonDocument.Parse(line);var row=d.RootElement;
                    if(row.GetProperty("kind").GetString()=="web_registration_ready"&&row.TryGetProperty("admission_id",out var id)&&Guid.TryParse(id.GetString(),out var admission))activated.Add(admission);
                }
            }
            registrations.Heartbeat();
            registrations.ProcessOne(job=>{
                ContentCatalog.Require(activated.Contains(job.Id)||activated.Count<registrations.Settings.Capacity,"Native registration capacity reached.");
                var choice=registrations.Settings.Choice(job.Avatar);
                ContentCatalog.Require(choice.Pin==job.Pin,"Registration starter pin changed.");
                var newcomer=new NewcomerAccount(job.FirstName,job.LastName,job.Password,choice.Directory,choice.Pin,job.Id.ToString());
                var account=ProvisionNewcomer(newcomer,job.Email);
                AccountRecord("web_registration_ready",account,job.Id.ToString());
                return Guid.Parse(account.PrincipalID.ToString());
            });
        }
        catch(Exception ex)
        {
            if(DateTimeOffset.UtcNow-registrationErrorAt>TimeSpan.FromMinutes(1))
            {
                registrationErrorAt=DateTimeOffset.UtcNow;
                log.Warn("Registration worker is unavailable: "+ex.GetType().Name);
            }
        }
        finally{Monitor.Exit(gate);}
    }
}
