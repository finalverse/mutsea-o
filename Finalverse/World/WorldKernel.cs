// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Finalverse.World;

public sealed record ActionSpec(string type, string? target_id, JsonElement parameters);
public sealed record Plan(int schema_version, string request, string provider, string model, ActionSpec[] actions);
public sealed record Entity(string id, string name, string description, string owner_id, string shape,
    double[] position, double[] rotation, double[] scale, double[] color, bool editable, bool copyable, bool deletable, double clearance_radius = 0);
public sealed record Step(string type, string id, Entity? before, Entity? after);
public sealed record Prepared(string plan_id, string actor_id, DateTimeOffset expires_at, Plan proposal, Step[] steps, string scene_revision, string summary);
public sealed record Entry(string operation_id, string plan_id, string actor_id, DateTimeOffset timestamp,
    string status, Plan proposal, Step[] steps, string? error, string? owner_agent_id, Entity?[]? observed_state = null);
public interface IWorld
{
    string RegionId { get; }
    string RegionName { get; }
    double WaterHeight { get; }
    double SizeX { get; }
    double SizeY { get; }
    double[] Avatar(string actor);
    double[] Orientation(string actor);
    Entity? Find(string id, string actor);
    IEnumerable<Entity> Entities(string actor);
    double Height(double x, double y);
    bool CanCreate(string actor, int count, double[] position);
    void Apply(Step step, string actor);
}

