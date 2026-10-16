// Copyright 2026 MutSea/Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
namespace MutSea.AccountPortal;
public sealed record PortalOptions(string Origin,string? Socket,string? Listen,bool Development,string DatabaseFile,string KeyFile,Dictionary<Guid,string> Regions)
{
    public static string ReadSecret(string file)
    {
        if(!Path.IsPathRooted(file)||!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Secret must be a regular absolute file.");
        if(!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(file)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute|UnixFileMode.GroupExecute))!=0)throw new InvalidDataException("Secret file must be owner-only.");
        return File.ReadAllText(file).Trim();
    }
    public void Validate()
    {
        var origin=new Uri(Origin);
        if(Origin!=origin.GetLeftPart(UriPartial.Authority)||origin.UserInfo!=""||(!Development&&(Origin!="https://mutsea.com"||Socket==null||Listen!=null)))throw new InvalidDataException("Invalid canonical account origin.");
        if(Development&&(origin.Scheme!="http"||origin.Host!="127.0.0.1"||Socket!=null||Listen!=Origin))throw new InvalidDataException("Development is restricted to an explicit loopback origin.");
        if(Socket!=null&&(!Path.IsPathRooted(Socket)||!OperatingSystem.IsLinux()))throw new InvalidDataException("Production requires a private Linux Unix socket.");
    }
}
public static class PortalApp
{
    public static WebApplication Build(PortalOptions options,Accounts core,string[]? args=null)
    {
        options.Validate();
        var builder=WebApplication.CreateBuilder(args??[]);builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k=>{
            k.AddServerHeader=false;k.Limits.MaxRequestBodySize=16384;k.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(10);k.Limits.MaxConcurrentConnections=64;
            if(options.Socket!=null)k.ListenUnixSocket(options.Socket);else k.Listen(IPAddress.Loopback,new Uri(options.Listen!).Port);
        });
        var app=builder.Build();var slots=new SemaphoreSlim(8);
        string sessionCookie=options.Development?"mutsea-dev-session":"__Host-mutsea-session";
        string csrfCookie=options.Development?"mutsea-dev-csrf":"__Host-mutsea-csrf";
        CookieOptions Cookie(TimeSpan? age=null)=>new(){HttpOnly=true,Secure=!options.Development,SameSite=SameSiteMode.Strict,Path="/",MaxAge=age??TimeSpan.FromHours(12),IsEssential=true};
        string Csrf(HttpContext c,bool renew=false)
        {
            string? token=c.Items[csrfCookie] as string??c.Request.Cookies[csrfCookie];
            if(renew||token==null||token.Length!=64||!token.All(Uri.IsHexDigit)){token=Accounts.RandomToken();c.Items[csrfCookie]=token;c.Response.Cookies.Append(csrfCookie,token,Cookie());}return token;
        }
        string? Token(HttpContext c)=>c.Request.Cookies[sessionCookie];
        void Clear(HttpContext c){c.Response.Cookies.Delete(sessionCookie,Cookie(TimeSpan.Zero));}
        object View(HttpContext c,string? token)
        {
            var s=core.Session(token);
            if(s==null){Clear(c);return new{authenticated=false,csrfToken=Csrf(c)};}
            return new{authenticated=true,csrfToken=Csrf(c),account=s.Account,profile=core.Profile(s),recoveryCodesRemaining=core.CodeCount(s)};
        }
        app.Use(async(c,next)=>{
            c.Response.Headers.CacheControl="no-store";c.Response.Headers.Pragma="no-cache";c.Response.Headers["X-Content-Type-Options"]="nosniff";c.Response.Headers["Referrer-Policy"]="no-referrer";
            if(c.Request.Host.Value!=new Uri(options.Origin).Authority){c.Response.StatusCode=400;return;}
            if(!await slots.WaitAsync(TimeSpan.FromSeconds(3),c.RequestAborted)){c.Response.StatusCode=503;return;}
            var watch=Stopwatch.StartNew();
            bool delay=c.Request.Path.Value is "/account/api/login" or "/account/api/reset";
            Stream output=c.Response.Body;using var buffered=new MemoryStream();
            if(delay)c.Response.Body=buffered;
            try
            {
                if(c.Request.Method!="GET")
                {
                    if(c.Request.Method!="POST")throw new PortalFault(405,"Method not allowed.");
                    string origin=c.Request.Headers.Origin.ToString();
                    if(origin!=options.Origin||c.Request.Headers["X-MutSea-CSRF"].ToString().Length!=64||!Accounts.Equal(c.Request.Headers["X-MutSea-CSRF"].ToString(),c.Request.Cookies[csrfCookie]))throw new PortalFault(403,"Reload this page before submitting the form.");
                    if(c.Request.ContentType?.Split(';')[0].Trim()!="application/json")throw new PortalFault(415,"Expected JSON.");
                }
                await next(c);
            }
            catch(PortalFault f){c.Response.StatusCode=f.Status;if(f.Status==429)c.Response.Headers.RetryAfter="900";await c.Response.WriteAsJsonAsync(new{error=f.Message});}
            catch(JsonException){c.Response.StatusCode=400;await c.Response.WriteAsJsonAsync(new{error="Invalid request."});}
            catch(Microsoft.AspNetCore.Http.BadHttpRequestException){c.Response.StatusCode=400;await c.Response.WriteAsJsonAsync(new{error="Invalid request."});}
            catch(Exception e){Console.Error.WriteLine("Account request failed: "+e.GetType().Name);c.Response.StatusCode=503;await c.Response.WriteAsJsonAsync(new{error="Account service is temporarily unavailable. No automatic retry was made."});}
            finally
            {
                slots.Release();
                if(delay)
                {
                    c.Response.Body=output;
                    int remaining=350-(int)watch.ElapsedMilliseconds;if(remaining>0)await Task.Delay(remaining);
                    buffered.Position=0;await buffered.CopyToAsync(output,c.RequestAborted);
                }
            }
        });
        app.MapGet("/account/api/session",(HttpContext c)=>Results.Json(View(c,Token(c))));
        app.MapPost("/account/api/{operation}",async(HttpContext c,string operation)=>{
            using var d=await JsonDocument.ParseAsync(c.Request.Body,new JsonDocumentOptions{MaxDepth=6},c.RequestAborted);
            if(d.RootElement.ValueKind!=JsonValueKind.Object)throw new PortalFault(400,"Invalid request.");
            var data=d.RootElement;
            void Fields(params string[] names){if(data.EnumerateObject().Any(p=>!names.Contains(p.Name))||data.EnumerateObject().GroupBy(p=>p.Name).Any(g=>g.Count()>1))throw new PortalFault(400,"Unexpected fields.");}
            string Text(string name,int max=256){if(!data.TryGetProperty(name,out var v)||v.ValueKind!=JsonValueKind.String||v.GetString() is not {} s||s.Length>max)throw new PortalFault(400,"Missing or invalid field.");return s;}
            string Ip()
            {
                string? ip=options.Socket==null?c.Connection.RemoteIpAddress?.ToString():c.Request.Headers["X-Real-IP"].ToString();
                return IPAddress.TryParse(ip,out var parsed)?parsed.ToString():throw new PortalFault(400,"Invalid request source.");
            }
            switch(operation)
            {
                case "login":
                    Fields("name","password");string token=core.Login(Text("name",130),Text("password"),Ip());c.Response.Cookies.Append(sessionCookie,token,Cookie());Csrf(c,true);return Results.Json(View(c,token));
                case "logout":Fields();core.Logout(Token(c));Clear(c);return Results.Json(new{authenticated=false,csrfToken=Csrf(c,true)});
                case "profile":
                    Fields("about","contactEmail","language","version");
                    if(!data.TryGetProperty("version",out var version)||!version.TryGetInt64(out long n))throw new PortalFault(400,"Invalid profile version.");
                    return Results.Json(new{profile=core.SaveProfile(Token(c),Text("about",500),Text("contactEmail",254),Text("language",10),n)});
                case "recovery-codes":Fields("password");return Results.Json(new{codes=core.Codes(Token(c),Text("password"),Ip())});
                case "password":Fields("currentPassword","newPassword");core.ChangePassword(Token(c),Text("currentPassword"),Text("newPassword"),Ip());Clear(c);return Results.Json(new{reauthenticate=true,csrfToken=Csrf(c,true)});
                case "reset":Fields("name","code","newPassword");core.Reset(Text("name",130),Text("code",80),Text("newPassword"),Ip());Clear(c);return Results.Json(new{reauthenticate=true,csrfToken=Csrf(c,true)});
                default:throw new PortalFault(404,"Not found.");
            }
        });
        return app;
    }
}
