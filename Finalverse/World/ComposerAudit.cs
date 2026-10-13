// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;

namespace Finalverse.World;

// A small human-facing view over the two existing journals. No new authority
// or duplicate persistence: exact plan IDs still go to their original executor.
public static class ComposerAudit
{
    public static JsonElement[] History(IEnumerable<Entry> actions,IEnumerable<PlaceEntry> places,string actor)
    {
        var normal=actions.Where(e=>e.actor_id==actor).Select(e=>JsonSerializer.SerializeToElement(e,Kernel.Json));
        var composed=places.Where(e=>e.plan.actor_id==actor).Select(e=>JsonSerializer.SerializeToElement(new {
            e.operation_id,plan_id=e.plan.plan_id,actor_id=actor,e.timestamp,
            status=e.status=="rolled_back"?"undone":e.status,
            proposal=new {request=e.plan.place.title},kind="place",e.error
        },Kernel.Json));
        return normal.Concat(composed).OrderBy(e=>e.GetProperty("timestamp").GetDateTimeOffset()).TakeLast(100).ToArray();
    }

    public static string? LatestPlace(IEnumerable<PlaceEntry> entries,string actor,DateTimeOffset after)
    {
        var all=entries.Where(e=>e.plan.actor_id==actor).ToArray();
        var rolledBack=all.Where(e=>e.status=="rolled_back").Select(e=>e.plan.plan_id).ToHashSet();
        return all.Where(e=>e.status=="committed"&&e.timestamp>after&&!rolledBack.Contains(e.plan.plan_id))
            .OrderBy(e=>e.timestamp).LastOrDefault()?.plan.plan_id;
    }
}
