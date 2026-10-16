// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MutSea.AccountPortal;
using MutSea.Registration;
using System.Net.Sockets;

if(args is ["--native",var fixture]){NativeChecks.Run(fixture);return;}
if(args is ["--world-registration",var mode,var directory]){await WorldRegistrationChecks.Run(mode,directory);return;}
int passed=0;void Check(bool yes,string name){if(!yes)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
void Fault(Action action,int status,string name){try{action();throw new Exception("Expected rejection: "+name);}catch(PortalFault f){Check(f.Status==status,name);}}
if(OperatingSystem.IsLinux())
{
 string dir=Path.Combine(Path.GetTempPath(),"mutsea-ipc-"+Guid.NewGuid());Directory.CreateDirectory(dir);
 try
 {
  string file=Path.Combine(dir,"ordinary"),path=Path.Combine(dir,"socket"),link=Path.Combine(dir,"link");File.WriteAllText(file,"keep");
  using var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);socket.Bind(new UnixDomainSocketEndPoint(path));File.CreateSymbolicLink(link,path);
  Check(!UnixEndpoint.IsSocket(file)&&File.ReadAllText(file)=="keep","IPC cleanup excludes ordinary files");
  Check(UnixEndpoint.IsSocket(path),"IPC cleanup recognizes actual Unix sockets");
  Check(!UnixEndpoint.IsSocket(link),"IPC cleanup excludes symbolic links");
 }
 finally{Directory.Delete(dir,true);}
}
var native=new FixtureIdentity();var store=new MemoryState();var now=DateTimeOffset.UtcNow;var key=new byte[32];var core=new Accounts(native,store,key,()=>now);
string token=core.Login("Portal Smoke",native.Password,"one");var signed=core.Require(token);
Check(signed.Account.Id==native.Id,"Native identity bound to web session");Check(!store.Sessions.ContainsKey(token)&&store.Sessions.ContainsKey(Accounts.Hash(token)),"Only session digests are persisted");
Fault(()=>core.Login("Portal Smoke","incorrect","two"),401,"Wrong password denied");
Fault(()=>core.Login("No Account","incorrect","three"),401,"Unknown account has same response");
var changed=core.SaveProfile(token,"<script>literal profile text</script>","example@example.invalid","en",0);Check(changed.Version==1,"Profile sidecar saves with version");
Check(core.SaveProfile(token,"Updated","","zh",1).Version==2,"Versioned profile update");Fault(()=>core.SaveProfile(token,"Stale","","en",1),409,"Concurrent profile edit rejected");
Check(!store.Sessions.Values.Any(s=>s.User!=native.Id),"Profile operations retain the authenticated owner");
Fault(()=>core.Codes(token,"incorrect","four"),401,"Recovery generation requires current password");
string[] codes=core.Codes(token,native.Password,"five");Check(codes.Length==6&&core.CodeCount(core.Require(token))==6,"Six recovery codes generated");
Check(codes.All(c=>!store.Codes.Keys.Any(k=>k.Contains(c.Replace("-","")))),"Only recovery-code digests stored");
string other=core.Login("Portal Smoke",native.Password,"six");core.Reset("Portal Smoke",codes[0],"NewPassword123!","seven");
Check(core.Session(token)==null&&core.Session(other)==null,"Password reset revokes all browser sessions");
Fault(()=>core.Reset("Portal Smoke",codes[0],"AnotherPass12!","eight"),401,"Recovery code cannot be replayed");
Fault(()=>core.Reset("Portal Smoke",codes[1],"AnotherPass12!","nine"),401,"Credential rotation invalidates remaining old codes");
Fault(()=>core.Login("Portal Smoke","OldPassword123!","ten"),401,"Old world password stops working");
token=core.Login("Portal Smoke",native.Password,"eleven");Check(core.Require(token).Account.Id==native.Id,"New world password signs in");
Fault(()=>core.ChangePassword(token,native.Password,"PasswordThatIsTooLongForViewer","twelve"),400,"Password policy preserves released viewer compatibility");
Fault(()=>core.ChangePassword(token,"wrong","OtherPass123!","thirteen"),401,"Password change reauthenticates");
core.ChangePassword(token,native.Password,"OtherPass123!","fourteen");Check(core.Session(token)==null,"Password change ends current session");
token=core.Login("Portal Smoke",native.Password,"fifteen");native.ExternalRotation();Check(core.Session(token)==null,"External native password change invalidates web session");
token=core.Login("Portal Smoke",native.Password,"sixteen");native.Active=false;Check(core.Session(token)==null,"Operator-disabled account loses web access");native.Active=true;
token=core.Login("Portal Smoke",native.Password,"seventeen");now+=TimeSpan.FromMinutes(31);Check(core.Session(token)==null,"Idle session expires");
token=core.Login("Portal Smoke",native.Password,"eighteen");now+=TimeSpan.FromHours(13);Check(core.Session(token)==null,"Absolute session expiry enforced");
token=core.Login("Portal Smoke",native.Password,"nineteen");core.Logout(token);Check(core.Session(token)==null,"Logout revokes its session");
native.RotateDuringVerify=true;Fault(()=>core.Login("Portal Smoke",native.Password,"twenty"),401,"Login detects password rotation during verification");native.RotateDuringVerify=false;
var rateCore=new Accounts(new FixtureIdentity(),new MemoryState(),key);for(int i=0;i<10;i++)Fault(()=>rateCore.Login("Portal Smoke","wrong","same"),401,"Bounded failed attempt "+i);Fault(()=>rateCore.Login("Portal Smoke","wrong","same"),429,"Persisted rate-limit threshold enforced");
var partialIdentity=new FixtureIdentity();var partialStore=new MemoryState();var partialCore=new Accounts(partialIdentity,partialStore,key);
var partialSession=partialCore.Login("Portal Smoke",partialIdentity.Password,"partial");var partialCodes=partialCore.Codes(partialSession,partialIdentity.Password,"partial");partialStore.FailRevoke=true;
try{partialCore.ChangePassword(partialSession,partialIdentity.Password,"NewPassword123!","partial");throw new Exception("Expected partial failure");}catch(IOException){}
Check(partialCore.Session(partialSession)==null,"Native rotation invalidates session even when metadata cleanup fails");
Fault(()=>partialCore.Reset("Portal Smoke",partialCodes[0],"OtherPass123!","partial"),401,"Old recovery codes fail after partial cleanup");
// Test the actual HTTP app, cookies, Origin, CSRF, strict JSON and ownership boundaries.
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();string origin="http://127.0.0.1:"+port;
var options=new PortalOptions(origin,null,origin,true,"unused","unused",new());
await using var app=PortalApp.Build(options,new Accounts(new FixtureIdentity(),new MemoryState(),key));await app.StartAsync();
using var handler=new HttpClientHandler{CookieContainer=new CookieContainer(),AllowAutoRedirect=false};using var http=new HttpClient(handler){BaseAddress=new Uri(origin)};
var initial=await http.GetFromJsonAsync<JsonElement>("/account/api/session");string csrf=initial.GetProperty("csrfToken").GetString()!;
async Task<HttpResponseMessage> Post(string path,object value,string? from="default",string? nonce="default")
{
 if(from=="default")from=origin;if(nonce=="default")nonce=csrf;
 var request=new HttpRequestMessage(HttpMethod.Post,"/account/api/"+path){Content=JsonContent.Create(value)};
 if(from!=null)request.Headers.Add("Origin",from);if(nonce!=null)request.Headers.Add("X-MutSea-CSRF",nonce);return await http.SendAsync(request);
}
var result=await Post("login",new{name="Portal Smoke",password="OldPassword123!"},"https://evil.invalid");Check(result.StatusCode==HttpStatusCode.Forbidden,"Cross-origin login rejected");
result=await Post("login",new{name="Portal Smoke",password="OldPassword123!"},null);Check(result.StatusCode==HttpStatusCode.Forbidden,"Missing Origin rejected");
result=await Post("login",new{name="Portal Smoke",password="OldPassword123!"},origin,null);Check(result.StatusCode==HttpStatusCode.Forbidden,"Missing CSRF rejected");
result=await Post("login",new{name="Portal Smoke",password="OldPassword123!",user_id=Guid.NewGuid()});Check(result.StatusCode==HttpStatusCode.BadRequest,"Client-supplied identity rejected");
result=await Post("login",new{name="Portal Smoke",password="OldPassword123!"});Check(result.IsSuccessStatusCode,"Actual HTTP sign-in succeeds");
var login=await result.Content.ReadFromJsonAsync<JsonElement>();string rotated=login.GetProperty("csrfToken").GetString()!;Check(rotated!=csrf,"CSRF rotates on sign-in");
Check(result.Headers.GetValues("Set-Cookie").Any(c=>c.Contains("httponly",StringComparison.OrdinalIgnoreCase)&&c.Contains("samesite=strict",StringComparison.OrdinalIgnoreCase)),"Session cookie is HttpOnly and SameSite Strict");
Check(!login.TryGetProperty("token",out _)&&!login.TryGetProperty("stamp",out _),"No authentication secret in JSON response");
result=await Post("profile",new{about="x",contactEmail="",language="en",version=0});Check(result.StatusCode==HttpStatusCode.Forbidden,"Pre-login CSRF cannot mutate an authenticated session");
result=await Post("profile",new{about="x",contactEmail="",language="en",version=0,role="admin"},origin,rotated);Check(result.StatusCode==HttpStatusCode.BadRequest,"Privilege/identity overposting rejected");
result=await Post("profile",new{about="x",contactEmail="",language="en",version=0},origin,rotated);Check(result.IsSuccessStatusCode,"Own profile updated through actual HTTP app");
Check(result.Headers.CacheControl?.NoStore==true,"Account responses prohibit caching");
result=await Post("recovery-codes",new{password="OldPassword123!"},origin,rotated);Check(result.IsSuccessStatusCode,"HTTP recovery generation reauthenticates");
var recovery=await result.Content.ReadFromJsonAsync<JsonElement>();var httpCode=recovery.GetProperty("codes")[0].GetString()!;
result=await Post("reset",new{name="Portal Smoke",code=httpCode,newPassword="NewPassword123!"},origin,rotated);Check(result.IsSuccessStatusCode,"HTTP recovery resets the world password");
var reset=await result.Content.ReadFromJsonAsync<JsonElement>();csrf=reset.GetProperty("csrfToken").GetString()!;
Check(!handler.CookieContainer.GetCookies(new Uri(origin)).Cast<Cookie>().Any(c=>c.Name=="mutsea-dev-session"),"Password reset expires the browser cookie");
result=await Post("login",new{name="Portal Smoke",password="NewPassword123!"});Check(result.IsSuccessStatusCode,"HTTP login accepts reset credential");
login=await result.Content.ReadFromJsonAsync<JsonElement>();rotated=login.GetProperty("csrfToken").GetString()!;
result=await Post("password",new{currentPassword="NewPassword123!",newPassword="OtherPass123!"},origin,rotated);Check(result.IsSuccessStatusCode,"HTTP password change reauthenticates and commits");
var passwordResult=await result.Content.ReadFromJsonAsync<JsonElement>();csrf=passwordResult.GetProperty("csrfToken").GetString()!;
result=await Post("login",new{name="Portal Smoke",password="OtherPass123!"});Check(result.IsSuccessStatusCode,"HTTP login accepts changed credential");
login=await result.Content.ReadFromJsonAsync<JsonElement>();rotated=login.GetProperty("csrfToken").GetString()!;
result=await Post("logout",new{},origin,rotated);Check(result.IsSuccessStatusCode,"Actual HTTP logout succeeds");
Check(!handler.CookieContainer.GetCookies(new Uri(origin)).Cast<Cookie>().Any(c=>c.Name=="mutsea-dev-session"),"Logout removes the browser cookie");
var last=await http.GetFromJsonAsync<JsonElement>("/account/api/session");Check(!last.GetProperty("authenticated").GetBoolean(),"Logged-out browser is anonymous");
csrf=last.GetProperty("csrfToken").GetString()!;
result=await Post("register",new{firstName="Ada",lastName="Meadow",password="StarterPass123!",email="",avatar="auren"});
Check(result.StatusCode==HttpStatusCode.ServiceUnavailable,"Registration stays closed without an explicitly configured native worker");
await app.StopAsync();
var registrationIdentity=new FixtureIdentity{Name="Ada Meadow",Password="StarterPass123!",Active=false};
var registrationFixture=new FixtureRegistrations();
await using var join=PortalApp.Build(options,new Accounts(registrationIdentity,new MemoryState(),key),registrations:registrationFixture);await join.StartAsync();
using var joinHandler=new HttpClientHandler{CookieContainer=new CookieContainer(),AllowAutoRedirect=false};
using var joinHttp=new HttpClient(joinHandler){BaseAddress=new Uri(origin)};
var bootstrap=await joinHttp.GetFromJsonAsync<JsonElement>("/account/api/session");string joinCsrf=bootstrap.GetProperty("csrfToken").GetString()!;
async Task<HttpResponseMessage> JoinPost(string operation,object body,string? from="default",string? nonce="default")
{
 using var message=new HttpRequestMessage(HttpMethod.Post,"/account/api/"+operation){Content=JsonContent.Create(body)};
 if(from!=null)message.Headers.Add("Origin",from=="default"?origin:from);
 if(nonce!=null)message.Headers.Add("X-MutSea-CSRF",nonce=="default"?joinCsrf:nonce);
 return await joinHttp.SendAsync(message);
}
var signup=new{firstName="Ada",lastName="Meadow",password="StarterPass123!",email="",avatar="auren"};
result=await JoinPost("register",signup,"https://evil.invalid");Check(result.StatusCode==HttpStatusCode.Forbidden,"Cross-origin registration rejected");
result=await JoinPost("register",signup,origin,null);Check(result.StatusCode==HttpStatusCode.Forbidden,"Registration requires CSRF");
result=await JoinPost("register",new{signup.firstName,signup.lastName,signup.password,signup.email,signup.avatar,userLevel=240});Check(result.StatusCode==HttpStatusCode.BadRequest,"Registration rejects privilege fields");
result=await JoinPost("register",new{firstName=Guid.NewGuid().ToString(),lastName="Meadow",signup.password,signup.email,signup.avatar});Check(result.StatusCode==HttpStatusCode.BadRequest,"Avatar UUID is rejected as a registration name");
result=await JoinPost("register",new{signup.firstName,signup.lastName,password="far-too-long-for-the-viewer-password",signup.email,signup.avatar});Check(result.StatusCode==HttpStatusCode.BadRequest,"Registration retains viewer password length contract");
result=await JoinPost("register",signup);Check(result.StatusCode==HttpStatusCode.Accepted,"Registration queues native provisioning instead of pretending an identity is ready");
Check(result.Headers.GetValues("Set-Cookie").Any(v=>v.StartsWith("mutsea-dev-registration=")&&v.Contains("httponly",StringComparison.OrdinalIgnoreCase)),"Registration receipt is an HttpOnly cookie");
var queued=await result.Content.ReadFromJsonAsync<JsonElement>();Check(queued.GetProperty("registration").GetProperty("state").GetString()=="queued","New registration reports queued state");
result=await JoinPost("login",new{name="Ada Meadow",password="StarterPass123!"});Check(result.StatusCode==HttpStatusCode.Unauthorized,"A pending account cannot sign in");
using(var stranger=new HttpClient{BaseAddress=new Uri(origin)})
{
 var hidden=await stranger.GetFromJsonAsync<JsonElement>("/account/api/registration-status?ticket=untrusted");Check(hidden.GetProperty("registration").ValueKind==JsonValueKind.Null,"Anonymous status cannot inspect another registration");
}
registrationFixture.Complete=true;registrationIdentity.Active=true;
var complete=await joinHttp.GetFromJsonAsync<JsonElement>("/account/api/registration-status");Check(complete.GetProperty("registration").GetProperty("name").GetString()=="Ada Meadow"&&complete.GetProperty("registration").GetProperty("state").GetString()=="ready","Receipt sees its own completed avatar name");
result=await JoinPost("login",new{name="Ada Meadow",password="StarterPass123!"});Check(result.IsSuccessStatusCode,"Newly activated native identity uses ordinary account sign-in");
await join.StopAsync();Console.WriteLine($"{passed} account security and HTTP checks passed.");

