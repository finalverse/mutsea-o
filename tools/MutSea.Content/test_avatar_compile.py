#!/usr/bin/env python3
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
"""Rejection tests for MutSea.AvatarCompile against a known-good package.

Environment: AVATAR_COMPILE_DLL, AVATAR_PACKAGE, VIEWER_SKELETON, DOTNET.
Each case mutates a private copy and expects a non-zero exit without output.
"""
import hashlib
import json
import os
import shutil
import struct
import subprocess
import tempfile
import unittest
from pathlib import Path


def env(name):
    value = os.environ.get(name)
    if not value:
        raise unittest.SkipTest(f"{name} not set")
    return value


class AvatarCompileRejections(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dll, cls.package, cls.skeleton = env("AVATAR_COMPILE_DLL"), Path(env("AVATAR_PACKAGE")), env("VIEWER_SKELETON")
        cls.dotnet = os.environ.get("DOTNET", "dotnet")

    def compile(self, package, out):
        run = subprocess.run([self.dotnet, self.dll, str(package), self.skeleton, str(out)], capture_output=True, text=True,
                             timeout=300, env={**os.environ, "DOTNET_ROLL_FORWARD": "Major"})
        return run.returncode, run.stdout + run.stderr

    def mutate(self, edit_manifest=None, edit_glb=None, rehash=True):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp)
        pkg = tmp / "pkg"
        shutil.copytree(self.package, pkg)
        manifest = json.loads((pkg / "package.json").read_text())
        if edit_glb:
            glb = bytearray((pkg / "runtime.glb").read_bytes())
            edit_glb(glb)
            (pkg / "runtime.glb").write_bytes(bytes(glb))
            if rehash:
                for f in manifest["files"]:
                    if f["path"] == "runtime.glb":
                        f["sha256"] = hashlib.sha256(glb).hexdigest()
        if edit_manifest:
            edit_manifest(manifest)
        (pkg / "package.json").write_text(json.dumps(manifest))
        return pkg, tmp / "out"

    def expect_reject(self, pkg, out, needle):
        code, text = self.compile(pkg, out)
        self.assertNotEqual(code, 0, text[-400:])
        self.assertIn(needle, text)
        self.assertFalse((out / "compiled.json").exists())

    def test_baseline_compiles(self):
        pkg, out = self.mutate()
        code, text = self.compile(pkg, out)
        self.assertEqual(code, 0, text[-400:])

    def test_existing_output_preserved(self):
        pkg, out = self.mutate()
        out.mkdir()
        self.expect_reject(pkg, out, "Preserve existing compiled packages")

    def test_tampered_interchange(self):
        pkg, out = self.mutate(edit_glb=lambda b: b.__setitem__(len(b) - 5, b[len(b) - 5] ^ 0xFF), rehash=False)
        self.expect_reject(pkg, out, "Payload hash changed")

    def test_wrong_skeleton_pin(self):
        pkg, out = self.mutate(lambda m: m["definition"]["skeleton"].__setitem__("sha256", "0" * 64))
        self.expect_reject(pkg, out, "skeleton pin")

    def test_selectable_rejected(self):
        pkg, out = self.mutate(lambda m: m["definition"].__setitem__("selectable", True))
        self.expect_reject(pkg, out, "non-selectable")

    def test_joint_list_mismatch(self):
        pkg, out = self.mutate(lambda m: m["definition"]["rig"]["joints"].reverse())
        self.expect_reject(pkg, out, "Skin joints differ")

    def test_joint_position_mismatch(self):
        def edit(m):
            m["definition"]["rig"]["joint_positions"]["mHead"][2] += 0.01
        pkg, out = self.mutate(edit)
        self.expect_reject(pkg, out, "Joint position differs")

    def test_lod_count_mismatch(self):
        pkg, out = self.mutate(lambda m: m["definition"]["lods"].__setitem__("LOD2", 1))
        self.expect_reject(pkg, out, "triangle count differs")

    def test_material_order_mismatch(self):
        pkg, out = self.mutate(lambda m: m["definition"]["materials"].reverse())
        self.expect_reject(pkg, out, "materials differ")

    def test_license_rejected(self):
        pkg, out = self.mutate(lambda m: m["license"].__setitem__("spdx", "CC-BY-4.0"))
        self.expect_reject(pkg, out, "CC0")

    def motion_case(self, edit, needle, edit_manifest=None):
        manifest = json.loads((self.package / "package.json").read_text())
        motions = manifest["definition"].get("animation_assets")
        if not motions:
            self.skipTest("package has no package motions")
        first = motions[0]["path"]

        def change(m):
            path = self._pkg / first
            data = bytearray(path.read_bytes())
            edit(data)
            path.write_bytes(bytes(data))
            for f in m["files"]:
                if f["path"] == first:
                    f["sha256"] = hashlib.sha256(data).hexdigest()
            if edit_manifest:
                edit_manifest(m)
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp)
        self._pkg = tmp / "pkg"
        shutil.copytree(self.package, self._pkg)
        m = json.loads((self._pkg / "package.json").read_text())
        change(m)
        (self._pkg / "package.json").write_text(json.dumps(m))
        self.expect_reject(self._pkg, tmp / "out", needle)

    def test_motion_emote_rejected(self):
        self.motion_case(lambda b: b.__setitem__(slice(12, 13), b"x\0"), "emotes")

    def test_motion_hand_pose_rejected(self):
        self.motion_case(lambda b: struct.pack_into("<I", b, 33, 1), "hand poses")

    def test_motion_trailing_bytes_rejected(self):
        self.motion_case(lambda b: b.extend(b"\0"), "trailing")

    def test_motion_constraints_rejected(self):
        self.motion_case(lambda b: struct.pack_into("<i", b, len(b) - 4, 1), "constraints")

    def test_motion_unknown_joint_rejected(self):
        def rename(b):
            i = b.find(b"mShoulderLeft\0")
            self.assertGreater(i, 0)
            b[i:i + 13] = b"mShoulderLefX"
        self.motion_case(rename, "Unknown or duplicate motion joint")

    def test_motion_metadata_mismatch_rejected(self):
        self.motion_case(lambda b: None, "Motion metadata differs",
                         lambda m: m["definition"]["animation_assets"][0].__setitem__("duration", 9.0))

    def worn(self):
        worn = os.environ.get("AVATAR_WORN_PACKAGE")
        if not worn:
            self.skipTest("AVATAR_WORN_PACKAGE not set")
        return Path(worn)

    def test_worn_avatar_compiles_with_alpha(self):
        source = self.worn()
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp)
        code, text = self.compile(source, tmp / "out")
        self.assertEqual(code, 0, text[-400:])
        compiled = json.loads((tmp / "out/compiled.json").read_text())
        self.assertEqual(compiled["representation"], "attachment")
        self.assertEqual(compiled["animations"], {})
        alpha = (tmp / "out" / compiled["alpha_asset_path"]).read_text()
        self.assertTrue(alpha.startswith("LLWearable version 22") and "type 13" in alpha)
        self.assertEqual(alpha.count("3a367d1c-bef1-6d43-7595-e88c1e3aadb3"), 5)

    def test_worn_avatar_rejects_unknown_alpha_region(self):
        source = self.worn()
        saved, self.package = self.package, source
        try:
            pkg, out = self.mutate(lambda m: m["definition"]["attachment"]["alpha"].append("tail"))
        finally:
            self.package = saved
        self.expect_reject(pkg, out, "alpha layer regions")

    def test_truncated_glb(self):
        def cut(b):
            del b[-64:]
            struct.pack_into("<I", b, 8, len(b))
        pkg, out = self.mutate(edit_glb=cut)
        self.expect_reject(pkg, out, "BIN chunk")


