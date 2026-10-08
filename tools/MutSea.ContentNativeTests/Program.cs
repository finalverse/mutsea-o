// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Scenes.Serialization;

int passed = 0;
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
void Assert(bool ok) { if (!ok) throw new Exception("Assertion failed."); }
var owner = UUID.Random();
SceneObjectPart Part(string name) => new(owner, PrimitiveBaseShape.CreateMesh(1, UUID.Random()),
    new Vector3(10, 10, 25), Quaternion.Identity, Vector3.Zero) { Name = name };
var root = Part("root"); root.LocalId = 1;
var group = new SceneObjectGroup(root);
var child = Part("child"); child.OffsetPosition = new Vector3(0, 0, 1);
child.RotationOffset = Quaternion.Identity * Quaternion.Inverse(Quaternion.Identity);
group.AddPart(child); child.ParentID = root.LocalId;
var hash = ContentFingerprint.Hash(group);
Test("signed zero from native quaternion math survives persistence", () => {
    child.RotationOffset = Quaternion.Identity;
    Assert(ContentFingerprint.Hash(group) == hash);
});
Test("native XML persistence preserves canonical fingerprint", () => {
    var restored = SceneObjectSerializer.FromXml2Format(SceneObjectSerializer.ToXml2Format(group));
    Assert(restored != null && ContentFingerprint.Hash(restored) == hash);
});
Test("link numbering does not change persisted content identity", () => {
    child.LinkNum = 9; Assert(ContentFingerprint.Hash(group) == hash); child.LinkNum = 2;
});
Test("later transform edits block undo", () => {
    child.OffsetPosition = new Vector3(0, 0, 1.01f);
    Assert(!ContentFingerprint.Matches(group, hash)); child.OffsetPosition = new Vector3(0, 0, 1);
});
Test("later permissions and ownership edits block undo", () => {
    var mask = child.OwnerMask; child.OwnerMask = 0; Assert(!ContentFingerprint.Matches(group, hash)); child.OwnerMask = mask;
    child.OwnerID = UUID.Random(); Assert(!ContentFingerprint.Matches(group, hash)); child.OwnerID = owner;
});
Test("later mesh and material edits block undo", () => {
    var mesh = child.Shape.SculptTexture; child.Shape.SculptTexture = UUID.Random(); Assert(!ContentFingerprint.Matches(group, hash)); child.Shape.SculptTexture = mesh;
    var texture = child.Shape.TextureEntry;
    var te = new Primitive.TextureEntry(Primitive.TextureEntry.WHITE_TEXTURE); te.DefaultTexture.RGBA = new Color4(.2f, .3f, .4f, 1);
    child.Shape.TextureEntry = te.GetBytes(); Assert(!ContentFingerprint.Matches(group, hash)); child.Shape.TextureEntry = texture;
});
Console.WriteLine($"{passed} native content fingerprint tests passed");
