// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using OpenMetaverse;
using MutSea.Region.Framework.Scenes;

namespace Finalverse.World;

// Native persistence can normalize signed zero and link numbering. Compare
// canonical content, including native material/light parameters and sit targets.
public static class ContentFingerprint
{
    static double Number(double value) => value == 0 ? 0 : value;
    static double[] Vector(Vector3 v, bool legacy) => legacy
        ? [v.X, v.Y, v.Z] : [Number(v.X), Number(v.Y), Number(v.Z)];

    static byte[] Snapshot(SceneObjectGroup group, bool legacy, bool signedChildZero = false)
    {
        var parts = legacy ? group.Parts.OrderBy(p => p.LinkNum)
            : group.Parts.OrderBy(p => p.UUID.ToString(), StringComparer.Ordinal);
        var view = parts.Select(p =>
        {
            float[] rotation = [p.RotationOffset.X, p.RotationOffset.Y,
                p.RotationOffset.Z, p.RotationOffset.W];
            for (int i = 0; i < rotation.Length; i++)
                if (rotation[i] == 0)
                    rotation[i] = legacy ? (signedChildZero && p != group.RootPart && i < 3
                        ? MathF.CopySign(0, -1) : rotation[i]) : 0;
            return new { id = p.UUID.ToString(), owner = p.OwnerID.ToString(),
                position = Vector(p.AbsolutePosition, legacy), offset = Vector(p.OffsetPosition, legacy),
                scale = Vector(p.Scale, legacy), rotation, mesh = p.Shape.SculptTexture.ToString(),
                p.Name, p.Description, texture = Convert.ToBase64String(p.Shape.TextureEntry),
                p.BaseMask, p.OwnerMask, p.GroupMask, p.EveryoneMask, p.NextOwnerMask,
                p.Flags, p.PhysicsShapeType, group.IsAttachment, group.UsesPhysics,
                inventory = p.Inventory.GetInventoryItems().Count };
        });
        return JsonSerializer.SerializeToUtf8Bytes(view);
    }

    static byte[] SnapshotV2(SceneObjectGroup group)=>JsonSerializer.SerializeToUtf8Bytes(new {
        baseline=JsonSerializer.Deserialize<JsonElement>(Snapshot(group,false)),
        native=group.Parts.OrderBy(p=>p.UUID.ToString(),StringComparer.Ordinal).Select(p=>new {
            id=p.UUID.ToString(),extra=Convert.ToBase64String(p.Shape.ExtraParamsToBytes()),
            overrides=Convert.ToBase64String(p.Shape.RenderMaterialsOvrToRawBin()??Array.Empty<byte>()),
            sit_position=Vector(p.SitTargetPosition,false),
            sit_rotation=new[]{Number(p.SitTargetOrientation.X),Number(p.SitTargetOrientation.Y),Number(p.SitTargetOrientation.Z),Number(p.SitTargetOrientation.W)} }) });
    public static string Hash(SceneObjectGroup group) => "v2:" + ContentCatalog.Hash(SnapshotV2(group));

    public static bool Matches(SceneObjectGroup group, string fingerprint)
    {
        if (fingerprint.StartsWith("v2:", StringComparison.Ordinal))
            return Hash(group) == fingerprint;
        // Historical imports had neither PBR bindings nor explicit sit targets.
        // New edits in these channels must not bypass an old receipt's guard.
        if(group.Parts.Any(p=>p.IsSitTargetSet||p.Shape.LightEntry||
            p.Shape.RenderMaterials?.entries is {Length:>0}||p.Shape.RenderMaterials?.overrides is {Length:>0}))return false;
        if (fingerprint.StartsWith("v1:", StringComparison.Ordinal))
            return "v1:"+ContentCatalog.Hash(Snapshot(group,false)) == fingerprint;
        // First development receipts used raw JSON. Their only accepted alternate
        // representation is linked-child quaternion signed zero, verified from
        // native before/after SQLite snapshots. No other field is relaxed.
        return ContentCatalog.Hash(Snapshot(group, true)) == fingerprint
            || ContentCatalog.Hash(Snapshot(group, true, true)) == fingerprint;
    }
}
