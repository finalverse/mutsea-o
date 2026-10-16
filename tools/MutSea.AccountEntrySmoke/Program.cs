// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Explicit hosted preview acceptance. Uses only the operator's own synthetic
// registered account, preserves appearance/world state, logs out on every path.
using System.Text.Json;
using OpenMetaverse;
if(args.Length!=2)throw new ArgumentException("Expected private synthetic-account file and new receipt path.");
var file=new FileInfo(args[0]);
if(!file.Exists||file.LinkTarget!=null||file.Length>16384||File.Exists(args[1]))throw new InvalidDataException("Regular private input and new output required.");
if(!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(args[0])&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.OtherRead|UnixFileMode.OtherWrite))!=0)throw new InvalidDataException("Account must be owner-only.");
using var account=JsonDocument.Parse(File.ReadAllBytes(args[0]));var a=account.RootElement;
var client=new GridClient();Settings.LOG_LEVEL=Helpers.LogLevel.Error;
client.Settings.LOGIN_SERVER="https://grid.mutsea.com/";
client.Settings.SEND_AGENT_APPEARANCE=false;client.Settings.SEND_AGENT_UPDATES=true;
client.Settings.ALWAYS_DECODE_OBJECTS=true;client.Settings.OBJECT_TRACKING=true;client.Settings.MULTIPLE_SIMS=false;
string stage="native_login";
try
{
    if(!client.Network.Login(a.GetProperty("firstName").GetString(),a.GetProperty("lastName").GetString(),a.GetProperty("password").GetString(),"MutSea.AccountEntrySmoke","home","0.1"))throw new InvalidDataException("Native login failed.");
    stage="udp_scene";var until=DateTime.UtcNow.AddSeconds(35);
    while(DateTime.UtcNow<until&&(client.Network.CurrentSim?.ObjectsPrimitives.Count??0)<10)await Task.Delay(200);
    var sim=client.Network.CurrentSim??throw new InvalidDataException("No native simulator.");
    if(sim.RegionID.ToString()!="1e485f25-50dd-43e8-bb3a-750b5648f707"||sim.ObjectsPrimitives.Count<10)throw new InvalidDataException("Starter scene did not stream through UDP.");
    stage="capabilities";var untilCaps=DateTime.UtcNow.AddSeconds(15);Uri? texture=null;
    while(DateTime.UtcNow<untilCaps&&(texture=sim.Caps?.CapabilityURI("GetTexture"))==null)await Task.Delay(200);
    if(texture?.Scheme!="https"||texture.Host!="grid.mutsea.com"||texture.Port!=443)throw new InvalidDataException("Expected verified public TLS capability origin.");
    stage="logout";int objects=sim.ObjectsPrimitives.Count;client.Network.Logout();
    if(client.Network.Connected)throw new InvalidDataException("Logout did not disconnect.");
    File.WriteAllText(args[1],JsonSerializer.Serialize(new{observedUtc=DateTimeOffset.UtcNow,registeredAccountLogin=true,publicTls=true,udpSceneStreaming=true,starterRegionVerified=true,httpsCapabilityOriginVerified=true,objectsReceived=objects,logoutVerified=true},new JsonSerializerOptions{WriteIndented=true}));
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(args[1],UnixFileMode.UserRead|UnixFileMode.UserWrite);
    Console.WriteLine("PASS registered native account: public TLS login, UDP scene, HTTPS capabilities and logout.");return 0;
}
catch(Exception ex){Console.Error.WriteLine("Account-entry check failed at "+stage+": "+ex.GetType().Name);return 1;}
finally{if(client.Network.Connected)client.Network.Logout();}