sealed class FixtureIdentity:IWorldIdentity
{
 public string Name="Portal Smoke";public Guid Id=Guid.NewGuid();public string Password="OldPassword123!";public bool Active=true,RotateDuringVerify;int generation;
 public WorldAccount? Find(string name)=>name==Name&&Active?Get(Id):null;
 public WorldAccount? Get(Guid id)=>id==Id&&Active?new(id,Name,"",1700000000,null,null,false,null):null;
 public bool Verify(Guid id,string password){bool yes=id==Id&&password==Password;if(RotateDuringVerify)generation++;return yes;}
 public string? CredentialStamp(Guid id)=>id==Id?"stamp-"+generation:null;
 public bool SetPassword(Guid id,string password){if(id!=Id)return false;Password=password;generation++;return true;}
 public void ExternalRotation()=>generation++;
}
sealed class MemoryState:IPortalState
{
 public bool FailRevoke;
 public readonly ConcurrentDictionary<string,SessionRow> Sessions=new();public readonly ConcurrentDictionary<string,string> Codes=new();
 readonly ConcurrentDictionary<Guid,ProfileRow> profiles=new();readonly ConcurrentDictionary<string,int> limits=new();readonly ConcurrentDictionary<Guid,object> gates=new();
 public bool Limit(string key,int max,DateTimeOffset now)=>limits.AddOrUpdate(key+":"+(now.ToUnixTimeSeconds()/900),1,(_,n)=>n+1)<=max;
 public void AddSession(SessionRow r)=>Sessions[r.Hash]=r;
 public SessionRow? Session(string h)=>Sessions.GetValueOrDefault(h);
 public void Touch(string h,DateTimeOffset now){if(Sessions.TryGetValue(h,out var r))Sessions[h]=r with{Seen=now};}
 public void RemoveSession(string h)=>Sessions.TryRemove(h,out _);
 public void Revoke(Guid user){if(FailRevoke)throw new IOException("Fixture failure");foreach(var s in Sessions.Values.Where(s=>s.User==user))RemoveSession(s.Hash);foreach(var h in Codes.Keys.Where(k=>k.StartsWith(user+":")))Codes.TryRemove(h,out _);}
 public ProfileRow Profile(Guid user)=>profiles.GetValueOrDefault(user)??new("","","en",0);
 public bool SaveProfile(Guid u,ProfileRow p){if(Profile(u).Version!=p.Version)return false;profiles[u]=p with{Version=p.Version+1};return true;}
 public void ReplaceCodes(Guid u,string stamp,string[] hashes,DateTimeOffset now){foreach(var k in Codes.Keys.Where(k=>k.StartsWith(u+":")))Codes.TryRemove(k,out _);foreach(var h in hashes)Codes[u+":"+h]=stamp;}
 public bool Code(Guid u,string stamp,string hash)=>Codes.GetValueOrDefault(u+":"+hash)==stamp;
 public int CodeCount(Guid u,string stamp)=>Codes.Count(p=>p.Key.StartsWith(u+":")&&p.Value==stamp);
 public T Locked<T>(Guid u,Func<T> a){lock(gates.GetOrAdd(u,_=>new object()))return a();}
 public void Audit(Guid? u,string kind,DateTimeOffset now){}
}

sealed class FixtureRegistrations:IRegistrations
{
 readonly string ticket=Accounts.RandomToken();public bool Complete;
 public object Options()=>new{enabled=true,avatars=new[]{new{id="auren",title="Auren"}}};
 public string Register(string first,string last,string password,string email,string avatar,string ip,string? previous)
 {
  PgRegistrations.Validate(first,last,password,email);
  if(avatar!="auren")throw new RegistrationFault(400,"Unavailable starter.");
  return ticket;
 }
 public RegistrationStatus? Status(string? value)=>value==ticket?new(Complete?"ready":"queued","Ada Meadow",null):null;
}
