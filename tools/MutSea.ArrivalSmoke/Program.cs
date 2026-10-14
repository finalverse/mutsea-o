// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Native avatar-only walking/Sit/Stand proof on one named local hero fixture.
// No model, appearance, world mutation or provisioning operation is sent.
using System.Text.Json;
using OpenMetaverse;
using OpenMetaverse.Packets;

if(args.Length!=3)throw new ArgumentException("Expected private hero account, world proof and NEW receipt.");
void Require(bool condition,string reason){if(!condition)throw new InvalidDataException(reason);}
JsonElement Read(string path,int limit,bool ownerOnly=false)
{
    var file=new FileInfo(path);Require(file.Exists&&file.LinkTarget==null&&file.Length is >0&&file.Length<=limit,"Bounded regular input required");
    if(ownerOnly&&!OperatingSystem.IsWindows())Require((File.GetUnixFileMode(path)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))==0,"Private account required");
    using var doc=JsonDocument.Parse(File.ReadAllBytes(path));return doc.RootElement.Clone();
}
var account=Read(args[0],16384,true);var proof=Read(args[1],16*1024*1024);
Require(account.GetProperty("grid_url").GetString()=="http://127.0.0.1:18099/"&&account.GetProperty("fixture").GetString()=="hero-arrival-v1"&&account.GetProperty("registered").GetBoolean(),"Named local hero account required");
Require(account.GetProperty("region_id").GetString()=="38bfc868-034e-4a29-acd6-5ff203e96e57"&&proof.GetProperty("world").GetProperty("region_id").GetString()==account.GetProperty("region_id").GetString(),"Hero region mismatch");
Require(proof.GetProperty("world").GetProperty("status").GetString()=="committed"&&proof.GetProperty("world").GetProperty("version").GetString()=="0.2.0","Committed hero world required");
var arrival=proof.GetProperty("places").EnumerateArray().Single(p=>p.GetProperty("plan").GetProperty("place").GetProperty("place_id").GetString()=="mutsea.starter.coastal-arrival").GetProperty("plan");
var placements=arrival.GetProperty("placements").EnumerateArray().ToArray();
var definitions=arrival.GetProperty("place").GetProperty("placements").EnumerateArray().ToArray();
JsonElement Placement(string id)=>placements[Array.FindIndex(definitions,d=>d.GetProperty("local_id").GetString()==id)];
var canopy=Placement("canopy_west");var bench=Placement("bench_west");
Vector3 V(JsonElement value)
{
    Require(value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()==3&&value.EnumerateArray().All(v=>double.IsFinite(v.GetDouble())&&Math.Abs(v.GetDouble())<=512),"Bounded geometry vector required");
    return new((float)value[0].GetDouble(),(float)value[1].GetDouble(),(float)value[2].GetDouble());
}
Vector3 Anchor(JsonElement plan)=>V(plan.GetProperty("anchor"));
Quaternion Rotation(JsonElement plan){double yaw=plan.GetProperty("yaw_degrees").GetDouble();Require(double.IsFinite(yaw)&&Math.Abs(yaw)<=360,"Bounded placement yaw required");var angle=(float)(yaw*Math.PI/180);return new(0,0,MathF.Sin(angle/2),MathF.Cos(angle/2));}
Vector3 At(JsonElement plan,Vector3 offset)=>offset*Rotation(plan)+Anchor(plan);
Require(!File.Exists(args[2]),"Preserve previous receipt");
var observations=new List<object>();var client=new GridClient();Settings.LOG_LEVEL=Helpers.LogLevel.Error;
client.Settings.LOGIN_SERVER="http://127.0.0.1:18099/";client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.MULTIPLE_SIMS=false;
client.Settings.SEND_AGENT_UPDATES=true;client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;
string stage="login";
void Stop(){client.Self.Movement.AtPos=false;client.Self.Movement.Stop=true;client.Self.Movement.SendUpdate(true);}
async Task Place(Vector3 position)
{
    Require(float.IsFinite(position.X)&&float.IsFinite(position.Y)&&float.IsFinite(position.Z)&&position.X is >=0 and <=256&&position.Y is >=0 and <=256&&position.Z is >=0 and <=128,"Setup must remain inside the local hero region");
    Stop();client.Self.Movement.Fly=false;client.Self.Movement.AlwaysRun=false;
    var packet=new TeleportLocationRequestPacket();packet.AgentData.AgentID=client.Self.AgentID;packet.AgentData.SessionID=client.Self.SessionID;
    var sim=client.Network.CurrentSim??throw new InvalidDataException("No active hero simulator");
    Require(sim.RegionID.ToString()==account.GetProperty("region_id").GetString(),"Hero region changed before setup");
    packet.Info.RegionHandle=sim.Handle;packet.Info.Position=position;packet.Info.LookAt=Vector3.UnitY;client.Network.SendPacket(packet);
    var until=DateTime.UtcNow.AddSeconds(8);
    while(DateTime.UtcNow<until){await Task.Delay(100);var p=client.Self.SimPosition;if(Math.Abs(p.X-position.X)<.25&&Math.Abs(p.Y-position.Y)<.25)break;}
    await Task.Delay(500);Require(Math.Abs(client.Self.SimPosition.X-position.X)<.25&&Math.Abs(client.Self.SimPosition.Y-position.Y)<.25,"Native setup positioning failed");
}
async Task<(Vector3 start,Vector3 end,List<float[]> samples)> Walk(string name,Vector3 position,float heading,int steps,float stopDistance=float.PositiveInfinity)
{
    await Place(position);var start=client.Self.SimPosition;var rotation=new Quaternion(0,0,MathF.Sin(heading/2),MathF.Cos(heading/2));
    client.Self.Movement.BodyRotation=rotation;client.Self.Movement.HeadRotation=rotation;client.Self.Movement.Stop=false;client.Self.Movement.AtPos=true;
    var samples=new List<float[]>();
    try{for(int i=0;i<steps;i++){client.Self.Movement.SendUpdate(true);await Task.Delay(100);var p=client.Self.SimPosition;samples.Add([p.X,p.Y,p.Z]);if(Vector3.Distance(p,start)>=stopDistance)break;}}
    finally{Stop();}
    await Task.Delay(250);var end=client.Self.SimPosition;observations.Add(new{name,start=new[]{start.X,start.Y,start.Z},end=new[]{end.X,end.Y,end.Z},samples});return(start,end,samples);
}
try
{
    Require(client.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"MutSea.ArrivalSmoke","home","0.1.0"),"Hero native login failed");
    Require(client.Self.AgentID.ToString()==account.GetProperty("user_id").GetString(),"Authenticated account mismatch");
    var deadline=DateTime.UtcNow.AddSeconds(20);var ids=new[]{new UUID(canopy.GetProperty("entity_ids")[0].GetString()),new UUID(bench.GetProperty("entity_ids")[0].GetString())};
    while(DateTime.UtcNow<deadline){var sim=client.Network.CurrentSim;if(sim!=null&&sim.RegionID.ToString()==account.GetProperty("region_id").GetString()&&ids.All(id=>sim.ObjectsPrimitives.Copy().Values.Any(p=>p.ID==id)))break;await Task.Delay(100);}
    Require(client.Network.CurrentSim?.RegionID.ToString()==account.GetProperty("region_id").GetString(),"Wrong native hero region");
    foreach(var plan in new[]{canopy,bench})
    {
        var id=new UUID(plan.GetProperty("entity_ids")[0].GetString());var p=client.Network.CurrentSim!.ObjectsPrimitives.Copy().Values.Single(p=>p.ID==id);
        Require(Vector3.Distance(p.Position,At(plan,V(plan.GetProperty("package").GetProperty("parts")[0].GetProperty("position"))))<.02f,"Hero geometry moved after provisioning");
    }
    stage="axis";var axis=await Walk("eighteen_metre_arrival_axis",Anchor(arrival)+new Vector3(0,-9,1.2f),MathF.PI/2,110,18.1f);
    Require(axis.end.Y-axis.start.Y>=18&&Math.Abs(axis.end.X-axis.start.X)<.35f&&axis.samples.All(p=>Math.Abs(p[2]-axis.start.Z)<.65f),"Arrival axis blocked or required flying/climbing");
    float yaw=(float)(canopy.GetProperty("yaw_degrees").GetDouble()*Math.PI/180);
    stage="canopy";var clear=await Walk("open_canopy_approach",At(canopy,new Vector3(0,-2.4f,1.2f)),yaw+MathF.PI/2,35,1.6f);
    Require(Vector3.Distance(clear.start,clear.end)>1.5f&&clear.samples.All(p=>Math.Abs(p[2]-clear.start.Z)<.65f),"Canopy void blocks its approach");
    stage="post";var blocked=await Walk("solid_canopy_post",At(canopy,new Vector3(-1.65f,-2.4f,1.2f)),yaw+MathF.PI/2,25);
    var local=(blocked.end-Anchor(canopy))*Quaternion.Inverse(Rotation(canopy));
    Require(Vector3.Distance(blocked.start,blocked.end)>.3f&&local.Y<-1.15f,"Canopy post failed to stop native walking");
    stage="sit";Stop();var seatIndex=bench.GetProperty("package").GetProperty("parts").EnumerateArray().Select((p,i)=>(p,i)).Single(p=>p.p.TryGetProperty("seat",out var s)&&s.ValueKind==JsonValueKind.Object).i;
    var seatId=new UUID(bench.GetProperty("entity_ids")[seatIndex].GetString());await Place(At(bench,new Vector3(0,-1.8f,1.2f)));
    var sit=new TaskCompletionSource<AvatarSitResponseEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);client.Self.AvatarSitResponse+=(_,e)=>sit.TrySetResult(e);client.Self.RequestSit(seatId,Vector3.Zero);
    Require(await Task.WhenAny(sit.Task,Task.Delay(8000))==sit.Task,"Native sit response timed out");
    Require((await sit.Task).ObjectID==new UUID(bench.GetProperty("entity_ids")[0].GetString()),"Wrong native seated object");
    deadline=DateTime.UtcNow.AddSeconds(8);while(client.Self.SittingOn==0&&DateTime.UtcNow<deadline)await Task.Delay(100);Require(client.Self.SittingOn!=0,"Avatar did not sit");
    client.Self.Stand();deadline=DateTime.UtcNow.AddSeconds(8);while(client.Self.SittingOn!=0&&DateTime.UtcNow<deadline)await Task.Delay(100);Require(client.Self.SittingOn==0,"Avatar did not stand");
    stage="logout";Stop();client.Network.Logout();deadline=DateTime.UtcNow.AddSeconds(5);while(client.Network.Connected&&DateTime.UtcNow<deadline)await Task.Delay(50);Require(!client.Network.Connected,"Native logout not confirmed");
    using var output=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write,FileShare.None);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[2],UnixFileMode.UserRead|UnixFileMode.UserWrite);
    JsonSerializer.Serialize(output,new{verified=true,world_mutations=false,appearance_sent=false,axis=true,canopy_approach=true,solid_post=true,sit=true,stand=true,logout=true,observations},new JsonSerializerOptions{WriteIndented=true});
    Console.WriteLine("ARRIVAL_SMOKE PASS: eighteen-metre walk, canopy clearance/post, native Sit/Stand/logout.");return 0;
}
catch(Exception e)
{
    var diagnostic=args[2]+".diagnostic";using var file=new FileStream(diagnostic,FileMode.CreateNew);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(diagnostic,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    JsonSerializer.Serialize(file,new{stage,type=e.GetType().Name,reason=e is InvalidDataException?e.Message:"Native probe failed",observations});Console.Error.WriteLine("ARRIVAL_SMOKE_FAILED stage="+stage);return 1;
}
finally{if(client.Network.Connected){Stop();client.Network.Logout();}}