// No model, viewer or renderer dependency. Every proposal is untrusted data.
public sealed class Kernel
{
    public static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    readonly IWorld world;
    readonly string journal;
    readonly Dictionary<string, Prepared> pending = new();
    readonly List<Entry> entries = new();
    readonly HashSet<string> undone = new();
    public bool RecoveryRequired { get; private set; }
    public Func<string,string,object?>? ContentSemantics { get; set; }
    public Func<object?>? AvatarBinding { get; set; }
    public const int MaxActions = 8, MaxCreates = 24;
    public const double MaxVolume = 120, MaxMove = 12, MaxScale = 12;
    public static readonly string LumiId = "b151468b-cbcc-4af2-ae2d-6967e8938bb4";
    public static string TraceIdForCitizen(int component) => TraceId(LumiId, component);
    public Kernel(IWorld world, string directory)
    {
        this.world = world;
        Directory.CreateDirectory(directory);
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        journal = Path.Combine(directory, world.RegionId + ".worldline.jsonl");
        if (File.Exists(journal))
            foreach (string line in File.ReadLines(journal)) entries.Add(JsonSerializer.Deserialize<Entry>(line, Json)!);
        RecoveryRequired = entries.Any(e => e.status == "recovery_required") || entries.Where(e => e.status == "intent").Any(e => !entries.Any(x => x.operation_id == e.operation_id && x.status != "intent"));
        foreach (var e in entries.Where(e => e.status == "undone")) undone.Add(e.plan_id);
    }
    public void InvalidateSession(string actor) { foreach(var id in pending.Where(x=>x.Value.actor_id==actor).Select(x=>x.Key).ToArray()) pending.Remove(id); }
    public Entry[] History(string actor) => entries.Where(e => e.actor_id == actor).TakeLast(100).ToArray();
    public object Lumi(string actor)
    {
        var home = entries.LastOrDefault(e => e.actor_id == actor && e.status == "committed" && e.owner_agent_id == LumiId && !undone.Contains(e.plan_id));
        return new { agent_id = LumiId, display_name = "Lumi", avatar_binding = AvatarBinding?.Invoke(),
            home_entity_id = home?.steps.FirstOrDefault(s => s.type == "create")?.id,
            memory = home == null ? Array.Empty<string>() : new[] { home.proposal.request },
            profile = "Persistent semantic citizen identity; autonomous navigation is not enabled." };
    }
    public object? Semantic(string id, string actor)
    {
        var content = ContentSemantics?.Invoke(id, actor);
        if (content != null) return content;
        var operation = entries.LastOrDefault(e => e.actor_id == actor && e.status == "committed" && !undone.Contains(e.plan_id) && e.steps.Any(s => s.id == id && s.type == "create"));
        if (operation == null || world.Find(id, actor) == null) return null;
        return new { entity_id = id, structure_id = operation.steps.First(s => s.type == "create").id,
            semantic_type = operation.owner_agent_id == LumiId ? (id==operation.steps.First(s=>s.type=="create").id?"home":"home_component") : "primitive", owner_agent_id = operation.owner_agent_id,
            source_prompt = operation.proposal.request, created_by_model = operation.proposal.model, provider = operation.proposal.provider,
            operation.timestamp, version = 1 };
    }
    public object Inspect(string actor, string? selected, double radius = 64)
    {
        Require(double.IsFinite(radius) && radius >= 1 && radius <= 64, "Inspection radius must be 1–64 meters.");
        var pos = world.Avatar(actor);
        if(selected != null) Require(Guid.TryParse(selected,out _) && world.Find(selected,actor) is {} chosen && Distance(chosen.position,pos)<=64,"Selected entity is missing or outside the inspection scope.");
        var nearby = world.Entities(actor).Select(e => new { entity = e, distance = Distance(e.position, pos), semantic = Semantic(e.id, actor) })
            .Where(e => e.distance <= radius).OrderBy(e => e.distance).ToArray();
        return new { schema_version = 1, region = new { id = world.RegionId, name = world.RegionName, bounds = new[] { world.SizeX, world.SizeY } },
            avatar = new { position = pos, orientation = world.Orientation(actor) }, selected_object = selected == null ? null : world.Find(selected, actor),
            nearby_entities = nearby.Take(64), truncated = nearby.Length > 64, radius, coverage = "authoritative regional radius query",
            terrain = new { height = world.Height(pos[0], pos[1]) }, lumi = Lumi(actor), undoable_plan_id = entries.LastOrDefault(e=>e.actor_id==actor&&e.status=="committed"&&!undone.Contains(e.plan_id))?.plan_id, recovery_required = RecoveryRequired };
    }
    public Prepared Prepare(Plan proposal, string actor)
    {
        Require(!RecoveryRequired, "An interrupted operation requires administrator recovery before more edits.");
        Require(proposal.schema_version == 1 && proposal.actions != null && proposal.actions.Length is > 0 and <= MaxActions, "Invalid schema or too many actions.");
        Require(!string.IsNullOrWhiteSpace(proposal.request) && proposal.request.Length <= 2048 && proposal.provider?.Length is > 0 and <= 80 && proposal.model?.Length is > 0 and <= 120, "Invalid request or provenance.");
        Require(Guid.TryParse(actor, out _), "Invalid authenticated actor.");
        string id = Guid.NewGuid().ToString();
        var steps = new List<Step>();
        var targets = new HashSet<string>();
        var avatar = world.Avatar(actor);
        int component = 0;
        Entity New(string name, string shape, double[] pos, double[] size, double[] color, double yaw = 0) =>
            new(TraceId(id, component++), name, "Created through Finalverse World API", actor, shape, pos,
                new[] { 0d, 0, Math.Sin(yaw/2), Math.Cos(yaw/2) }, size, color, true, true, true);
        foreach (var a in proposal.actions!)
        {
            Require(a != null && a.parameters.ValueKind == JsonValueKind.Object, "Malformed action parameters.");
            var p = a!.parameters;
            if (a.type == "create_structure")
            {
                Fields(p, "anchor", "width", "depth", "height", "yaw", "owner_agent_id");
                Require(a.target_id == null && p.GetProperty("owner_agent_id").GetString() == LumiId, "This home must bind to Lumi.");
                var anchor = Vec(p, "anchor", 3); anchor[2] = world.Height(anchor[0],anchor[1]); double w = Number(p, "width"), d = Number(p, "depth"), h = Number(p, "height"), yaw = Number(p, "yaw");
                Require(anchor[2] >= world.WaterHeight+.5,"Choose dry ground above the water for the home.");
                Require(w >= 4 && w <= 8 && d >= 4 && d <= 8 && h >= 2.5 && h <= 4 && Math.Abs(yaw) <= Math.PI*2, "Home dimensions must be 4–8m × 4–8m × 2.5–4m.");
                Require(Distance(anchor, avatar) <= 16, "Move closer to the proposed home site.");
                // Check the complete rotated footprint against every scene object; preserve paths and lake.
                var half = Math.Sqrt(w*w+d*d)/2 + .6;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                {
                    var px = anchor[0]+x*half; var py = anchor[1]+y*half;
                    Require(px > 1 && py > 1 && px < world.SizeX-1 && py < world.SizeY-1, "The home would cross the region boundary.");
                    Require(Math.Abs(world.Height(px,py)-anchor[2]) <= .6, "Choose level, dry ground for the home.");
                }
                foreach (var e in world.Entities(actor))
                    Require(Math.Abs(e.position[0]-anchor[0]) > half + Footprint(e) || Math.Abs(e.position[1]-anchor[1]) > half + Footprint(e), "An existing object or path overlaps the proposed home footprint.");
                void Part(string name, double x, double y, double z, double sx, double sy, double sz, double[] c)
                {
                    var pos = new[] { anchor[0]+x*Math.Cos(yaw)-y*Math.Sin(yaw), anchor[1]+x*Math.Sin(yaw)+y*Math.Cos(yaw), anchor[2]+z };
                    var e = New("Lumi Home · " + name, "box", pos, new[] { sx,sy,sz }, c, yaw);
                    steps.Add(new("create", e.id, null, e));
                }
                double[] wood = [.46,.32,.22,1], silver = [.76,.81,.88,1], purple = [.4,.26,.68,1];
                Part("Floor",0,0,.12,w,d,.24,wood);
                Part("Back wall",0,d/2,h/2,w,.2,h,silver);
                // Front wall has a real open doorway, not a painted rectangle.
                Part("Door left",-(w/4+.4),-d/2,h/2,w/2-.8,.2,h,silver);
                Part("Door right",w/4+.4,-d/2,h/2,w/2-.8,.2,h,silver);
                Part("Door lintel",0,-d/2,h-.35,1.6,.2,.7,silver);
                // Side walls have two open window apertures.
                foreach (double side in new[] {-1d,1d})
                {
                    Part("Window sill "+side,side*w/2,0,.6,.2,d,1.2,silver);
                    Part("Window header "+side,side*w/2,0,h-.3,.2,d,.6,silver);
                    foreach (double end in new[] {-1d,1d}) Part("Window edge "+side+"-"+end,side*w/2,end*(d/4+.5),h/2,.2,d/2-1,h-1.2,silver);
                }
                Part("Roof",0,0,h+.2,w+.6,d+.6,.4,purple);
                Part("Table",0,.6,.75,1.4,1,.12,wood);
                Part("Table pedestal",0,.6,.4,.25,.25,.7,wood);
                Part("Seat",1.4,.6,.5,.7,.7,.3,purple);
            }
            else if (a.type == "create_primitive")
            {
                Fields(p,"position","scale","color","shape","name");
                Require(a.target_id == null, "Creation cannot target an existing object.");
                string shape = p.GetProperty("shape").GetString()!;
                Require(shape is "box" or "sphere", "Only box and sphere primitives are supported.");
                var e = New(Text(p,"name",80),shape,Vec(p,"position",3),Vec(p,"scale",3),Vec(p,"color",4));
                steps.Add(new("create",e.id,null,e));
            }
            else
            {
                Require(a.target_id != null && Guid.TryParse(a.target_id,out _) && targets.Add(a.target_id), "Targets must be explicit unique entity IDs.");
                var before = world.Find(a.target_id!, actor);
                Require(before != null && before.owner_id == actor && before.editable, "You can only edit your own unlinked, nonphysical objects.");
                Require(Distance(before!.position,avatar) <= 64, "Target is outside your inspection scope.");
                Entity? after = before;
                switch(a.type)
                {
                    case "clone_entity":
                        Fields(p,"position");
                        Require(before.copyable&&Semantic(before.id,actor)!=null&&!entries.Any(e=>e.status=="committed"&&e.owner_agent_id==LumiId&&e.steps.Any(s=>s.id==before.id)),"Clone supports your AI-created plain primitives; complete homes use a new structure plan.");
                        var clone=New(before.name[..Math.Min(74,before.name.Length)]+" copy",before.shape,Vec(p,"position",3),before.scale.ToArray(),before.color.ToArray()) with {rotation=before.rotation.ToArray(),description=before.description};
                        Require(Distance(before.position,clone.position)<=MaxMove,"Clone placement exceeds 12 meters.");
                        steps.Add(new("create",clone.id,null,clone));continue;
                    case "move_entity":
                        double[] destination;
                        if(p.TryGetProperty("position",out _)){Fields(p,"position");destination=Vec(p,"position",3);}
                        else
                        {
                            Fields(p,"toward_id","distance");string reference=p.GetProperty("toward_id").GetString()!;
                            Require(Guid.TryParse(reference,out _),"Invalid spatial reference.");var toward=world.Find(reference,actor);
                            Require(toward!=null&&Distance(toward.position,avatar)<=64,"Spatial reference is outside the inspection scope.");
                            double distance=Number(p,"distance"),dx=toward!.position[0]-before.position[0],dy=toward.position[1]-before.position[1],length=Math.Sqrt(dx*dx+dy*dy);
                            Require(distance>0&&distance<=MaxMove&&length>.01,"Invalid relative movement.");
                            destination=[before.position[0]+dx/length*distance,before.position[1]+dy/length*distance,before.position[2]];
                        }
                        after=before with {position=destination};Require(Distance(before.position,after.position)<=MaxMove,"Move exceeds 12 meters.");break;
                    case "rotate_entity": Fields(p,"rotation"); var q=Vec(p,"rotation",4); Require(Math.Abs(q.Sum(x=>x*x)-1)<.01,"Rotation must be a unit quaternion."); after=before with {rotation=q}; break;
                    case "scale_entity": Fields(p,"scale"); after=before with {scale=Vec(p,"scale",3)}; break;
                    case "set_name": Fields(p,"name"); after=before with {name=Text(p,"name",80)}; break;
                    case "set_description": Fields(p,"description"); after=before with {description=Text(p,"description",256)}; break;
                    case "set_color": Fields(p,"color"); after=before with {color=Vec(p,"color",4)}; break;
                    case "delete_entity": Fields(p); Require(!entries.Any(e=>e.status=="committed"&&e.owner_agent_id==LumiId&&!undone.Contains(e.plan_id)&&e.steps.Any(s=>s.id==before.id)),"Undo the complete home instead of deleting one component."); Require(before.deletable && before.copyable && Semantic(before.id,actor)!=null,"Only your AI-created plain primitives can be deleted and restored."); after=null; break;
                    default: throw new InvalidDataException("Unsupported action: " + a.type);
                }
                steps.Add(new(after==null?"delete":"update",before.id,before,after));
            }
        }
        Require(steps.Count(s=>s.type=="create")<=MaxCreates && steps.Sum(s=>s.after?.scale.Aggregate(1d,(v,x)=>v*x)??0)<=MaxVolume,"Creation exceeds the primitive/volume budget.");
        Require(world.CanCreate(actor,steps.Count(s=>s.type=="create"),avatar),"Region object budget or build permission rejected this plan.");
        ValidateSteps(steps,actor);
        var prepared=new Prepared(id,actor,DateTimeOffset.UtcNow.AddMinutes(2),proposal,steps.ToArray(),SceneRevision(actor),$"{steps.Count} bounded world changes. Build creates {steps.Count(s=>s.type=="create")} primitives; undo uses guarded compensation.");
        foreach(var old in pending.Where(x=>x.Value.expires_at<DateTimeOffset.UtcNow).Select(x=>x.Key).ToArray()) pending.Remove(old);
        Require(pending.Count < 100, "Too many pending plans; wait for expiry."); pending.Add(id,prepared); return prepared;
    }
    string SceneRevision(string actor)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(world.Entities(actor).OrderBy(e=>e.id)))));
    void ValidateSteps(IEnumerable<Step> steps,string actor)
    {
        foreach(var s in steps)
        {
            if(s.before!=null) Require(Same(world.Find(s.id,actor),s.before),"The target changed since this plan was prepared. Please plan again.");
            else Require(world.Find(s.id,actor)==null,"Creation ID already exists.");
            if(s.after==null) continue;
            var e=s.after;
            Require(e.position.All(double.IsFinite)&&e.scale.All(x=>double.IsFinite(x)&&x>=.05&&x<=MaxScale)&&e.color.All(x=>double.IsFinite(x)&&x>=0&&x<=1),"Invalid geometry or color.");
            double half=Footprint(e);
            Require(e.position[0]-half>=0&&e.position[1]-half>=0&&e.position[0]+half<world.SizeX&&e.position[1]+half<world.SizeY&&e.position[2]>=0&&e.position[2]+e.scale[2]/2<=256,"Geometry is outside permitted region bounds.");
            Require(Distance(e.position,world.Avatar(actor))<=64&&world.CanCreate(actor,1,e.position),"No build permission at the destination.");
        }
    }
    public Entry Commit(string planId,string actor)
    {
        var previous=entries.LastOrDefault(e=>e.plan_id==planId&&e.actor_id==actor&&e.status=="committed");
        if(previous!=null && !undone.Contains(planId)) return previous; // retries never duplicate geometry
        Require(!RecoveryRequired&&pending.TryGetValue(planId,out var _),"Plan missing or recovery required.");
        var plan=pending[planId]; Require(plan.actor_id==actor&&plan.expires_at>DateTimeOffset.UtcNow,"Plan expired or belongs to another actor.");
        if(plan.steps.Any(s=>s.type=="create")) Require(plan.scene_revision==SceneRevision(actor),"Scene changed after placement validation. Please plan again.");
        ValidateSteps(plan.steps,actor); pending.Remove(planId);
        return Execute(planId,actor,plan.proposal,plan.steps,"committed",plan.proposal.actions.Any(a=>a.type=="create_structure")?LumiId:null);
    }
    public Entry Undo(string planId,string actor)
    {
        Require(!RecoveryRequired&&!undone.Contains(planId),"Operation already undone or recovery required.");
        var e=entries.LastOrDefault(x=>x.plan_id==planId&&x.actor_id==actor&&x.status=="committed"); Require(e!=null,"Operation not found.");
        var inverse=e!.steps.Reverse().Select(s=>new Step(s.before==null?"delete":s.after==null?"create":"update",s.id,s.after,s.before)).ToArray();
        ValidateSteps(inverse,actor);
        var result=Execute(planId,actor,e.proposal,inverse,"undone",e.owner_agent_id); if(result.status=="undone") undone.Add(planId); return result;
    }
    Entry Execute(string planId,string actor,Plan proposal,Step[] steps,string success,string? lumi)
    {
        string operation=Guid.NewGuid().ToString();
        var entry=new Entry(operation,planId,actor,DateTimeOffset.UtcNow,"intent",proposal,steps,null,lumi);
        Append(entry); // durable before first mutation
        var applied=new List<Step>();
        try
        {
            foreach(var s in steps) { Require(Same(world.Find(s.id,actor),s.before),"Target changed immediately before execution."); applied.Add(s); world.Apply(s,actor); Require(Same(world.Find(s.id,actor),s.after),"Simulator did not apply the expected state."); }
            entry=entry with {status=success,observed_state=steps.Select(s=>world.Find(s.id,actor)).ToArray()}; Append(entry); return entry;
        }
        catch(Exception ex)
        {
            var errors=new List<string>();
            foreach(var s in applied.AsEnumerable().Reverse())
                try
                {
                    var current=world.Find(s.id,actor);
                    if(Same(current,s.before))continue; // Failed before applying, or already restored.
                    Require(Same(current,s.after),"Compensation refused: target changed or was only partially applied.");
                    world.Apply(new(s.before==null?"delete":s.after==null?"create":"update",s.id,s.after,s.before),actor);
                    Require(Same(world.Find(s.id,actor),s.before),"Compensation mismatch");
                }
                catch(Exception rollback) { errors.Add(rollback.Message); }
            RecoveryRequired=errors.Count>0;
            entry=entry with {status=RecoveryRequired?"recovery_required":"rolled_back",observed_state=steps.Select(s=>world.Find(s.id,actor)).ToArray(),error=ex.Message+(errors.Count>0?"; compensation: "+string.Join("; ",errors):"")};
            Append(entry); return entry;
        }
    }
    void Append(Entry entry)
    {
        try
        {
            using(var f=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read))
            { var b=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry,Json)+"\n"); f.Write(b);f.Flush(true); }
            if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(journal,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            entries.Add(entry);
        }
        catch { RecoveryRequired=true;throw; } // No further writes after unconfirmed audit durability.
    }
    public static bool Same(Entity? a,Entity? b) => a==null||b==null ? a==b : a.id==b.id&&a.owner_id==b.owner_id&&a.shape==b.shape&&a.editable==b.editable&&a.copyable==b.copyable&&a.deletable==b.deletable&&a.name==b.name&&a.description==b.description&&Distance(a.position,b.position)<.01&&Distance(a.scale,b.scale)<.01&&Distance(a.rotation,b.rotation)<.01&&Distance(a.color,b.color)<.01;
    static string TraceId(string plan,int n)=>new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(plan+"/"+n)).AsSpan(0,16)).ToString();
    static double Footprint(Entity e)=>Math.Max(e.clearance_radius,Math.Sqrt(e.scale.Sum(x=>x*x))/2);
    static double Distance(double[] a,double[] b)=>Math.Sqrt(a.Zip(b,(x,y)=>(x-y)*(x-y)).Sum());
    static void Require(bool ok,string reason) { if(!ok) throw new InvalidDataException(reason); }
    static void Fields(JsonElement p,params string[] allowed) { Require(p.EnumerateObject().Count()==allowed.Length&&p.EnumerateObject().All(x=>allowed.Contains(x.Name)),"Unexpected or missing action fields."); }
    static double Number(JsonElement p,string key) { double n=p.GetProperty(key).GetDouble();Require(double.IsFinite(n),"Non-finite number.");return n; }
    static double[] Vec(JsonElement p,string key,int length) { var a=p.GetProperty(key).EnumerateArray().Select(x=>x.GetDouble()).ToArray();Require(a.Length==length&&a.All(double.IsFinite),"Malformed vector.");return a; }
    static string Text(JsonElement p,string key,int max) { var s=p.GetProperty(key).GetString();Require(!string.IsNullOrWhiteSpace(s)&&s.Length<=max&&!s.Any(char.IsControl),"Invalid text.");return s!; }
}