class AvatarSurfaceRejections(unittest.TestCase):
    compile = AvatarCompileRejections.compile
    expect_reject = AvatarCompileRejections.expect_reject

    @classmethod
    def setUpClass(cls):
        cls.dll,cls.package,cls.skeleton=env('AVATAR_COMPILE_DLL'),Path(env('AVATAR_SURFACE_PACKAGE')),env('VIEWER_SKELETON')
        cls.dotnet=os.environ.get('DOTNET','dotnet')

    def changed(self,edit_bytes=None,edit_manifest=None,edit_gltf=None,edit_png=None,rehash=True):
        tmp=Path(tempfile.mkdtemp());self.addCleanup(shutil.rmtree,tmp)
        pkg=tmp/'pkg';shutil.copytree(self.package,pkg)
        m=json.loads((pkg/'package.json').read_text())
        if edit_bytes:
            path=pkg/m['definition']['textures'][0]['path'];b=bytearray(path.read_bytes());edit_bytes(b);path.write_bytes(b)
            if rehash:
                next(f for f in m['files'] if f['path']==path.name)['sha256']=hashlib.sha256(b).hexdigest()
        if edit_png:
            path=pkg/m['definition']['textures'][0]['source_path'];b=bytearray(path.read_bytes());edit_png(b);path.write_bytes(b)
            next(f for f in m['files'] if f['path']==path.name)['sha256']=hashlib.sha256(b).hexdigest()
        if edit_gltf:
            path=pkg/'runtime.glb';data=path.read_bytes();length=struct.unpack_from('<I',data,12)[0]
            g=json.loads(data[20:20+length]);edit_gltf(g)
            js=json.dumps(g,separators=(',',':')).encode();js+=b' '*((-len(js))%4)
            tail=data[20+length:];glb=struct.pack('<III',0x46546c67,2,20+len(js)+len(tail))+struct.pack('<II',len(js),0x4e4f534a)+js+tail
            path.write_bytes(glb);next(f for f in m['files'] if f['path']=='runtime.glb')['sha256']=hashlib.sha256(glb).hexdigest()
        if edit_manifest:edit_manifest(m)
        (pkg/'package.json').write_text(json.dumps(m));return pkg,tmp/'out'

    def test_surface_baseline(self):
        p,out=self.changed();code,text=self.compile(p,out);self.assertEqual(code,0,text[-400:])
        a=json.loads((out/'compiled.json').read_text());self.assertEqual(a['schema_version'],3);self.assertEqual(len(a['textures']),3)
        self.assertTrue(all((out/t['asset_path']).is_file() for t in a['textures']))

    def test_texture_hash_changed(self):
        self.expect_reject(*self.changed(edit_bytes=lambda b:b.__setitem__(-3,b[-3]^1),rehash=False),'Payload hash changed')

    def test_texture_header_and_end_marker(self):
        for index,needle in [(0,'SOC/SIZ'),(-1,'end marker')]:
            self.expect_reject(*self.changed(edit_bytes=lambda b,i=index:b.__setitem__(i,0)),needle)

    def test_texture_dimensions_and_precision(self):
        self.expect_reject(*self.changed(edit_manifest=lambda m:m['definition']['textures'][0].__setitem__('width',512)),'dimensions differ')
        self.expect_reject(*self.changed(edit_bytes=lambda b:b.__setitem__(42,135)),'unsigned 8-bit RGB')

    def test_texture_bytes_bounded(self):
        self.expect_reject(*self.changed(edit_bytes=lambda b:b.extend(bytes((1<<20)+1-len(b)))),'Bounded JPEG2000')

    def test_authoring_png_is_bounded_rgb8(self):
        for index in [8,12,24,25,26,27,28]:
            self.expect_reject(*self.changed(edit_png=lambda b,i=index:b.__setitem__(i,b[i]^1)), 'authoring dimensions differ')
        self.expect_reject(*self.changed(edit_png=lambda b:b.extend(bytes((1<<20)+1-len(b)))), 'authoring dimensions differ')

    def test_unresolved_surface_binding(self):
        self.expect_reject(*self.changed(edit_manifest=lambda m:m['definition']['material_textures'].__setitem__('FV_Auren_Slate','missing')),'bindings do not close')

    def test_external_interchange_image_rejected(self):
        self.expect_reject(*self.changed(edit_gltf=lambda g:g['images'][0].__setitem__('uri','https://example.invalid/image.png')),'package-local PNG')

    def test_extra_image_rejected(self):
        self.expect_reject(*self.changed(edit_gltf=lambda g:g['images'].append({'uri':'unused.png'})),'exactly match')


if __name__ == "__main__":
    unittest.main()
