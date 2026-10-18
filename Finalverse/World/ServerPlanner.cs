// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Finalverse.World;

// Proposal transport only. Authentication and context are supplied by WorldModule;
// all actions still pass the existing prepare/approval/commit policy and WorldLine.
public sealed class ServerPlanner
{
    readonly HttpClient http;
    static readonly SemaphoreSlim capacity = new(2);
    public ServerPlanner(string socketPath)
    {
        http=PrivateServiceHttp.Client(socketPath,TimeSpan.FromSeconds(45));
    }
    public static bool Recipe(JsonElement data)
    {
        if(data.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Expected a planning object.");
        var fields=data.EnumerateObject().Select(x=>x.Name).ToArray();
        bool recipe=data.TryGetProperty("world_spec",out _);
        var allowed=recipe?new[]{"op","world_spec"}:new[]{"op","request","selected_id"};
        if(fields.Distinct().Count()!=fields.Length||fields.Any(x=>!allowed.Contains(x)))throw new InvalidDataException("Planning accepts a request or recipe; scene context comes from the server.");
        if(recipe){if(data.GetProperty("world_spec").ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Expected a recipe.");}
        else if(!data.TryGetProperty("request",out var r)||r.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(r.GetString())||r.GetString()!.Length>2048)throw new InvalidDataException("Enter a request of up to 2048 characters.");
        return recipe;
    }
    public static JsonObject Context(object inspection)
    {
        var source=JsonSerializer.SerializeToNode(inspection)!.AsObject();
        JsonNode? Copy(JsonNode? value)=>value?.DeepClone();
        JsonObject Entity(JsonNode value)
        {
            var result=new JsonObject();
            foreach(var field in new[]{"id","name","shape","position","scale","editable"})result[field]=Copy(value[field]);
            return result;
        }
        var context=new JsonObject{
            ["region"]=new JsonObject{["id"]=Copy(source["region"]!["id"]),["bounds"]=Copy(source["region"]!["bounds"])},
            ["avatar"]=new JsonObject{["position"]=Copy(source["avatar"]!["position"])},
            ["terrain"]=new JsonObject{["height"]=Copy(source["terrain"]!["height"])}
        };
        if(source["selected_object"] is {} selected)context["selected_object"]=Entity(selected);
        var nearby=new JsonArray();
        foreach(var item in source["nearby_entities"]!.AsArray().Take(64))
            nearby.Add(new JsonObject{["entity"]=Entity(item!["entity"]!),["distance"]=Copy(item["distance"])});
        context["nearby_entities"]=nearby;
        if(source["workspace"] is JsonObject workspace&&workspace["zones"] is JsonArray spatial)
        {
            var zones=new JsonArray();
            foreach(var zone in spatial.Take(8))
            {
                if(zone is not JsonObject)continue;
                var value=new JsonObject();foreach(var key in new[]{"id","title","purpose","center","radius"})value[key]=Copy(zone[key]);zones.Add(value);
            }
            context["workspace"]=new JsonObject{["id"]=Copy(workspace["id"]),["title"]=Copy(workspace["title"]),["zones"]=zones};
        }
        return context;
    }
    public JsonObject Propose(JsonElement data,JsonObject? context)
    {
        bool recipe=Recipe(data);
        var payload=recipe?new JsonObject{["world_spec"]=JsonNode.Parse(data.GetProperty("world_spec").GetRawText())}:
            new JsonObject{["request"]=data.GetProperty("request").GetString(),["world_context"]=context};
        byte[] bytes=Encoding.UTF8.GetBytes(payload.ToJsonString());
        if(bytes.Length>32768)throw new InvalidDataException("Planning context exceeds the supported scope.");
        if(!capacity.Wait(0))throw new InvalidDataException("The planner is busy. Please try shortly.");
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"http://planner"+(recipe?"/v1/world/spec/plan":"/v1/world/plan"));
            request.Content=new ByteArrayContent(bytes);request.Content.Headers.ContentType=new("application/json");
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var reply=http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).GetAwaiter().GetResult();
            reply.EnsureSuccessStatusCode();
            using var input=reply.Content.ReadAsStream(deadline.Token);using var output=new MemoryStream();var buffer=new byte[4096];int count;
            while((count=input.ReadAsync(buffer,deadline.Token).AsTask().GetAwaiter().GetResult())>0){output.Write(buffer,0,count);if(output.Length>131072)throw new IOException("Planner reply exceeds limit.");}
            var result=JsonNode.Parse(output.ToArray(),documentOptions:new(){MaxDepth=24})?.AsObject()??throw new IOException();
            if(result["answer"] is not JsonValue answer||!answer.TryGetValue<string>(out _)||result.ContainsKey("error"))throw new IOException();
            return result;
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
        {throw new InvalidDataException("The planner is unavailable or returned an invalid proposal. No world changes were made.");}
        finally{capacity.Release();}
    }
}
