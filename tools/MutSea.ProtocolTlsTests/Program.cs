// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Net.Security;
using OpenMetaverse;

// Exercise the actual loaded SDK callbacks, including the combined error flags
// produced by a self-signed certificate for the wrong host. No user credentials.
var errors=new[]{SslPolicyErrors.None,SslPolicyErrors.RemoteCertificateNotAvailable,
    SslPolicyErrors.RemoteCertificateChainErrors,SslPolicyErrors.RemoteCertificateNameMismatch,
    SslPolicyErrors.RemoteCertificateChainErrors|SslPolicyErrors.RemoteCertificateNameMismatch};
int checkedCases=0;
foreach(var error in errors)
{
    bool expected=error==SslPolicyErrors.None;
    if(NetworkManager.ValidateServerCertificate(null!,null!,null!,error)!=expected)
        throw new Exception("Native login TLS callback accepted a certificate validation error.");
    if(OpenMetaverse.Http.CapsBase.ValidateServerCertificate(null!,null!,null!,error)!=expected)
        throw new Exception("Native capability TLS callback accepted a certificate validation error.");
    checkedCases+=2;
}
Console.WriteLine($"{checkedCases} actual protocol SDK TLS checks passed: untrusted chains, wrong hosts and missing certificates refused.");
