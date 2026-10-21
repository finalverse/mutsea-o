// Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nini.Config;
using OpenMetaverse;
namespace Finalverse.World;

public sealed record EarthBridgeEntry(string plan_id,string actor_id,string grant_id,string manifest_sha256,
    DateTimeOffset expires_at,string status,JsonElement manifest,JsonElement? receipt=null,string session_id="");

public sealed partial class WorldModule
{
    const long EarthJournalLimit=16*1024*1024;
    const int EarthRecordLimit=128*1024;
    HttpClient? earthBridge;
    string earthGrid="",earthJournal="";
    readonly Dictionary<string,EarthBridgeEntry> earthEntries=new();
    readonly Dictionary<string,(string plan,int binding)> earthBindings=new();
    readonly HashSet<UUID> earthBusy=new();
    void InitialiseEarthBridge(IConfigSource source)
    {
        var c=source.Configs["FinalverseEarthBridge"];if(c?.GetBoolean("Enabled",false)!=true)return;
        ContentCatalog.Require(Guid.TryParse(c.GetString("GridId",""),out var grid)&&grid!=Guid.Empty,"Explicit Earth bridge grid UUID required.");earthGrid=grid.ToString();
        string path=c.GetString("CredentialFile","");var info=new FileInfo(path);
        ContentCatalog.Require(info.Exists&&info.Length is >0 and <=1024&&info.LinkTarget==null,"Private Earth bridge credential file required.");
        if(!OperatingSystem.IsWindows())ContentCatalog.Require((File.GetUnixFileMode(path)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))==0,"Bridge credential must be private.");
        string token=File.ReadAllText(path).Trim();ContentCatalog.Require(token.Length==64&&token.All(Uri.IsHexDigit),"Invalid bridge credential.");
        string socket=c.GetString("SocketPath","");
        if(socket.Length>0)earthBridge=PrivateServiceHttp.Client(socket,TimeSpan.FromSeconds(8));
        else {
            // Explicit local Docker Desktop development transport only. Never a caller URL.
            string origin=c.GetString("LocalDevelopmentOrigin","");var uri=new Uri(origin);
            ContentCatalog.Require(uri.Scheme=="http"&&uri.AbsolutePath=="/"&&uri.Query==""&&uri.Fragment==""&&uri.UserInfo==""&&uri.Host is "127.0.0.1" or "host.docker.internal","Use a private socket or explicit local development origin.");
            earthBridge=new HttpClient(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false}){BaseAddress=uri,Timeout=TimeSpan.FromSeconds(8)};
        }
        earthBridge.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);earthBridge.DefaultRequestHeaders.Host="planner";
    }
    void StartEarthBridge()
    {
        // Disabling new handoffs must not erase provenance or reopen the generic
        // commit/export routes for experiences that already exist.
        earthJournal=Path.Combine(directory,RegionId+".fsip.jsonl");
        if(File.Exists(earthJournal)) {
            ContentCatalog.Require(new FileInfo(earthJournal).Length<=EarthJournalLimit,"FSIP audit quota reached.");
            foreach(var line in File.ReadLines(earthJournal)){var entry=JsonSerializer.Deserialize<EarthBridgeEntry>(line,Kernel.Json)!;earthEntries[entry.plan_id]=entry;}
            foreach(var entry in earthEntries.Values)IndexEarth(entry);
        }
    }
    void EarthAppend(EarthBridgeEntry entry)
    {
        ContentCatalog.Require(earthEntries.Count<256||earthEntries.ContainsKey(entry.plan_id),"FSIP region retention quota reached.");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(entry,Kernel.Json);
        ContentCatalog.Require(bytes.Length+1<=EarthRecordLimit,"FSIP audit record budget exceeded.");
        // Admission reserves the worst-case remaining records before any native
        // mutation. Filling the journal cannot consume an existing place's Undo
        // or an executing transaction's outcome capacity.
        long reserve=EarthReservedRecords(entry.status)*EarthRecordLimit;
        foreach(var retained in earthEntries.Values)
            if(retained.plan_id!=entry.plan_id)reserve+=EarthReservedRecords(retained.status)*EarthRecordLimit;
        long length=File.Exists(earthJournal)?new FileInfo(earthJournal).Length:0;
        ContentCatalog.Require(length+bytes.Length+1+reserve<=EarthJournalLimit,"FSIP audit quota reached; existing outcome and Undo capacity is reserved.");
        using(var file=new FileStream(earthJournal,FileMode.Append,FileAccess.Write,FileShare.Read)) {
            file.Write(bytes);file.WriteByte(10);file.Flush(true);
        }
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(earthJournal,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        if(earthEntries.TryGetValue(entry.plan_id,out var previous)&&previous.receipt is {} oldReceipt)
            foreach(var binding in oldReceipt.GetProperty("bindings").EnumerateArray())foreach(var id in binding.GetProperty("entity_ids").EnumerateArray())earthBindings.Remove(id.GetString()!);
        earthEntries[entry.plan_id]=entry;IndexEarth(entry);
    }
    static int EarthReservedRecords(string status)=>status switch {"prepared"=>3,"executing"=>2,"committed"=>1,_=>0};
    void IndexEarth(EarthBridgeEntry entry)
    {
        if(entry.status!="committed"||entry.receipt is not {} receipt)return;
        int index=0;foreach(var binding in receipt.GetProperty("bindings").EnumerateArray()) {
            foreach(var id in binding.GetProperty("entity_ids").EnumerateArray())earthBindings[id.GetString()!]=(entry.plan_id,index);
            index++;
        }
    }
    JsonElement EarthCall(string path,object body)
    {
        ContentCatalog.Require(earthBridge!=null,"Earth bridge is not enabled here.");
        var uri=earthBridge!.BaseAddress==null?new Uri("http://planner"+path):new Uri(earthBridge.BaseAddress,path);
        using var request=new HttpRequestMessage(HttpMethod.Post,uri){Content=new StringContent(JsonSerializer.Serialize(body),System.Text.Encoding.UTF8,"application/json")};
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var result=earthBridge.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).GetAwaiter().GetResult();
        ContentCatalog.Require(result.IsSuccessStatusCode,"Earth authorization refused or expired. Inspect the receipt before retrying.");
        using var stream=result.Content.ReadAsStream();using var memory=new MemoryStream();byte[] buffer=new byte[4096];int n;
        while((n=stream.ReadAsync(buffer,0,buffer.Length,deadline.Token).GetAwaiter().GetResult())>0){memory.Write(buffer,0,n);ContentCatalog.Require(memory.Length<=65536,"Bridge response budget exceeded.");}
        using var doc=JsonDocument.Parse(memory.ToArray(),new JsonDocumentOptions{MaxDepth=24});return doc.RootElement.Clone();
    }
    bool IsEarthPlan(string plan)=>earthEntries.ContainsKey(plan);
    object HumanEarthBridge(JsonElement data,UUID agent,Guid session)
    {
        string action=data.GetProperty("action").GetString()??"",actor=agent.ToString();
        void Session(){ContentCatalog.Require(HasWorldSession(agent,session),"Your region session is no longer active.");}
        lock(gate){Session();ContentCatalog.Require(composerPlaces!=null&&composerCatalog!=null,"Experience tools are not enabled here.");
            ContentCatalog.Require(action is "inspect" or "reject" or "undo"||earthBridge!=null,"New Earth handoffs are not enabled here.");
            ContentCatalog.Require(action is "inspect" or "reject"||!WorldRecoveryRequired,"An interrupted native world transaction requires recovery.");
            ContentCatalog.Require(earthBusy.Count<4&&earthBusy.Add(agent),"An Earth operation is already in progress.");}
        try {
            var audience=new{grid_id=earthGrid,region_id=RegionId};
            if(action=="preview") {
                string token=data.GetProperty("handoff").GetString()??"";ContentCatalog.Require(token.Length==64&&token.All(Uri.IsHexDigit),"Paste a valid single-use Earth handoff.");
                var grant=EarthCall("/v1/world/bridge/redeem",new{token,agent_id=actor,audience});
                var manifest=grant.GetProperty("manifest");string hash=grant.GetProperty("manifest_sha256").GetString()!;
                ContentCatalog.Require(ContentCatalog.Hash(System.Text.Encoding.UTF8.GetBytes(manifest.GetRawText()))==hash,"Manifest integrity mismatch.");
                var draft=EarthBridgeContract.Draft(manifest,earthGrid,RegionId,composerCatalog!);
                lock(gate) {
                    Session();ContentCatalog.Require(!WorldRecoveryRequired,"World recovery required.");
                    var p=JsonSerializer.SerializeToElement(new{draft,site="in_front_of_actor"});
                    var proposal=new Plan(1,"Build reviewed FSIP experience","finalverse-earth","fsip-0.1",[new ActionSpec("compose_place",null,p)]);
                    var preview=JsonSerializer.SerializeToNode(ComposePrepare(proposal,actor))!.AsObject();string plan=preview["plan_id"]!.GetValue<string>();
                    var expires=DateTimeOffset.Parse(grant.GetProperty("expires_at").GetString()!);
                    if(expires>composerPending[plan].expires)expires=composerPending[plan].expires;
                    var preparedEntry=new EarthBridgeEntry(plan,actor,grant.GetProperty("grant_id").GetString()!,hash,expires,"prepared",manifest.Clone(),session_id:session.ToString());
                    try{EarthAppend(preparedEntry);}catch{composerPending.Remove(plan);throw;}
                    preview["fsip"]=EarthView(preparedEntry);return preview;
                }
            }
            string planId=data.GetProperty("plan_id").GetString()??"";EarthBridgeEntry entry;
            lock(gate){Session();ContentCatalog.Require(earthEntries.TryGetValue(planId,out entry!)&&entry.actor_id==actor,"No Earth experience owned by you.");}
            if(action=="commit") {
                lock(gate){Session();ContentCatalog.Require(entry.status=="prepared"&&entry.expires_at>DateTimeOffset.UtcNow&&entry.session_id==session.ToString(),"Preview is missing, expired, consumed or belongs to an earlier login.");EarthAppend(entry with{status="executing"});}
                // Never hold the world gate across authorization I/O. A lost reply
                // consumes the grant; reconciling cannot replay native creation.
                EarthCall("/v1/world/bridge/authorize",new{grant_id=entry.grant_id,agent_id=actor,audience,plan_id=planId,manifest_sha256=entry.manifest_sha256});
                lock(gate){Session();ContentCatalog.Require(entry.expires_at>DateTimeOffset.UtcNow&&!WorldRecoveryRequired,"Approval expired or world recovery required.");ComposeCommit(planId,actor);
                    entry=EarthOutcome(entry,"committed");EarthAppend(entry);}
            }
            else if(action=="reject") {
                lock(gate){Session();ContentCatalog.Require(entry.status=="prepared","Only a pending preview can be rejected.");composerPending.Remove(planId);entry=EarthRejected(entry);EarthAppend(entry);}
            }
            else if(action=="undo") {
                lock(gate){Session();ContentCatalog.Require(entry.status=="committed","No committed Earth experience to undo.");ComposeUndo(planId,actor);entry=EarthOutcome(entry,"undone");EarthAppend(entry);}
            }
            else if(action!="inspect")throw new InvalidDataException("Unsupported Earth bridge operation.");
            if(action=="inspect"&&entry.status=="committed")lock(gate) {
                if(composerPlaces!.Entries.Any(e=>e.plan.plan_id==planId&&e.status=="rolled_back")) {entry=EarthOutcome(entry,"undone");EarthAppend(entry);}
            }
            // Manual inspection reconciles only a proven native committed outcome.
            // Unknown or partially compensated native writes remain blocked.
            if(action=="inspect"&&!WorldRecoveryRequired&&(entry.status is "prepared" or "executing"))lock(gate) {
                var native=composerPlaces!.Entries.LastOrDefault(e=>e.plan.plan_id==planId&&e.status=="committed");
                if(native!=null&&!composerPlaces.Entries.Any(e=>e.plan.plan_id==planId&&e.status=="rolled_back")) {
                    ContentCatalog.Require(native.fingerprints!=null&&native.plan.placements.Select((p,i)=>((IContentWorld)this).MatchesFingerprint(p,native.fingerprints[i])).All(x=>x),"Native experience changed; administrator reconciliation required.");
                    entry=EarthOutcome(entry,"committed");EarthAppend(entry);
                }
                else if(native==null&&(entry.status=="executing"||!composerPending.ContainsKey(planId)||entry.session_id!=session.ToString()||entry.expires_at<=DateTimeOffset.UtcNow)) {
                    var outcomes=composerPlaces.Entries.Where(e=>e.plan.plan_id==planId).ToArray();
                    if(outcomes.Length==0||outcomes.Last().status=="compensated") {
                        composerPending.Remove(planId);entry=EarthRejected(entry);EarthAppend(entry);
                    }
                }
            }
            bool synced=false;
            if(earthBridge!=null&&entry.receipt is {} receipt)try{EarthCall("/v1/world/bridge/"+entry.grant_id+"/receipt",receipt);synced=true;}catch(Exception ex) when(ex is InvalidDataException or HttpRequestException or TaskCanceledException or IOException){}
            return new{status=entry.status,plan_id=planId,fsip=EarthView(entry),receipt_sync=synced?"recorded":"pending",receipt=entry.receipt};
        }
        finally{lock(gate)earthBusy.Remove(agent);}
    }
    EarthBridgeEntry EarthRejected(EarthBridgeEntry e)
    {
        var receipt=JsonSerializer.SerializeToElement(new{protocol_version="0.1",receipt_id=Guid.NewGuid(),execution_id=e.grant_id,manifest_sha256=e.manifest_sha256,
            owner=new{earth_subject=e.manifest.GetProperty("owner").GetProperty("earth_subject").GetString(),mutsea_agent_id=e.actor_id},audience=e.manifest.GetProperty("transform").GetProperty("target"),
            status="rejected",worldline_operation_id=(string?)null,bindings=Array.Empty<object>(),timestamp=DateTimeOffset.UtcNow});
        return e with{status="rejected",receipt=receipt};
    }
    object? EarthSemantic(string id,string actor)
    {
        if(!earthBindings.TryGetValue(id,out var reference)||!earthEntries.TryGetValue(reference.plan,out var e)||e.actor_id!=actor||e.status!="committed"||e.receipt is not {} receipt||composerPlaces?.Semantic(id,actor) is not {} original)return null;
        var binding=receipt.GetProperty("bindings")[reference.binding];
        var result=JsonSerializer.SerializeToNode(original)!.AsObject();result["earth_experience"]=JsonSerializer.SerializeToNode(new{experience_id=e.manifest.GetProperty("experience_id"),e.manifest_sha256,
            source_workspace=e.manifest.GetProperty("source_workspace"),source_record_ids=binding.GetProperty("source_record_ids"),generated_geometry=true,layout=e.manifest.GetProperty("transform").GetProperty("layout")});return result;
    }
    object CitizenEarthContext(string plan)
    {
        ContentCatalog.Require(Guid.TryParse(plan,out _)&&earthEntries.TryGetValue(plan,out var e)&&e.actor_id==citizenOwner,"No experience authorized by Lumi's controller.");
        var entry=earthEntries[plan];var m=entry.manifest;
        return new{protocol_version="0.1",experience_id=m.GetProperty("experience_id"),title=m.GetProperty("title"),layout=m.GetProperty("transform").GetProperty("layout"),
            description=m.GetProperty("transform").GetProperty("description"),status=entry.status,approval_required=entry.status=="prepared",
            exhibits=m.GetProperty("objects").EnumerateArray().Where(o=>o.GetProperty("source_record_ids").GetArrayLength()>0).Take(6).Select(o=>new{description=o.GetProperty("description"),source_record_ids=o.GetProperty("source_record_ids")}).ToArray(),
            provenance=m.GetProperty("provenance").EnumerateArray().Select(p=>new{attribution=p.GetProperty("attribution"),license=p.GetProperty("license"),generated=p.GetProperty("generated")}).ToArray(),
            worldline_operation_id=entry.receipt is {} receipt?receipt.GetProperty("worldline_operation_id"):(object?)null};
    }
    EarthBridgeEntry EarthOutcome(EarthBridgeEntry e,string status)
    {
        var native=composerPlaces!.Entries.Last(x=>x.plan.plan_id==e.plan_id&&x.status==(status=="undone"?"rolled_back":"committed"));
        var objects=e.manifest.GetProperty("objects").EnumerateArray().ToArray();
        var owner=new{earth_subject=e.manifest.GetProperty("owner").GetProperty("earth_subject").GetString(),mutsea_agent_id=e.actor_id};
        var audience=e.manifest.GetProperty("transform").GetProperty("target");string experience=e.manifest.GetProperty("experience_id").GetString()!;
        var bindings=native.plan.placements.Select((p,i)=>new{protocol_version="0.1",binding_id=Guid.NewGuid(),experience_id=experience,manifest_sha256=e.manifest_sha256,owner,audience,
            local_id=objects[i].GetProperty("local_id").GetString(),entity_ids=p.entity_ids,source_record_ids=objects[i].GetProperty("source_record_ids")}).ToArray();
        var receipt=JsonSerializer.SerializeToElement(new{protocol_version="0.1",receipt_id=Guid.NewGuid(),execution_id=e.grant_id,manifest_sha256=e.manifest_sha256,owner,audience,status,worldline_operation_id=native.operation_id,bindings,timestamp=DateTimeOffset.UtcNow});
        return e with{status=status,receipt=receipt};
    }
    JsonNode EarthView(EarthBridgeEntry e)=>JsonSerializer.SerializeToNode(new{
        protocol_version="0.1",title=e.manifest.GetProperty("title"),layout=e.manifest.GetProperty("transform").GetProperty("layout"),
        context=e.manifest.GetProperty("transform"),source_workspace=e.manifest.GetProperty("source_workspace"),provenance=e.manifest.GetProperty("provenance"),
        narrative=e.manifest.GetProperty("ai_narrative"),permissions=e.manifest.GetProperty("required_permissions"),budget=e.manifest.GetProperty("budget"),
        manifest_sha256=e.manifest_sha256,grant_id=e.grant_id,status=e.status,expires_at=e.expires_at,
        exhibits=e.manifest.GetProperty("objects").EnumerateArray().Where(o=>o.GetProperty("source_record_ids").GetArrayLength()>0).Select(o=>new{local_id=o.GetProperty("local_id"),description=o.GetProperty("description"),source_record_ids=o.GetProperty("source_record_ids")}).ToArray(),
        destination=new{region_id=RegionId,region_name=RegionName,position=EarthArrival(e.plan_id)}
    })!;
    double[]? EarthArrival(string planId)
    {
        var plan=composerPlaces?.Entries.LastOrDefault(e=>e.plan.plan_id==planId&&e.status=="committed")?.plan;
        if(plan==null)return null;var p=plan.place.entrances[0].position;double yaw=plan.yaw_degrees*Math.PI/180;
        return [plan.anchor[0]+p[0]*Math.Cos(yaw)-p[1]*Math.Sin(yaw),plan.anchor[1]+p[0]*Math.Sin(yaw)+p[1]*Math.Cos(yaw),plan.anchor[2]+p[2]];
    }
}
