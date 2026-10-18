// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Finalverse.World;

public sealed record LibraryArchive(string id,string sha256,string report_sha256,string source_sha256,int expected_parts);
public sealed record WorkspaceZone(string id,string title,string purpose,double[] center,double radius);
public sealed record LibraryDestination(string id,string title,string description,string region_id,double[] spawn,
    string license,string attribution,string source,string remix_policy,string? template=null,WorkspaceZone[]? workspace=null,LibraryArchive? archive=null);
public sealed record LibraryManifest(int schema_version,LibraryDestination[] destinations);

// Operator catalog is passive metadata, never an upload/import/remote-fetch API.
// Grid availability and native parcel/object permissions remain authoritative.
public sealed class WorldLibrary
{
    public LibraryDestination[] Destinations {get;}
    static readonly JsonSerializerOptions Json=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12};
    public WorldLibrary(byte[] bytes)
    {
        Require(bytes.Length is >0 and <=65536,"World Library manifest size limit.");
        var manifest=JsonSerializer.Deserialize<LibraryManifest>(bytes,Json);
        Require(manifest is {schema_version:1,destinations.Length:<=64},"Unsupported World Library manifest.");
        Destinations=manifest!.destinations;
        var regions=new HashSet<string>();var ids=new HashSet<string>();
        foreach(var d in Destinations)
        {
            Require(d!=null&&Id(d.id)&&ids.Add(d.id)&&Text(d.title,80)&&Text(d.description,800),"Invalid destination identity.");
            Require(CanonicalRegion(d!.region_id)&&regions.Add(d.region_id),"Duplicate or invalid region UUID.");
            Require(d.spawn is {Length:3}&&d.spawn.All(double.IsFinite)&&d.spawn[0] is >=1 and <=255&&d.spawn[1] is >=1 and <=255&&d.spawn[2] is >=1 and <=4096,"Invalid destination spawn.");
            Require(d.license is "CC0-1.0" or "CC-BY-4.0" or "LicenseRef-Reviewed-Mixed"&&Text(d.attribution,500)&&Text(d.source,500),"Destination needs reviewed rights and attribution.");
            Require(d.remix_policy is "copy-under-license" or "visit-only","Invalid remix policy.");
            Require(d.template==null||Regex.IsMatch(d.template,@"^[a-z0-9][a-z0-9.-]{1,119}@[0-9]+\.[0-9]+\.[0-9]+$"),"Invalid pinned world template.");
            Require(d.archive==null||d.template==null&&d.license=="CC0-1.0"&&Id(d.archive.id)&&d.archive.expected_parts is >0 and <=10000&&new[]{d.archive.sha256,d.archive.report_sha256,d.archive.source_sha256}.All(h=>h!=null&&Regex.IsMatch(h,"^[a-f0-9]{64}$")),"Invalid passive archive admission.");
            Require(d.workspace==null||d.workspace.Length is >0 and <=8,"Workspace zone limit.");
            var zones=new HashSet<string>();
            foreach(var z in d.workspace??[])
                Require(z!=null&&Id(z.id)&&zones.Add(z.id)&&Text(z.title,80)&&Text(z.purpose,500)&&z.center is {Length:2}&&z.center.All(v=>double.IsFinite(v)&&v is >=1 and <=255)&&double.IsFinite(z.radius)&&z.radius is >=2 and <=32,"Invalid workspace zone.");
        }
    }
    public LibraryDestination? Find(string region)=>Destinations.SingleOrDefault(d=>d.region_id==region);
    public static bool CanonicalRegion(string id)=>Guid.TryParseExact(id,"D",out var g)&&g!=Guid.Empty&&g.ToString()==id;
    public static bool Hosts(string configured,string current)=>configured==""||CanonicalRegion(configured)&&configured==current;
    static bool Id(string? id)=>id!=null&&Regex.IsMatch(id,@"^[a-z][a-z0-9.-]{1,95}$");
    static bool Text(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&!value.Any(char.IsControl);
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
}
