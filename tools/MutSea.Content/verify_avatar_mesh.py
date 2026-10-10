#!/usr/bin/env python3
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
"""Independent viewer-equivalent check of a compiled skinned avatar mesh.

Decodes `avatar.llmesh` following the viewer sources (llmeshrepository.h
LLMeshHeader, llmodel.cpp LLMeshSkinInfo, llvolume.cpp submesh/weights) and
re-skins the rest pose with the joint position overrides the viewer applies
(pelvis pinned to the root, SL-315). The result must reproduce the authored
glTF interchange within quantization tolerance. Read-only; prints JSON.
"""
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path

import numpy as np

MAX_BYTES = 4 << 20


class LLSDBinary:
    def __init__(self, data):
        self.data, self.at, self.depth = data, 0, 0

    def take(self, n):
        if self.at + n > len(self.data):
            raise ValueError("truncated LLSD")
        out = self.data[self.at:self.at + n]
        self.at += n
        return out

    def parse(self):
        self.depth += 1
        if self.depth > 16:
            raise ValueError("LLSD nesting too deep")
        tag = self.take(1)
        if tag == b"!":
            v = None
        elif tag in (b"1", b"0"):
            v = tag == b"1"
        elif tag == b"i":
            v = struct.unpack(">i", self.take(4))[0]
        elif tag == b"r":
            v = struct.unpack(">d", self.take(8))[0]
        elif tag == b"u":
            v = self.take(16).hex()
        elif tag in (b"b", b"s", b"l"):
            n = struct.unpack(">I", self.take(4))[0]
            raw = self.take(n)
            v = raw if tag == b"b" else raw.decode("utf-8")
        elif tag == b"d":
            v = struct.unpack("<d", self.take(8))[0]
        elif tag == b"[":
            n = struct.unpack(">I", self.take(4))[0]
            v = [self.parse() for _ in range(n)]
            if self.take(1) != b"]":
                raise ValueError("unterminated LLSD array")
        elif tag == b"{":
            n = struct.unpack(">I", self.take(4))[0]
            v = {}
            for _ in range(n):
                if self.take(1) != b"k":
                    raise ValueError("bad LLSD key")
                k = self.take(struct.unpack(">I", self.take(4))[0]).decode("utf-8")
                v[k] = self.parse()
            if self.take(1) != b"}":
                raise ValueError("unterminated LLSD map")
        else:
            raise ValueError(f"unsupported LLSD tag {tag!r}")
        self.depth -= 1
        return v


def block(data, header_size, entry):
    start = header_size + entry["offset"]
    if entry["offset"] < 0 or entry["size"] <= 0 or start + entry["size"] > len(data):
        raise ValueError("mesh block outside asset")
    raw = zlib.decompressobj().decompress(data[start:start + entry["size"]], 8 << 20)
    return LLSDBinary(raw).parse()


def matrix(values):
    # LLMatrix4 row-vector layout: mMatrix[j][k] = values[j*4+k].
    if len(values) != 16:
        raise ValueError("matrix must have 16 reals")
    return np.array(values, float).reshape(4, 4)


def u16(raw):
    return np.frombuffer(raw, "<u2").astype(float)


def decode_face(face):
    """llvolume.cpp unpackVolumeFacesInternal, including the weight loop."""
    pos = u16(face["Position"]).reshape(-1, 3)
    n = len(pos)
    lo, hi = np.array(face["PositionDomain"]["Min"]), np.array(face["PositionDomain"]["Max"])
    pos = pos / 65535.0 * (hi - lo) + lo
    nrm = u16(face["Normal"]).reshape(-1, 3) / 65535.0 * 2 - 1
    idx = np.frombuffer(face["TriangleList"], "<u2").astype(int)
    if len(idx) % 3 or len(idx) < 3 or idx.max() >= n or n > 65535:
        raise ValueError("invalid triangle list")
    w = face["Weights"]
    joints = np.zeros((n, 4), int)
    weights = np.zeros((n, 4))
    i = v = 0
    while i < len(w) and v < n:
        joint = w[i]
        i += 1
        k = 0
        while joint != 0xFF and i < len(w):
            influence = w[i] | (w[i + 1] << 8)
            i += 2
            weights[v, k] = min(max(influence / 65535.0, 0.001), 0.999)
            joints[v, k] = joint
            k += 1
            if k >= 4:
                joint = 0xFF
            else:
                joint = w[i]
                i += 1
        if weights[v].sum() <= 0:
            weights[v] = [0.999, 0, 0, 0]
        v += 1
    if v != n or i != len(w):
        raise ValueError("Vertex weight count does not match vertex count")
    return pos, nrm, idx.reshape(-1, 3), joints, weights


