// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Finalverse.World;

// The operator's private newcomer file: {"accounts":[{"first_name","last_name","password","genesis","genesis_sha256"}]}.
// Validation never echoes file content, so a password cannot reach a log through an error message.
public sealed record NewcomerAccount(string first_name,string last_name,string password,string? genesis=null,string? genesis_sha256=null,string? admission_id=null);
public sealed record NewcomerFile(NewcomerAccount[] accounts)
{
    static readonly Regex NamePart=new("^[A-Za-z][A-Za-z0-9]{0,30}$");
    public static NewcomerFile Parse(byte[] bytes)
    {
        ContentCatalog.Require(bytes.Length is >0 and <=65536,"Newcomer file is empty or oversized.");
        NewcomerFile? file;
        try{file=JsonSerializer.Deserialize<NewcomerFile>(bytes,ContentCatalog.Json);}
        // Never echo parser detail: it can quote file content, including passwords.
        catch(JsonException){throw new InvalidDataException("Newcomer file is not valid JSON.");}
        ContentCatalog.Require(file?.accounts is {Length:>0 and <=16},"Newcomer file needs 1–16 accounts.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var admissions=new HashSet<string>(StringComparer.Ordinal);
        foreach(var a in file!.accounts)
        {
            ContentCatalog.Require(a!=null&&a.first_name!=null&&a.last_name!=null&&NamePart.IsMatch(a.first_name)&&NamePart.IsMatch(a.last_name),"Newcomer names must be letters and digits, starting with a letter.");
            ContentCatalog.Require(names.Add(a!.first_name+" "+a.last_name),"Duplicate newcomer name.");
            ContentCatalog.Require(a.password is {Length:>=12 and <=128}&&!a.password.Any(char.IsControl),"Newcomer passwords must be 12–128 printable characters.");
            ContentCatalog.Require((a.genesis==null)==(a.genesis_sha256==null),"A Genesis choice needs both a compiled directory and its pin.");
            if(a.genesis!=null)ContentCatalog.Require(Path.IsPathRooted(a.genesis)&&a.genesis_sha256!.Length==64&&a.genesis_sha256.All(Uri.IsHexDigit),"Genesis choice must be an absolute compiled directory and a SHA-256 pin.");
            if(a.admission_id!=null)ContentCatalog.Require(Guid.TryParseExact(a.admission_id,"D",out var admission)&&admission!=Guid.Empty&&admission.ToString()==a.admission_id&&admissions.Add(a.admission_id)&&a.genesis!=null,"A pilot admission needs a unique canonical UUID and a pinned character choice.");
        }
        return file;
    }
}
