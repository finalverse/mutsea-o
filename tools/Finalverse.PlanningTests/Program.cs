// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
using System.Text.Json;
int passed=0;
void Check(bool value){if(!value)throw new Exception("Assertion failed");passed++;}
void Reject(string json){try{ServerPlanner.Recipe(JsonDocument.Parse(json).RootElement);}catch(InvalidDataException){passed++;return;}throw new Exception("Expected rejection");}
Check(!ServerPlanner.Recipe(JsonDocument.Parse("{\"op\":\"plan\",\"request\":\"What is nearby?\"}").RootElement));
Check(ServerPlanner.Recipe(JsonDocument.Parse("{\"op\":\"plan\",\"world_spec\":{}}").RootElement));
Reject("{\"op\":\"plan\",\"request\":\"test\",\"world_context\":{}}");
Reject("{\"op\":\"plan\",\"request\":\"test\",\"actor_id\":\"forged\"}");
Reject("{\"op\":\"plan\",\"request\":\"\"}");
Reject("{\"op\":\"plan\",\"request\":3}");
Reject("{\"op\":\"plan\",\"request\":\"a\",\"request\":\"b\"}");
Reject("{\"op\":\"plan\",\"request\":\"test\",\"world_spec\":{}}");
Reject("{\"op\":\"plan\",\"request\":\""+new string('x',2049)+"\"}");
var entity=new{id="id",name="safe name",shape="box",position=new[]{1,2,3},scale=new[]{1,1,1},editable=true,owner_id="secret",description="secret"};
var context=ServerPlanner.Context(new{region=new{id="region",bounds=new[]{256,256},name="secret"},avatar=new{position=new[]{1,2,3},session_id="secret"},terrain=new{height=2},selected_object=entity,nearby_entities=Enumerable.Range(0,70).Select(_=>new{entity,distance=1,semantic="secret"}),lumi="secret",undoable_plan_id="secret"});
Check(!context.ToJsonString().Contains("secret"));Check(context["nearby_entities"]!.AsArray().Count==64);Check(context["selected_object"]!["name"]!.GetValue<string>()=="safe name");
Console.WriteLine($"{passed} planning boundary checks passed.");
