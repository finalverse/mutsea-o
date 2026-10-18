// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
using System.Text.Json;
int passed=0;void Check(bool ok){if(!ok)throw new Exception("Assertion failed");}
void Test(string name,Action action){action();Console.WriteLine("PASS "+name);passed++;}
void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidDataException or JsonException){return;}throw new Exception("Expected rejection");}
var region=Guid.NewGuid().ToString();var actor=Guid.NewGuid().ToString();var other=Guid.NewGuid().ToString();
var d=new LibraryDestination("confluence","Confluence","Conversation circle",region,[128,116,24],"CC0-1.0","Finalverse contributors","Original Starter Library","copy-under-license","mutsea.starter.confluence@0.1.0",[new("circle","Circle","Review ideas together",[128,128],12)]);
WorldLibrary Load(params LibraryDestination[] values)=>new(JsonSerializer.SerializeToUtf8Bytes(new LibraryManifest(1,values)));
Test("removed capability response differs from World API success",()=>{
 Check(MutSea.LibrarySmoke.CapabilityResponse.Unavailable(404,[]));
 Check(MutSea.LibrarySmoke.CapabilityResponse.Unavailable(400,System.Text.Encoding.UTF8.GetBytes("{\"ok\":false}")));
 Check(MutSea.LibrarySmoke.CapabilityResponse.Unavailable(200,[]));
 Check(!MutSea.LibrarySmoke.CapabilityResponse.Unavailable(200,System.Text.Encoding.UTF8.GetBytes("{\"ok\":true,\"result\":{}}")));
 Check(!MutSea.LibrarySmoke.CapabilityResponse.Unavailable(200,System.Text.Encoding.UTF8.GetBytes("<html>unknown response</html>")));
 Check(!MutSea.LibrarySmoke.CapabilityResponse.Unavailable(500,[]));
});
Test("valid pinned directory and workspace",()=>Check(Load(d).Find(region)!.workspace![0].id=="circle"));
Test("duplicate region or ID",()=>{Reject(()=>Load(d,d with{id="other"}));Reject(()=>Load(d,d with{region_id=other}));});
Test("no arbitrary schemes templates or IDs",()=>{Reject(()=>Load(d with{template="../../other@1.0.0"}));Reject(()=>Load(d with{region_id="bad"}));Reject(()=>Load(d with{license="unknown"}));});
Test("bounded finite spawns and workspace",()=>{Reject(()=>Load(d with{spawn=[128,256,24]}));Reject(()=>Load(d with{workspace=[new("circle","Circle","Chat",[128,128],33)]}));});
Test("citizen host isolation preserves single-region compatibility",()=>{Check(WorldLibrary.Hosts("",region));Check(WorldLibrary.Hosts(region,region));Check(!WorldLibrary.Hosts(region,other));Check(!WorldLibrary.Hosts("invalid",region));});
Test("archive admission pins exact bytes and cannot relabel mixed rights",()=>{
 var a=new LibraryArchive("zadaroo.lakehouse",new string('a',64),new string('b',64),new string('c',64),1722);
 Check(Load(d with{template=null,archive=a}).Find(region)!.archive!.expected_parts==1722);
 Reject(()=>Load(d with{archive=a}));Reject(()=>Load(d with{template=null,archive=a,license="LicenseRef-Reviewed-Mixed"}));
 Reject(()=>Load(d with{template=null,archive=a with{id="../escape"}}));
 Reject(()=>Load(d with{template=null,archive=a with{expected_parts=10001}}));
});
Test("native archive stream contract is bounded and seekable",()=>{
 byte[] bytes;
 using(var source=new MemoryStream()){using(var gzip=new System.IO.Compression.GZipStream(source,System.IO.Compression.CompressionLevel.Optimal,true))gzip.Write(new byte[1024]);bytes=source.ToArray();}
 using var tar=PassiveArchive.OpenTar(bytes);Check(tar.CanSeek&&tar.Position==0&&tar.Length==1024);
 Reject(()=>PassiveArchive.OpenTar(bytes,1023));Reject(()=>PassiveArchive.OpenTar([]));
});
var root=Path.Combine(Path.GetTempPath(),"mutsea-library-"+Guid.NewGuid());
try{
var store=new CreatorLibrary(root);var plan=Guid.NewGuid().ToString();
var recipe=JsonSerializer.SerializeToElement(new{schema_version=1,license="CC0-1.0",place_id="user.test.garden",version="0.1.0"});
CreatorRelease? release=null;
Test("private immutable release is idempotent",()=>{release=store.Save(actor,region,plan,"Garden",recipe);Check(store.Save(actor,region,plan,"Garden",recipe).release_id==release.release_id);Check(store.Published().Length==0);});
Test("owner sees only own releases",()=>{Check(store.Mine(actor).Length==1);Check(store.Mine(other).Length==0);});
Test("publish free and preserve owner",()=>{var l=store.Publish(actor,release!.release_id,true,0,"USD");Check(l.published&&!l.checkout_enabled);Check(store.Published().Length==1);Reject(()=>store.Publish(other,release.release_id,false,0,"USD"));});
Test("offer is priced but cannot take payment",()=>{var l=store.Publish(actor,release!.release_id,true,1200,"USD");Check(l.price_minor==1200&&!l.checkout_enabled);Reject(()=>store.Publish(actor,release.release_id,true,-1,"USD"));});
Test("unpublish retains immutable bytes and origin",()=>{store.Publish(actor,release!.release_id,false,0,"USD");Check(store.Published().Length==0);Check(store.Get(release.release_id).content.source_plan_id==plan);});
Test("remix lineage survives process restart",()=>{var remix=Guid.NewGuid().ToString();store.LinkRemix(actor,remix,release!.release_id);var r=new CreatorLibrary(root).Save(actor,region,remix,"Remix",recipe);Check(r.content.parent_release==release.release_id);});
Test("unknown license and oversized title cannot be laundered",()=>{Reject(()=>store.Save(actor,region,plan,"Bad",JsonSerializer.SerializeToElement(new{license="LicenseRef-Unknown"})));Reject(()=>store.Save(actor,region,plan,new string('x',81),recipe));});
Test("path traversal and altered release fail closed",()=>{Reject(()=>store.Get("../escape"));File.AppendAllText(Path.Combine(root,release!.release_id+".release.json"),"x");Reject(()=>store.Get(release.release_id));});
}finally{Directory.Delete(root,true);}
Console.WriteLine($"{passed} World Library/creator tests passed");
