// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Read-only, loopback-only observation from a second real protocol client.
using System.Security.Cryptography;
using System.Text.Json;
using OpenMetaverse;

if (args.Length != 3) { Console.Error.WriteLine("Usage: AppearanceSmoke private-observer-account.json private-target-account.json receipt.json"); return 2; }
var observer = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var target = JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;
var grid = new Uri(observer.GetProperty("grid_url").GetString()!);
if (!grid.IsLoopback || grid != new Uri(target.GetProperty("grid_url").GetString()!))
    throw new InvalidDataException("This probe is limited to one loopback development grid.");
UUID targetId = new(target.GetProperty("user_id").GetString());
if (targetId == new UUID(observer.GetProperty("user_id").GetString()))
    throw new InvalidDataException("Observer must be a different account.");
var client = new GridClient();
client.Settings.LOGIN_SERVER = grid.AbsoluteUri;
client.Settings.SEND_AGENT_APPEARANCE = false;
client.Settings.SEND_AGENT_UPDATES = true;
client.Settings.ALWAYS_DECODE_OBJECTS = true;
client.Settings.OBJECT_TRACKING = true;
client.Settings.MULTIPLE_SIMS = false;
var appearance = new TaskCompletionSource<AvatarAppearanceEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
client.Avatars.AvatarAppearance += (_, data) => { if (data.AvatarID == targetId) appearance.TrySetResult(data); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
try
{
    if (!client.Network.Login(observer.GetProperty("first_name").GetString(), observer.GetProperty("last_name").GetString(),
        observer.GetProperty("password").GetString(), "MutSea.AppearanceSmoke", "home", "0.1"))
        throw new InvalidDataException("Observer login failed: " + client.Network.LoginMessage);
    var data = await appearance.Task.WaitAsync(TimeSpan.FromSeconds(30));
    // The SDK has already negotiated seed capabilities. Reuse that real
    // result: its SeedCapsURI property need not retain a reusable endpoint.
    var textureCap = client.Network.CurrentSim.Caps.CapabilityURI("GetTexture")
        ?? throw new InvalidDataException("Texture capability was not negotiated.");
    if (!textureCap.IsLoopback || textureCap.Port != grid.Port) throw new InvalidDataException("Unexpected texture capability origin.");
    var textures = new List<object>();
    foreach (int index in new[] { 8, 9, 10 })
    {
        var face = data.FaceTextures.Length > index ? data.FaceTextures[index] : null;
        if (face is null || face.TextureID == UUID.Zero || face.TextureID == new UUID("c228d1cf-4b5d-4ba8-84f4-899a0796aa97"))
            throw new InvalidDataException("Required head/upper/lower bake missing.");
        var url = new UriBuilder(textureCap) { Query = "texture_id=" + face.TextureID };
        var bytes = await http.GetByteArrayAsync(url.Uri);
        // Raw JPEG2000 codestream SOC, SIZ; SIZ encodes component count after
        // its fixed image/tile fields. No fabricated image or local cache.
        if (bytes.Length < 42 || bytes[0] != 0xff || bytes[1] != 0x4f || bytes[2] != 0xff || bytes[3] != 0x51)
            throw new InvalidDataException("Bake is not a JPEG2000 codestream.");
        int channels = (bytes[40] << 8) | bytes[41];
        if (channels != 5) throw new InvalidDataException("Bake lacks RGBHM channels.");
        textures.Add(new { texture_index = index, bytes = bytes.Length, channels,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
    }
    var receipt = new { observed_utc = DateTime.UtcNow, second_client_udp_appearance = true,
        texture_capability_downloads = true, visual_param_count = data.VisualParams.Count, textures };
    File.WriteAllText(args[2], JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(args[2], UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.WriteLine("APPEARANCE_SMOKE PASS: second-client UDP appearance and three real RGBHM texture downloads.");
    return 0;
}
catch (Exception error)
{
    // Capability URLs are bearer credentials; exception messages may contain
    // them. Keep diagnostic classification without emitting those messages.
    var diagnostic = args[2] + ".diagnostic";
    File.WriteAllText(diagnostic, error.ToString());
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(diagnostic, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.Error.WriteLine("APPEARANCE_SMOKE FAIL: " + error.GetType().Name + " (private diagnostic saved)");
    return 1;
}
finally { if (client.Network.Connected) client.Network.Logout(); }
