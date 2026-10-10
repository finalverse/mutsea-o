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

    def test_truncated_glb(self):
        def cut(b):
            del b[-64:]
            struct.pack_into("<I", b, 8, len(b))
        pkg, out = self.mutate(edit_glb=cut)
        self.expect_reject(pkg, out, "BIN chunk")


if __name__ == "__main__":
    unittest.main()