def load_glb_rest(path, mesh_name):
    sys.path.insert(0, str(Path(path).resolve().parent))
    data = Path(path).read_bytes()
    jlen = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + jlen])
    blob = data[28 + jlen:]
    sizes = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
    kinds = {5121: np.uint8, 5123: np.uint16, 5126: np.float32}

    def acc(i):
        a = doc["accessors"][i]
        view = doc["bufferViews"][a["bufferView"]]
        k = sizes[a["type"]]
        arr = np.frombuffer(blob, kinds[a["componentType"]], a["count"] * k, view["byteOffset"])
        return arr.reshape(a["count"], k) if k > 1 else arr
    mesh = next(m for m in doc["meshes"] if m["name"] == mesh_name)
    P = np.array([[0, 0, 1], [1, 0, 0], [0, 1, 0]], float)
    out = []
    for prim in mesh["primitives"]:
        at = prim["attributes"]
        out.append((acc(at["POSITION"]).astype(float) @ P.T, acc(at["WEIGHTS_0"]).astype(float),
                    acc(at["JOINTS_0"]).astype(int)))
    return out


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: verify_avatar_mesh.py <source avatar package> <compiled directory>")
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    compiled = json.loads((out / "compiled.json").read_text())
    manifest = json.loads((src / "package.json").read_text())
    data = (out / compiled["mesh_path"]).read_bytes()
    if len(data) > MAX_BYTES or hashlib.sha256(data).hexdigest() != compiled["mesh_sha256"]:
        raise SystemExit("mesh payload hash/size mismatch")
    reader = LLSDBinary(data)
    header = reader.parse()
    header_size = reader.at
    if header.get("version", 0) > 999:
        raise SystemExit("mesh version exceeds viewer MAX_MESH_VERSION")
    skin = block(data, header_size, header["skin"])
    names = skin["joint_names"]
    inv = [matrix(m) for m in skin["inverse_bind_matrix"]]
    alt = [matrix(m) for m in skin["alt_inverse_bind_matrix"]]
    bind_shape = matrix(skin["bind_shape_matrix"])
    if not (len(names) == len(inv) == len(alt) <= 110) or not skin.get("lock_scale_if_joint_position"):
        raise SystemExit("skin joint/bind counts differ or exceed the per-mesh limit")
    # Joint world at rest with overrides: local translation = alt[j] row 3;
    # pelvis rides the runtime root at the origin.
    parent = {}
    rig = manifest["definition"]["rig"]["joint_positions"]
    world = {}
    for j, name in enumerate(names):
        local = np.zeros(3) if name == "mPelvis" else alt[j][3, :3]
        par = None
        best = None
        for k in range(j):  # parents precede children in the native order
            cand = world[names[k]] + local
            expected = np.array(rig[name]) - np.array(rig["mPelvis"])
            if np.allclose(cand, expected, atol=1e-5):
                best = names[k]
        par = best if name != "mPelvis" else None
        if name != "mPelvis" and par is None:
            raise SystemExit(f"override chain for {name} does not reproduce the authored joint")
        parent[name] = par
        world[name] = local if par is None else world[par] + local
    palette = []
    for j, name in enumerate(names):
        jw = np.eye(4)
        jw[3, :3] = world[name]
        palette.append(inv[j] @ jw)  # row-vector: v * InvBind * JointWorld
    palette = np.array(palette)
    report = {"header_version": header.get("version"), "joints": len(names), "lods": {}}
    worst = 0.0
    weight_err = 0.0
    for lod_key, mesh_name in (("high_lod", "LOD0"), ("medium_lod", "LOD1"), ("low_lod", "LOD2"), ("lowest_lod", "LOD3")):
        faces = block(data, header_size, header[lod_key])
        authored = load_glb_rest(src / "runtime.glb", mesh_name)
        if len(faces) != len(authored) or len(faces) != len(compiled["materials"]):
            raise SystemExit(f"{lod_key}: face count differs from materials")
        tris = 0
        for face, (apos, aw, aj) in zip(faces, authored):
            pos, nrm, idx, joints, weights = decode_face(face)
            tris += len(idx)
            if len(pos) != len(apos):
                raise SystemExit(f"{lod_key}: vertex count changed")
            w = weights / weights.sum(1, keepdims=True)  # LLSkinningUtil normalization
            hom = np.c_[pos, np.ones(len(pos))] @ bind_shape
            mats = np.einsum("vk,vkij->vij", w, palette[joints])
            skinned = np.einsum("vi,vij->vj", hom, mats)[:, :3]
            expected = apos - np.array(rig["mPelvis"])
            worst = max(worst, float(np.abs(skinned - expected).max()))
            dense = np.zeros((len(pos), len(names)))
            np.add.at(dense, (np.arange(len(pos))[:, None].repeat(4, 1), joints), w)
            adense = np.zeros_like(dense)
            np.add.at(adense, (np.arange(len(pos))[:, None].repeat(4, 1), aj), aw / aw.sum(1, keepdims=True))
            weight_err = max(weight_err, float(np.abs(dense - adense).max()))
        report["lods"][lod_key] = tris
    expected_tris = compiled["lod_triangles"]
    ok = (list(report["lods"].values()) == expected_tris and worst < 2e-4 and weight_err < 2e-3)
    report.update({"max_rest_position_error_m": round(worst, 7), "max_weight_error": round(weight_err, 6),
                   "lod_triangles_match": list(report["lods"].values()) == expected_tris, "pass": ok})
    print(json.dumps(report))
    raise SystemExit(0 if ok else 1)


if __name__ == "__main__":
    main()
