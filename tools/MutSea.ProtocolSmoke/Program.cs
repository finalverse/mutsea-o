// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Real HTTP login + UDP scene streaming against a named development fixture.
// Account secrets are read from a private file and never emitted or put in argv.
using System.Text.Json;
using System.Globalization;
using OpenMetaverse;

if(args.Length != 2) { Console.Error.WriteLine("Usage: MutSea.ProtocolSmoke <private-account.json> <scene-directory>"); return 2; }
using var account = JsonDocument.Parse(File.ReadAllText(args[0]));
var a = account.RootElement;
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1],"manifest.json")));
var expected = new System.Xml.XmlDocument(); expected.Load(Path.Combine(args[1],"harbor.xml"));
var names = expected.SelectNodes("/scene/SceneObjectGroup/SceneObjectPart/Name")!.Cast<System.Xml.XmlNode>().Select(n=>n.InnerText).ToHashSet();
if (names.Count != manifest.RootElement.GetProperty("objects").GetInt32())
    throw new InvalidDataException("Fixture object count does not match manifest");
Vector3 ReadVector(System.Xml.XmlNode node) => new(
    float.Parse(node["X"]!.InnerText,CultureInfo.InvariantCulture),
    float.Parse(node["Y"]!.InnerText,CultureInfo.InvariantCulture),
    float.Parse(node["Z"]!.InnerText,CultureInfo.InvariantCulture));
var client = new GridClient();
client.Settings.LOGIN_SERVER = a.GetProperty("grid_url").GetString();
client.Settings.ALWAYS_DECODE_OBJECTS = true;
client.Settings.OBJECT_TRACKING = true;
client.Settings.SEND_AGENT_UPDATES = true;
client.Settings.SEND_AGENT_APPEARANCE = false; // This probe exercises scene streaming, not avatar baking.
client.Settings.MULTIPLE_SIMS = false;
try {
    if(!client.Network.Login(a.GetProperty("first_name").GetString(),a.GetProperty("last_name").GetString(),
        a.GetProperty("password").GetString(),"MutSea.ProtocolSmoke","home","1.0"))
        throw new InvalidOperationException("Login failed: "+client.Network.LoginMessage);
    var deadline = DateTime.UtcNow.AddSeconds(45);
    Primitive[] prims = Array.Empty<Primitive>();
    do {
        prims = client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.ToArray();
        if(prims.Length >= names.Count) break;
        Thread.Sleep(250);
    } while(DateTime.UtcNow < deadline);
    foreach(var prim in prims) client.Objects.RequestObjectPropertiesFamily(client.Network.CurrentSim,prim.ID);
    deadline = DateTime.UtcNow.AddSeconds(45);
    do {
        prims = client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.ToArray();
        if(names.All(name=>prims.Any(prim=>prim.Properties?.Name==name))) break;
        Thread.Sleep(250);
    } while(DateTime.UtcNow < deadline);
    string[] missing = names.Where(name=>!prims.Any(prim=>prim.Properties?.Name==name)).ToArray();
    if(missing.Length != 0) throw new InvalidDataException("Scene objects missing from real protocol: "+string.Join(", ",missing));
    if(client.Network.CurrentSim.Name != "MutSea Harbor") throw new InvalidDataException("Unexpected region");
    var loginUri = new Uri(a.GetProperty("grid_url").GetString()!);
    var seedUri = new Uri(client.Network.CurrentSim.Caps.SeedCapsURI);
    if (loginUri.Scheme == "https" && (seedUri.Scheme != "https"
        || seedUri.Host != loginUri.Host || seedUri.Port != loginUri.Port))
        throw new InvalidDataException("HTTPS login advertised an unexpected capability origin");
    foreach(System.Xml.XmlNode part in expected.SelectNodes("/scene/SceneObjectGroup/SceneObjectPart")!) {
        var prim = prims.Single(p=>p.Properties?.Name == part["Name"]!.InnerText);
        if (prim.ID != new UUID(part["UUID"]!["UUID"]!.InnerText)
            || Vector3.Distance(prim.Position,ReadVector(part["GroupPosition"]!)) > .01f
            || Vector3.Distance(prim.Scale,ReadVector(part["Scale"]!)) > .01f)
            throw new InvalidDataException("Streamed identity or transform differs: "+prim.Properties.Name);
    }
    var receipt = new {
        login=true, udp_scene_stream=true, transforms_verified=true, region=client.Network.CurrentSim.Name,
        login_scheme=loginUri.Scheme, seed_caps_scheme=seedUri.Scheme,
        seed_caps_host=seedUri.Host, seed_caps_port=seedUri.Port, logout_verified=true,
        expected_objects=names.Count, received_objects=prims.Length,
        names_verified=names.Order().ToArray(),
        objects=prims.Where(p=>names.Contains(p.Properties?.Name ?? "")).Select(p=>new {id=p.ID.ToString(),name=p.Properties.Name,position=p.Position.ToString(),scale=p.Scale.ToString()}).ToArray()
    };
    client.Network.Logout();
    deadline = DateTime.UtcNow.AddSeconds(5);
    while (client.Network.Connected && DateTime.UtcNow < deadline) Thread.Sleep(50);
    if (client.Network.Connected) throw new InvalidOperationException("Logout did not disconnect the client");
    Console.WriteLine("MUTSEA_SMOKE_RECEIPT "+JsonSerializer.Serialize(receipt));
    return 0;
} catch(Exception error) {
    Console.Error.WriteLine("MUTSEA_SMOKE_FAILED "+error.Message); return 1;
} finally { if(client.Network.Connected) client.Network.Logout(); }
