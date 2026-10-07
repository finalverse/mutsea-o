// Copyright (c) 2026 Finalverse contributors. BSD-3-Clause; see LICENSE.txt.
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MutSea.Framework.Servers.HttpServer;

// Exercise the actual server constructor without opening a network listener.
// The SAN intentionally differs from CN, reproducing the Linux deployment bug.
string scratch = Path.Combine(Path.GetTempPath(), "mutsea-tls-" + Guid.NewGuid());
Directory.CreateDirectory(scratch);
try
{
    using RSA key = RSA.Create(2048);
    var request = new CertificateRequest("CN=mutsea.example", key,
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName("grid.mutsea.example");
    san.AddDnsName("*.sandbox.mutsea.example");
    san.AddIpAddress(IPAddress.Parse("127.0.0.1"));
    san.AddIpAddress(IPAddress.Parse("::1"));
    request.CertificateExtensions.Add(san.Build());
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddDays(1));
    string file = Path.Combine(scratch, "test.p12");
    File.WriteAllBytes(file, certificate.Export(X509ContentType.Pkcs12, ""));
    var server = new BaseHttpServer(443, true, "grid.mutsea.example", file, "");
    var cases = new (string Host, bool Expected)[] {
        ("grid.mutsea.example", true), ("GRID.MUTSEA.EXAMPLE", true),
        ("one.sandbox.mutsea.example", true), ("127.0.0.1", true), ("::1", true),
        ("wrong.mutsea.example", false), ("mutsea.example", false),
        ("one.two.sandbox.mutsea.example", false), ("127.0.0.2", false),
        ("::2", false), ("", false), ("https://grid.mutsea.example", false)
    };
    foreach (var test in cases)
        if (server.CheckSSLCertHost(test.Host) != test.Expected)
            throw new Exception("Certificate host validation failed: " + test.Host);

    bool rejected = false;
    try { _ = new BaseHttpServer(443, true, "wrong.mutsea.example", file, ""); }
    catch (Exception) { rejected = true; }
    if (!rejected) throw new Exception("Server accepted a mismatched certificate");
    Console.WriteLine($"MUTSEA_TLS_SMOKE: {cases.Length} host checks and mismatched startup rejection passed");
}
finally { Directory.Delete(scratch, true); }
