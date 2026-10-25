// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Private isolated-grid acceptance probe for geographic world binding: two real accounts, real
// login, real UDP, real World capabilities. Earth-style discovery picks where each user enters.
//   GeoProbe <probe.json> session|restart
// probe.json: {"ada":..., "ben":..., "discovery":"http://127.0.0.1:19070", "terrain":"...r32", "evidence":"dir"}
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

if(args.Length!=2||args[1] is not ("session" or "restart")){Console.Error.WriteLine("Usage: GeoProbe probe.json session|restart");return 2;}
var cfg=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;string mode=args[1];
string evidence=cfg.GetProperty("evidence").GetString()!;Directory.CreateDirectory(evidence);
var r32=File.ReadAllBytes(cfg.GetProperty("terrain").GetString()!);int S=(int)Math.Sqrt(r32.Length/4);
var checks=new List<object>();int failed=0;
void Check(string name,bool ok,object? detail=null){checks.Add(new{name,ok,detail});if(!ok)failed++;Console.WriteLine((ok?"PASS ":"FAIL ")+name+(detail==null?"":" "+JsonSerializer.Serialize(detail)));}
using var web=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
async Task<JsonElement> Get(string path){var body=await web.GetStringAsync(cfg.GetProperty("discovery").GetString()+path);return JsonDocument.Parse(body).RootElement.Clone();}
double Terrain(double x,double y){int i=Math.Min((int)x,S-2),j=Math.Min((int)y,S-2);double u=x-i,v=y-j;float H(int a,int b)=>BitConverter.ToSingle(r32,(b*S+a)*4);return H(i,j)*(1-u)*(1-v)+H(i+1,j)*u*(1-v)+H(i,j+1)*(1-u)*v+H(i+1,j+1)*u*v;}
var latencies=new Dictionary<string,List<double>>();
string receiptPath=Path.Combine(evidence,"probe-session-receipt.json");

