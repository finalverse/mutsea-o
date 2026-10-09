// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Bounded operator acceptance on the isolated loopback Harbor fixture. This
// sends native walking controls, not teleports through the object under test.
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.Packets;

if(args.Length!=4)throw new ArgumentException("Expected private account, retained bench/tree receipts and NEW evidence path.");
if(File.Exists(args[3]))throw new InvalidDataException("Preserve prior evidence.");
var account=JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
if(account.GetProperty("grid_url").GetString()!="http://127.0.0.1:18094/")throw new InvalidDataException("This behavioral fixture accepts the designated loopback grid only.");
var bench=JsonDocument.Parse(File.ReadAllBytes(args[1])).RootElement.GetProperty("entry").GetProperty("plan");
var tree=JsonDocument.Parse(File.ReadAllBytes(args[2])).RootElement.GetProperty("entry").GetProperty("plan");
if(bench.GetProperty("package").GetProperty("package_id").GetString()!="mutsea.starter.bench"||tree.GetProperty("package").GetProperty("package_id").GetString()!="mutsea.starter.coastal-tree")throw new InvalidDataException("Expected retained bench and tree fixtures.");
Vector3 Anchor(JsonElement plan){var a=plan.GetProperty("anchor");return new((float)a[0].GetDouble(),(float)a[1].GetDouble(),(float)a[2].GetDouble());}
var observations=new List<object>();
Settings.LOG_LEVEL=Helpers.LogLevel.Error;
var client=new GridClient();
client.Settings.LOGIN_SERVER="http://127.0.0.1:18094/";
client.Settings.OBJECT_TRACKING=true;client.Settings.ALWAYS_DECODE_OBJECTS=true;
client.Settings.SEND_AGENT_UPDATES=true;client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
void Stop(){client.Self.Movement.AtPos=false;client.Self.Movement.Stop=true;client.Self.Movement.SendUpdate(true);}
async Task Place(Vector3 position)
{
    Stop();client.Self.Movement.Fly=false;client.Self.Movement.AlwaysRun=false;
    var packet=new TeleportLocationRequestPacket();
    packet.AgentData.AgentID=client.Self.AgentID;packet.AgentData.SessionID=client.Self.SessionID;
    packet.Info.RegionHandle=client.Network.CurrentSim.Handle;packet.Info.Position=position;packet.Info.LookAt=Vector3.UnitY;
    client.Network.SendPacket(packet);
    var deadline=DateTime.UtcNow.AddSeconds(8);
    while(DateTime.UtcNow<deadline){await Task.Delay(100);var p=client.Self.SimPosition;if(Math.Abs(p.X-position.X)<.25&&Math.Abs(p.Y-position.Y)<.25)break;}
    await Task.Delay(500);
    var actual=client.Self.SimPosition;
    Require(Math.Abs(actual.X-position.X)<.25&&Math.Abs(actual.Y-position.Y)<.25,"Fixture positioning failed.");
}
async Task<(Vector3 start,Vector3 end,List<float[]> samples)> Walk(string name,Vector3 origin,float heading)
{
    await Place(origin);
    var start=client.Self.SimPosition;
    var rotation=new Quaternion(0,0,(float)Math.Sin(heading/2),(float)Math.Cos(heading/2));
    client.Self.Movement.BodyRotation=rotation;client.Self.Movement.HeadRotation=rotation;
    client.Self.Movement.Stop=false;client.Self.Movement.AtPos=true;
    var samples=new List<float[]>();
    try{for(int i=0;i<20;i++){client.Self.Movement.SendUpdate(true);await Task.Delay(100);var p=client.Self.SimPosition;samples.Add([p.X,p.Y,p.Z]);}}
    finally{Stop();}
    await Task.Delay(250);var end=client.Self.SimPosition;
    observations.Add(new{name,start=new[]{start.X,start.Y,start.Z},end=new[]{end.X,end.Y,end.Z},samples});
    return(start,end,samples);
}
try
{
    Require(client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"MutSea.InteractionSmoke","home","0.1.0"),"Fixture login failed.");
    var deadline=DateTime.UtcNow.AddSeconds(15);
    while(DateTime.UtcNow<deadline&&(client.Network.CurrentSim==null||client.Network.CurrentSim.ObjectsPrimitives.Copy().Count<40))await Task.Delay(100);
    Require(client.Network.CurrentSim?.RegionID==new UUID("a9d44538-26ca-4586-a8a9-e551722ffa15"),"Wrong fixture region.");
    foreach(var plan in new[]{bench,tree})
    {
        var id=new UUID(plan.GetProperty("entity_ids")[0].GetString());
        deadline=DateTime.UtcNow.AddSeconds(10);
        while(DateTime.UtcNow<deadline&&!client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.Any(p=>p.ID==id))await Task.Delay(100);
        var observed=client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==id);
        Require(observed!=null,"Retained content is not streamed.");
        var offset=plan.GetProperty("package").GetProperty("parts")[0].GetProperty("position");
        var expected=Anchor(plan)+new Vector3((float)offset[0].GetDouble(),(float)offset[1].GetDouble(),(float)offset[2].GetDouble());
        Require(Vector3.Distance(observed!.Position,expected)<.02f,"Retained content moved; use a matching fixture receipt.");
        Require(plan.GetProperty("actor_id").GetString()==client.Self.AgentID.ToString(),"Use the fixture content owner's account.");
    }
    var b=Anchor(bench);var t=Anchor(tree);
    var clear=await Walk("bench_clear_lane",b+new Vector3(2.2f,1.8f,1.2f),-MathF.PI/2);
    Require(clear.start.Y-clear.end.Y>2.2f&&Math.Abs(clear.start.X-clear.end.X)<.3f,"Empty control lane did not permit walking.");
    var blocked=await Walk("bench_back_obstruction",b+new Vector3(0,1.8f,1.2f),-MathF.PI/2);
    // The inherited character controller can step/climb low furniture. If it
    // crosses the back, its center must first rise above the actual backrest;
    // passing straight through at terrain height is a failure, as in BasicPhysics.
    bool stopped=blocked.end.Y>b.Y+.4f;
    bool climbed=blocked.samples.Any(p=>Math.Abs(p[1]-(b.Y+.32f))<.28f&&p[2]>b.Z+1.75f);
    Require(blocked.start.Y-blocked.end.Y>.35f&&(stopped||climbed)&&Math.Abs(blocked.start.X-blocked.end.X)<.35f,"Bench path neither stopped nor cleared the backrest vertically.");
    var foliage=await Walk("tree_foliage_clearance",t+new Vector3(1.15f,-1.8f,1.2f),MathF.PI/2);
    Require(foliage.end.Y-foliage.start.Y>2.2f&&Math.Abs(foliage.start.X-foliage.end.X)<.3f,"Noncolliding foliage obstructed walking.");
    var trunk=await Walk("tree_trunk_obstruction",t+new Vector3(0,-1.8f,1.2f),MathF.PI/2);
    Require(trunk.end.Y-trunk.start.Y>.35f&&trunk.end.Y<t.Y-.15f&&Math.Abs(trunk.start.X-trunk.end.X)<.35f,"Tree shaft did not block native walking.");
    File.WriteAllText(args[3],JsonSerializer.Serialize(new{verified=true,bench_version=bench.GetProperty("package").GetProperty("version").GetString(),tree_version=tree.GetProperty("package").GetProperty("version").GetString(),observations},new JsonSerializerOptions{WriteIndented=true}));
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[3],UnixFileMode.UserRead|UnixFileMode.UserWrite);
    Console.WriteLine("PASS native bench collision response / empty lane / tree trunk / foliage clearance (four walks).");return 0;
}
catch(Exception error)
{
    var diagnostic=args[3]+".diagnostic";
    File.WriteAllText(diagnostic,JsonSerializer.Serialize(new{error=error.GetType().Name,message=error is InvalidDataException?error.Message:"Native interaction probe failed.",observations},new JsonSerializerOptions{WriteIndented=true}));
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(diagnostic,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    Console.Error.WriteLine("FAILED native interaction acceptance; bounded diagnostic retained privately.");return 1;
}
finally{if(client.Network.Connected){Stop();client.Network.Logout();}}
