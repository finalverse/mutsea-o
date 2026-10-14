// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text.Json;

namespace Finalverse.World;

// An operator admission correlation is not a bearer token or a new identity
// store. Native account activation remains the authority. Replaying its own
// confirmed admission must never reset a password, wardrobe or changed home.
public static class PilotAdmission
{
    public static string Selection(NewcomerAccount account,string region,double[] home)
        =>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
            account.first_name,account.last_name,account.genesis,account.genesis_sha256,region,home}))).ToLowerInvariant();

    public static bool Confirmed(IEnumerable<string> records,string admission,string account,string selection)
    {
        bool confirmed=false;
        foreach(var line in records)
        {
            using var document=JsonDocument.Parse(line);var r=document.RootElement;
            if(r.TryGetProperty("kind",out var kind)&&kind.GetString()=="account_activated"
                &&r.TryGetProperty("admission_id",out var id)&&id.ValueKind==JsonValueKind.String&&id.GetString()==admission
                &&r.GetProperty("account_id").GetString()==account
                &&r.TryGetProperty("selection_sha256",out var pin)&&pin.GetString()==selection)confirmed=true;
        }
        return confirmed;
    }
}
