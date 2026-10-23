// Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using OpenMetaverse;
namespace Finalverse.World;

public sealed partial class WorldModule
{
    // Presentation connection only. Canonical conversations and grants live in Earth.
    readonly Dictionary<UUID,(Guid nativeSession,string grant)> confluenceLinks=new();
    object HumanConfluence(JsonElement data,UUID agent,Guid session)
    {
        string action=data.GetProperty("action").GetString()??"";
        void Active()=>ContentCatalog.Require(HasWorldSession(agent,session),"Your world session changed; reconnect explicitly.");
        lock(gate) {
            Active();ContentCatalog.Require(earthBridge!=null,"Confluence continuity is not enabled here.");
            ContentCatalog.Require(action is "join" or "read" or "message" or "artifact" or "leave","Unsupported Confluence operation.");
            if(action=="read"&&lastRequest.TryGetValue(agent,out var recent))ContentCatalog.Require(DateTimeOffset.UtcNow-recent>=TimeSpan.FromMilliseconds(100),"Please wait briefly.");
            lastRequest[agent]=DateTimeOffset.UtcNow;
            ContentCatalog.Require(earthBusy.Count<4&&earthBusy.Add(agent),"A spatial operation is already running.");
        }
        try {
            var audience=new{grid_id=earthGrid,region_id=RegionId};
            if(action=="leave") {lock(gate){Active();confluenceLinks.Remove(agent);}return new{disconnected=true,server_grant_revoked=false};}
            JsonElement result;
            if(action=="join") {
                string token=data.GetProperty("handoff").GetString()??"";
                ContentCatalog.Require(token.Length==64&&token.All(Uri.IsHexDigit),"Paste a valid scoped Confluence handoff.");
                result=EarthCall("/v1/world/confluence/join",new{token,agent_id=agent.ToString(),audience,native_session_id=session});
                lock(gate){Active();ContentCatalog.Require(confluenceLinks.Count<128||confluenceLinks.ContainsKey(agent),"Connection quota reached.");confluenceLinks[agent]=(session,result.GetProperty("grant_id").GetString()!);}
            } else {
                string grant;
                lock(gate){Active();ContentCatalog.Require(confluenceLinks.TryGetValue(agent,out var link)&&link.nativeSession==session,"Connect this avatar to an explicitly shared session first.");grant=confluenceLinks[agent].grant;}
                long after=data.TryGetProperty("after",out var cursor)?cursor.GetInt64():0;
                ContentCatalog.Require(after>=0&&after<=4096,"Invalid replay cursor.");
                object? message=null;string? artifact=null;
                if(action=="message") {
                    string text=data.GetProperty("text").GetString()??"";
                    ContentCatalog.Require(text.Length is >0 and <=2048&&Guid.TryParse(data.GetProperty("request_id").GetString(),out _),"Expected a bounded message and request UUID.");
                    message=new{request_id=data.GetProperty("request_id").GetString(),text};
                    if(data.TryGetProperty("artifact_id",out var context)){artifact=context.GetString();ContentCatalog.Require(Guid.TryParse(artifact,out _),"Invalid artifact context.");}
                }
                if(action=="artifact") {artifact=data.GetProperty("artifact_id").GetString();ContentCatalog.Require(Guid.TryParse(artifact,out _),"Invalid artifact reference.");}
                result=EarthCall("/v1/world/confluence/"+action,new{grant_id=grant,agent_id=agent.ToString(),audience,native_session_id=session,after,artifact_id=artifact,message});
                lock(gate)Active();
            }
            return result;
        }
        finally{lock(gate)earthBusy.Remove(agent);}
    }
}