try
{
    // ---- 1. Earth-style discovery: no identity, no capability, only where a world is.
    var worlds=await Get("/finalverse/geo/v1/worlds");var world=worlds.GetProperty("worlds")[0];
    string version=world.GetProperty("binding_version").GetString()!;
    Check("public discovery names one bound region and its immutable version",worlds.GetProperty("worlds").GetArrayLength()==1&&version.Length==64,new{version,region=world.GetProperty("region_name").GetString()});
    if(mode=="session")
    {
        var saved=new JsonObject{["binding_version"]=version};
        // Ada enters beside a real published building; Ben at the region's own entry.
        string building=cfg.GetProperty("building").GetString()!;
        var adaEntry=await Get("/finalverse/geo/v1/entry?spatialdb_entity="+building);
        var benAnchor=world.GetProperty("world_entry").GetProperty("anchor");
        var benEntry=await Get(FormattableString.Invariant($"/finalverse/geo/v1/entry?latitude={benAnchor.GetProperty("latitude_degrees").GetDouble():R}&longitude={benAnchor.GetProperty("longitude_degrees").GetDouble():R}"));
        Check("discovery resolves a building to a standing place on a real street",adaEntry.GetProperty("target").GetProperty("on").EnumerateArray().Any(o=>o.GetProperty("kind").GetString()=="road"),
            new{building=adaEntry.GetProperty("target").GetProperty("feature").GetProperty("label").GetString(),on=adaEntry.GetProperty("target").GetProperty("on").EnumerateArray().Select(o=>o.GetProperty("label").GetString()).ToArray()});

        // ---- 2. Two authenticated users log in concurrently at the discovered places.
        var t0=Stopwatch.StartNew();
        var logins=await Task.WhenAll(Session.Login(cfg.GetProperty("ada"),adaEntry.GetProperty("target").GetProperty("login").GetProperty("start").GetString()!,"Ada"),
                                      Session.Login(cfg.GetProperty("ben"),benEntry.GetProperty("target").GetProperty("login").GetProperty("start").GetString()!,"Ben"));
        var ada=logins[0];var ben=logins[1];
        Check("two independent accounts authenticate concurrently over native login",ada.Agent!=ben.Agent&&ada.Agent!=UUID.Zero&&ben.Agent!=UUID.Zero,new{ada=ada.Agent.ToString(),ben=ben.Agent.ToString(),ms=t0.ElapsedMilliseconds});
        saved["ada_agent"]=ada.Agent.ToString();saved["ben_agent"]=ben.Agent.ToString();

        // ---- 3. Authorization-preserving handoff: the native session alone confers authority.
        foreach(var (s,entry,body) in new[]{(ada,adaEntry,(object)new{op="geo_enter",binding_version=version,spatialdb_entity=building}),
            (ben,benEntry,new{op="geo_enter",binding_version=version,latitude=benAnchor.GetProperty("latitude_degrees").GetDouble(),longitude=benAnchor.GetProperty("longitude_degrees").GetDouble()})})
        {
            var receipt=(await s.World(body,latencies)).GetProperty("receipt");
            Check(s.Name+" arrives where discovery pointed, with a journaled receipt",receipt.GetProperty("status").GetString()=="arrived"&&receipt.GetProperty("actor_id").GetString()==s.Agent.ToString(),
                new{error_m=Math.Round(receipt.GetProperty("horizontal_error_metres").GetDouble(),3),arrival=receipt.GetProperty("arrival").GetProperty("geographic")});
        }
        var stale=await ada.TryWorld(new{op="geo_enter",binding_version=new string('0',64),spatialdb_entity=building});
        Check("an entry for another binding version is refused",!stale.ok&&stale.error!.Contains("changed"),stale.error);
        var binding=await ada.World(new{op="geo_binding"},latencies);
        Check("the session sees the same binding discovery served, with terrain matching issuance",binding.GetProperty("binding").GetProperty("version").GetString()==version&&binding.GetProperty("terrain_matches_issued").GetBoolean()
            &&JsonSerializer.Serialize(binding.GetProperty("earth").GetProperty("world_binding"))==JsonSerializer.Serialize(world.GetProperty("world_binding")));

        // ---- 4. Live ground is the issued, publication-derived ground.
        var rng=new Random(5);double worst=0;var coverage=world.GetProperty("coverage");
        double lat0=coverage[0].GetProperty("latitude_degrees").GetDouble(),lon0=coverage[0].GetProperty("longitude_degrees").GetDouble();
        double lat2=coverage[2].GetProperty("latitude_degrees").GetDouble(),lon2=coverage[2].GetProperty("longitude_degrees").GetDouble();
        int inside=0;
        for(int i=0;i<40;i++)
        {
            double lat=lat0+(lat2-lat0)*rng.NextDouble(),lon=lon0+(lon2-lon0)*rng.NextDouble();
            var r=await ben.World(new{op="geo_resolve",latitude=lat,longitude=lon},latencies);if(!r.GetProperty("inside").GetBoolean())continue;inside++;
            var l=r.GetProperty("region_local");worst=Math.Max(worst,Math.Abs(l[2].GetDouble()-Terrain(l[0].GetDouble(),l[1].GetDouble())));
        }
        Check("live MutSea ground equals the issued heightmap at random geographic points",inside>=20&&worst<1e-4,new{points=inside,max_difference_m=worst});

        // ---- 5. Concurrent creation through WorldKernel, observed by both users over UDP.
        var feature=await ada.World(new{op="geo_entity",entity_id=building},latencies);
        var centroid=feature.GetProperty("region_centroid");double cx=centroid[0].GetDouble(),cy=centroid[1].GetDouble();
        var adaAt=await ada.World(new{op="geo_locate"},latencies);var benAt=await ben.World(new{op="geo_locate"},latencies);
        double[] P(JsonElement e)=>e.GetProperty("region_local").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
        var ap=P(adaAt);var bp=P(benAt);
        // An Earth client knows the published scene: put the post inside the road triangles beside Ada.
        string road=adaEntry.GetProperty("target").GetProperty("on").EnumerateArray().First(o=>o.GetProperty("kind").GetString()=="road").GetProperty("entity_id").GetString()!;
        var footprint=JsonDocument.Parse(File.ReadAllBytes(cfg.GetProperty("correspondence").GetString()!)).RootElement.GetProperty("features").EnumerateArray()
            .First(f=>f.GetProperty("entity_id").GetString()==road).GetProperty("footprint").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        var onRoad=Enumerable.Range(0,footprint.Length/6).Select(t=>new[]{(footprint[6*t]+footprint[6*t+2]+footprint[6*t+4])/3,(footprint[6*t+1]+footprint[6*t+3]+footprint[6*t+5])/3})
            .Select(c=>(c,d:Math.Sqrt((c[0]-ap[0])*(c[0]-ap[0])+(c[1]-ap[1])*(c[1]-ap[1])))).Where(x=>x.d>=2.5&&x.d<=30).MinBy(x=>x.d).c;
        double[] markerPos=[cx,cy,Terrain(cx,cy)+.5],streetPos=[onRoad[0],onRoad[1],Terrain(onRoad[0],onRoad[1])+.5],benPos=[bp[0]+2,bp[1]+1,Terrain(bp[0]+2,bp[1]+1)+.5];
        // Concurrent across users; each user's own requests stay serial (MutSea throttles per user).
        async Task<(string id,string plan,string status)[]> AdaBuilds(){var label=feature.GetProperty("feature").GetProperty("label").GetString()!;
            return [await ada.Create("Marker inside "+label[..Math.Min(40,label.Length)],markerPos,"box",latencies),await ada.Create("Street post",streetPos,"box",latencies)];}
        async Task<(string id,string plan,string status)[]> BenBuilds()=>[await ben.Create("Ben's lantern",benPos,"sphere",latencies)];
        var both=await Task.WhenAll(AdaBuilds(),BenBuilds());var created=both[0].Concat(both[1]).ToArray();
        string marker=created[0].id,post=created[1].id,lantern=created[2].id;
        Check("three objects committed concurrently by two users through WorldLine",created.All(c=>c.status=="committed"),new{objects=created.Select(c=>c.id).ToArray(),replanned_after_conflict=ada.Conflicts+ben.Conflicts});
        // Deterministic optimistic concurrency: a plan prepared before another user's commit is stale.
        var early=await ada.World(new{op="prepare",plan=Session.CreatePlan("Concurrency probe",[ap[0]-2,ap[1],Terrain(ap[0]-2,ap[1])+.5],"box")},latencies);
        double[] secondPos=[bp[0]-2,bp[1]+1,Terrain(bp[0]-2,bp[1]+1)+.5];
        var second=await ben.Create("Ben's second lantern",secondPos,"sphere",latencies);
        var staleCommit=await ada.TryWorld(new{op="commit",plan_id=early.GetProperty("plan_id").GetString()});
        Check("a plan prepared before another user's commit is refused as stale (no lost update)",!staleCommit.ok&&staleCommit.error!.Contains("Scene changed"),staleCommit.error);
        Check("each user observes the other's newest object over UDP",await ada.Observe(second.id,secondPos)&&await ben.Observe(created[0].id,markerPos));
        foreach(var s in new[]{ada,ben})foreach(var (id,pos) in new[]{(marker,markerPos),(post,streetPos),(lantern,benPos)})
            Check(s.Name+" observes "+id[..8]+" at its committed transform over UDP",await s.Observe(id,pos),null);
        saved["marker"]=marker;saved["post"]=post;saved["lantern"]=lantern;
        saved["plans"]=new JsonArray(created.Select(c=>(JsonNode)c.plan).ToArray());

        // ---- 6. Entity correspondence in both directions.
        var m=await ada.World(new{op="geo_entity",entity_id=marker},latencies);
        Check("a live object reports the published building it stands in",m.GetProperty("authority").GetString()=="mutsea"&&m.GetProperty("on").EnumerateArray().Any(o=>o.GetProperty("entity_id").GetString()==building),
            new{geographic=m.GetProperty("geographic"),on=m.GetProperty("on").EnumerateArray().Select(o=>o.GetProperty("label").GetString()).ToArray()});
        var bf=await ben.World(new{op="geo_entity",entity_id=building},latencies);
        Check("the published building lists the live object standing in it",bf.GetProperty("authority").GetString()=="spatialdb"&&bf.GetProperty("live_objects_on").EnumerateArray().Any(o=>o.GetProperty("id").GetString()==marker));
        var p=await ben.World(new{op="geo_entity",entity_id=post},latencies);
        Check("the street post resolves onto the published street it was placed on",p.GetProperty("on").EnumerateArray().Any(o=>o.GetProperty("entity_id").GetString()==road),p.GetProperty("on").EnumerateArray().Select(o=>o.GetProperty("label").GetString()).ToArray());
        saved["marker_geographic"]=JsonNode.Parse(m.GetProperty("geographic").GetRawText());
        saved["post_geographic"]=JsonNode.Parse(p.GetProperty("geographic").GetRawText());

        // ---- 7. No unauthorized mutation: World API and native protocol.
        string Fail((bool ok,string? error,JsonElement? result) r)=>r.ok?"ACCEPTED":r.error!;
        var del=await ben.TryWorld(new{op="prepare",plan=Plan("delete_entity",post,new{})});
        var mov=await ben.TryWorld(new{op="prepare",plan=Plan("move_entity",post,new{position=new[]{streetPos[0]+3,streetPos[1],streetPos[2]}})});
        var adaPlan=await ada.World(new{op="prepare",plan=Plan("set_name",post,new{name="Ada's street post"})},latencies);
        var steal=await ben.TryWorld(new{op="commit",plan_id=adaPlan.GetProperty("plan_id").GetString()});
        var undo=await ben.TryWorld(new{op="undo",plan_id=created[1].plan});
        var forged=await ben.TryWorld(new{op="geo_issue",binding_version=version});
        Check("Ben cannot delete Ada's object through the World API",!del.ok,Fail(del));
        Check("Ben cannot move Ada's object through the World API",!mov.ok,Fail(mov));
        Check("Ben cannot commit Ada's prepared plan",!steal.ok,Fail(steal));
        Check("Ben cannot undo Ada's committed plan",!undo.ok,Fail(undo));
        Check("no client operation can issue or change a binding",!forged.ok,Fail(forged));
        var beforeNative=ben.Prim(post);
        Check("Ben sees Ada's object natively before the attack",beforeNative!=null);
        ben.Client.Objects.SetPosition(ben.Client.Network.CurrentSim,beforeNative!.LocalID,new Vector3((float)streetPos[0]+5,(float)streetPos[1],(float)streetPos[2]));
        ben.Client.Inventory.RequestDeRezToInventory(beforeNative.LocalID);
        await Task.Delay(3000);
        var still=await ada.World(new{op="geo_locate",entity_id=post},latencies);
        Check("native UDP move and take-to-inventory by a non-owner change nothing",await ada.Observe(post,streetPos)&&await ben.Observe(post,streetPos)&&GeoDistance(P(still),streetPos)<.02,new{post=P(still)});
        await ada.World(new{op="commit",plan_id=adaPlan.GetProperty("plan_id").GetString()},latencies);

        // ---- 8. WorldLine evidence.
        var adaJournal=await ada.World(new{op="journal"},latencies);var benJournal=await ben.World(new{op="journal"},latencies);
        var adaPlans=adaJournal.EnumerateArray().Where(e=>e.GetProperty("status").GetString()=="committed").Select(e=>e.GetProperty("plan_id").GetString()).ToHashSet();
        Check("WorldLine records each owner's commits and none of the refused attempts",adaPlans.Contains(created[0].plan)&&adaPlans.Contains(created[1].plan)&&adaPlans.Contains(adaPlan.GetProperty("plan_id").GetString())
            &&benJournal.EnumerateArray().All(e=>e.GetProperty("actor_id").GetString()==ben.Agent.ToString())&&benJournal.EnumerateArray().Count(e=>e.GetProperty("status").GetString()=="committed")==2
            &&!adaJournal.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==early.GetProperty("plan_id").GetString()),
            new{ada_entries=adaJournal.GetArrayLength(),ben_entries=benJournal.GetArrayLength()});
        File.WriteAllText(Path.Combine(evidence,"worldline-ada.json"),adaJournal.GetRawText());File.WriteAllText(Path.Combine(evidence,"worldline-ben.json"),benJournal.GetRawText());

        // ---- 9. Reconnection without identity loss; the old capability dies with its session.
        string oldCap=ada.Cap;var adaAgent=ada.Agent;await ada.Logout();
        var dead=await Session.Raw(oldCap,new{op="geo_locate"});
        Check("Ada's previous World capability is refused after logout",!dead.ok,dead.error);
        var again=await Session.Login(cfg.GetProperty("ada"),adaEntry.GetProperty("target").GetProperty("login").GetProperty("start").GetString()!,"Ada");
        Check("Ada reconnects with the same identity and a new capability",again.Agent==adaAgent&&again.Cap!=oldCap);
        Check("Ada still sees her objects with their identities and owner after reconnecting",await again.Observe(marker,markerPos)&&await again.Observe(post,streetPos)&&await again.Owner(post,latencies)==adaAgent.ToString());
        var delMarker=await again.World(new{op="prepare",plan=Plan("delete_entity",marker,new{})},latencies);
        var deleted=await again.World(new{op="commit",plan_id=delMarker.GetProperty("plan_id").GetString()},latencies);
        Check("after reconnecting Ada deletes her marker through WorldLine; Ben observes the deletion",deleted.GetProperty("status").GetString()=="committed"&&await ben.Observe(marker,null)&&await again.Observe(marker,null));
        saved["marker_deleted_plan"]=delMarker.GetProperty("plan_id").GetString();
        var bfAfter=await ben.World(new{op="geo_entity",entity_id=building},latencies);
        Check("the building no longer lists the deleted object",!bfAfter.GetProperty("live_objects_on").EnumerateArray().Any(o=>o.GetProperty("id").GetString()==marker));
        saved["post_position"]=new JsonArray(streetPos.Select(v=>(JsonNode)v).ToArray());saved["lantern_position"]=new JsonArray(benPos.Select(v=>(JsonNode)v).ToArray());
        await again.Logout();await ben.Logout();
        File.WriteAllText(receiptPath,saved.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
    }
    else
    {
        // ---- after a simulator restart: binding, identities, objects and history persist.
        var saved=JsonDocument.Parse(File.ReadAllText(receiptPath)).RootElement;
        Check("the binding version survives the restart",version==saved.GetProperty("binding_version").GetString());
        var logins=await Task.WhenAll(Session.Login(cfg.GetProperty("ada"),"last","Ada"),Session.Login(cfg.GetProperty("ben"),"last","Ben"));
        var ada=logins[0];var ben=logins[1];
        Check("both identities are unchanged after restart",ada.Agent.ToString()==saved.GetProperty("ada_agent").GetString()&&ben.Agent.ToString()==saved.GetProperty("ben_agent").GetString());
        var b=await ada.World(new{op="geo_binding"},latencies);
        Check("the restarted region re-validated the same binding and its terrain",b.GetProperty("binding").GetProperty("version").GetString()==version&&b.GetProperty("terrain_matches_issued").GetBoolean()&&!b.GetProperty("recovery_required").GetBoolean());
        string post=saved.GetProperty("post").GetString()!,lantern=saved.GetProperty("lantern").GetString()!,marker=saved.GetProperty("marker").GetString()!;
        double[] V(string k)=>saved.GetProperty(k).EnumerateArray().Select(x=>x.GetDouble()).ToArray();
        Check("persistent objects keep identity, owner and transform across restart",await ada.Observe(post,V("post_position"))&&await ben.Observe(lantern,V("lantern_position"))
            &&await ada.Owner(post,latencies)==saved.GetProperty("ada_agent").GetString()&&await ben.Owner(lantern,latencies)==saved.GetProperty("ben_agent").GetString());
        Check("the deleted marker stays deleted",await ada.Observe(marker,null));
        var g=(await ben.World(new{op="geo_locate",entity_id=post},latencies)).GetProperty("geographic");var before=saved.GetProperty("post_geographic");
        double dLat=(g.GetProperty("latitude_degrees").GetDouble()-before.GetProperty("latitude_degrees").GetDouble())*111132,dLon=(g.GetProperty("longitude_degrees").GetDouble()-before.GetProperty("longitude_degrees").GetDouble())*78000;
        Check("the object's geographic position is unchanged across restart (< 1 cm)",Math.Sqrt(dLat*dLat+dLon*dLon)<.01,new{geographic=g});
        var journal=await ada.World(new{op="journal"},latencies);
        Check("WorldLine history survives restart",saved.GetProperty("plans").EnumerateArray().Take(2).All(pl=>journal.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==pl.GetString()))
            &&journal.EnumerateArray().Any(e=>e.GetProperty("plan_id").GetString()==saved.GetProperty("marker_deleted_plan").GetString()));
        // Lifecycle completes after restart: the owner deletes; the other user observes.
        var prep=await ada.World(new{op="prepare",plan=Plan("delete_entity",post,new{})},latencies);var done=await ada.World(new{op="commit",plan_id=prep.GetProperty("plan_id").GetString()},latencies);
        Check("after restart Ada deletes her street post; Ben observes it gone",done.GetProperty("status").GetString()=="committed"&&await ben.Observe(post,null));
        await ada.Logout();await ben.Logout();
    }
}
catch(Exception error){Check("probe completed without an unexpected error",false,error.Message);}
var summary=new{mode,passed=checks.Count(c=>((dynamic)c).ok),failed,checks,latency_ms=latencies.ToDictionary(k=>k.Key,v=>new{n=v.Value.Count,median=Median(v.Value),max=v.Value.Max()})};
var outPath=Path.Combine(evidence,"probe-"+mode+".json");File.WriteAllText(outPath,JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"GEO_PROBE {mode}: {summary.passed} passed, {failed} failed");
// Client UDP threads are foreground; a failed run must not linger holding sessions open.
Environment.Exit(failed==0?0:1);return 0;

static object Plan(string type,string? target,object parameters)=>new{schema_version=1,request="Geographic binding acceptance: "+type,provider="integration-test",model="none",actions=new[]{new{type,target_id=target,parameters}}};
static double GeoDistance(double[] a,double[] b)=>Math.Sqrt((a[0]-b[0])*(a[0]-b[0])+(a[1]-b[1])*(a[1]-b[1])+(a[2]-b[2])*(a[2]-b[2]));
static double Median(List<double> v){var s=v.OrderBy(x=>x).ToArray();return s.Length==0?0:Math.Round(s[s.Length/2],1);}

sealed class Session
{
    public GridClient Client=null!;public string Cap="",Name="";public UUID Agent;
    static readonly HttpClient Http=new(new SocketsHttpHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(60)};
    public static async Task<Session> Login(JsonElement account,string start,string name)
    {
        var c=new GridClient();
        c.Settings.LOGIN_SERVER=account.GetProperty("grid_url").GetString();c.Settings.ALWAYS_DECODE_OBJECTS=true;c.Settings.OBJECT_TRACKING=true;
        c.Settings.SEND_AGENT_UPDATES=true;c.Settings.SEND_AGENT_APPEARANCE=false;c.Settings.MULTIPLE_SIMS=false;
        bool ok=await Task.Run(()=>c.Network.Login(account.GetProperty("first_name").GetString(),account.GetProperty("last_name").GetString(),account.GetProperty("password").GetString(),"Finalverse.GeoProbe",start,"1.0"));
        if(!ok)throw new Exception(name+" login failed: "+c.Network.LoginMessage);
        await Task.Delay(2000);
        var seed=new OSDArray{OSD.FromString("FinalverseWorld")};
        var content=new StringContent(OSDParser.SerializeLLSDXmlString(seed),Encoding.UTF8);content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/llsd+xml");
        var response=await Http.PostAsync(c.Network.CurrentSim.Caps.SeedCapsURI,content);
        var caps=(OSDMap)OSDParser.DeserializeLLSDXml(await response.Content.ReadAsStringAsync());
        return new Session{Client=c,Cap=caps["FinalverseWorld"].AsString(),Name=name,Agent=c.Self.AgentID};
    }
    public static async Task<(bool ok,string? error,JsonElement? result)> Raw(string cap,object body)
    {
        try
        {
            // MutSea closes a keep-alive connection after 19 requests; never reuse one for a POST.
            using var request=new HttpRequestMessage(HttpMethod.Post,cap){Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
            request.Headers.ConnectionClose=true;
            var r=await Http.SendAsync(request);var text=await r.Content.ReadAsStringAsync();
            if(!r.IsSuccessStatusCode&&text.Length==0)return (false,"HTTP "+(int)r.StatusCode,null);
            if(!text.TrimStart().StartsWith('{'))return (false,"HTTP "+(int)r.StatusCode+" (no World API response)",null);
            var root=JsonDocument.Parse(text).RootElement;
            return root.GetProperty("ok").GetBoolean()?(true,null,root.GetProperty("result").Clone()):(false,"HTTP "+(int)r.StatusCode+": "+root.GetProperty("error").GetString(),null);
        }
        catch(Exception e) when(e is HttpRequestException or JsonException){return (false,e.GetType().Name,null);}
    }
    public async Task<(bool ok,string? error,JsonElement? result)> TryWorld(object body){await Task.Delay(150);return await Raw(Cap,body);}
    public async Task<JsonElement> World(object body,Dictionary<string,List<double>> latencies)
    {
        await Task.Delay(150);var timer=Stopwatch.StartNew();var r=await Raw(Cap,body);
        string op=JsonSerializer.SerializeToElement(body).GetProperty("op").GetString()!;
        lock(latencies){if(!latencies.TryGetValue(op,out var l))latencies[op]=l=new();l.Add(timer.Elapsed.TotalMilliseconds);}
        if(!r.ok)throw new Exception(Name+" "+op+" refused: "+r.error);return r.result!.Value;
    }
    public int Conflicts;
    // A create plan is validated against the whole scene; if another user commits first, MutSea
    // refuses the stale commit and the client plans again, exactly as a viewer would.
    public async Task<(string id,string plan,string status)> Create(string name,double[] position,string shape,Dictionary<string,List<double>> latencies)
    {
        for(int attempt=0;;attempt++)
        {
            var prepared=await World(new{op="prepare",plan=CreatePlan(name,position,shape)},latencies);
            var r=await TryWorld(new{op="commit",plan_id=prepared.GetProperty("plan_id").GetString()});
            if(!r.ok&&r.error!.Contains("Scene changed")&&attempt<5){Conflicts++;continue;}
            if(!r.ok)throw new Exception(Name+" commit refused: "+r.error);
            var committed=r.result!.Value;
            return (committed.GetProperty("steps")[0].GetProperty("id").GetString()!,committed.GetProperty("plan_id").GetString()!,committed.GetProperty("status").GetString()!);
        }
    }
    public static object CreatePlan(string name,double[] position,string shape)=>new{schema_version=1,request="Geographic binding acceptance: create "+name,provider="integration-test",model="none",
        actions=new[]{new{type="create_primitive",target_id=(string?)null,parameters=(object)new{position,scale=new[]{1,1,1},color=new[]{.4,.26,.68,1},shape,name}}}};
    // Ownership from the authoritative World API; UDP object updates do not carry it.
    public async Task<string?> Owner(string id,Dictionary<string,List<double>> latencies)=>(await World(new{op="inspect",selected_id=id},latencies)).GetProperty("selected_object").GetProperty("owner_id").GetString();
    public Primitive? Prim(string id){var u=new UUID(id);return Client.Network.CurrentSim.ObjectsPrimitives.Copy().Values.FirstOrDefault(p=>p.ID==u&&p.ParentID==0);}
    // position null: wait for absence.
    public async Task<bool> Observe(string id,double[]? position)
    {
        var deadline=DateTime.UtcNow.AddSeconds(20);
        do
        {
            var p=Prim(id);
            if(position==null&&p==null)return true;
            if(position!=null&&p!=null&&Vector3.Distance(p.Position,new((float)position[0],(float)position[1],(float)position[2]))<.02f)return true;
            await Task.Delay(200);
        }while(DateTime.UtcNow<deadline);
        return false;
    }
    public async Task Logout(){Client.Network.Logout();var d=DateTime.UtcNow.AddSeconds(8);while(Client.Network.Connected&&DateTime.UtcNow<d)await Task.Delay(100);await Task.Delay(1500);}
}
