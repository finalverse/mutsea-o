# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
"""Alignment map for one MutSea geographic binding.

Left: the live MutSea region frame. MutSea's own ground (the issued heightmap) is shaded,
and the published SpatialDB buildings and roads are drawn from the correspondence index, so
any misalignment between live ground and the publication would show as offset edges.
Right: the same district in true east/north at the publication origin, with the region
square rotated by its calibrated heading.

  python3 alignment_map.py <binding.json> <correspondence.json> <terrain.r32> <out.png> [objects.json]
"""
import json
import math
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.collections import PolyCollection

binding = json.load(open(sys.argv[1]))
index = json.load(open(sys.argv[2]))
size = binding["region"]["size_metres"][0]
terrain = np.fromfile(sys.argv[3], dtype="<f4").reshape(size, size)
objects = json.load(open(sys.argv[5])) if len(sys.argv) > 5 else []
# Objects may be given by WGS84 position; place them through the binding's own inverse matrix.
_inverse = np.linalg.inv(np.array(binding["transform"]["region_to_ecef"]).reshape(4, 4).T)
for o in objects:
    if "geographic" in o:
        g = o["geographic"]
        lo, la, h = math.radians(g["longitude_degrees"]), math.radians(g["latitude_degrees"]), g["height_metres"]
        n = 6378137.0 / math.sqrt(1 - 0.0066943799901413165 * math.sin(la) ** 2)
        xyz = np.array([(n + h) * math.cos(la) * math.cos(lo), (n + h) * math.cos(la) * math.sin(lo), (n * (1 - 0.0066943799901413165) + h) * math.sin(la), 1.0])
        o["x"], o["y"] = (_inverse @ xyz)[:2]
heading = binding["transform"]["heading_degrees"]

# Hillshade of MutSea's own heightmap (sun from the north-west, true north).
gy, gx = np.gradient(terrain.astype(float))
sun = math.radians(315 - heading)
shade = np.clip(0.55 + 0.45 * ((-gx * math.cos(sun) - gy * math.sin(sun)) / np.sqrt(1 + gx * gx + gy * gy)), 0, 1)

colours = {"building": "#7a5af5", "road": "#e9e4d4"}
fig, (left, detail, right) = plt.subplots(1, 3, figsize=(25, 8.8), dpi=150, gridspec_kw={"width_ratios": [1, 1, 1]})
for ax in (left, detail, right):
    ax.set_aspect("equal")
    ax.set_facecolor("#10131a")


def polygons(kind, transform):
    out = []
    for f in index["features"]:
        if f["kind"] != kind:
            continue
        p = f["footprint"]
        for t in range(0, len(p), 6):
            out.append([transform(p[t], p[t + 1]), transform(p[t + 2], p[t + 3]), transform(p[t + 4], p[t + 5])])
    return out


# ---- left and detail: region frame
elevation = (terrain - terrain.min()) / max(1e-6, float(terrain.max() - terrain.min()))
rgb = plt.get_cmap("gist_earth")(0.15 + 0.7 * elevation)[..., :3] * (0.3 + 0.7 * shade)[..., None] * 0.75
for ax in (left, detail):
    ax.imshow(rgb, origin="lower", extent=[0, size, 0, size], interpolation="bilinear")
    ax.contour(np.arange(size), np.arange(size), terrain, levels=np.arange(6, 30, 1 if ax is detail else 2), colors="#5fd3b5", linewidths=0.35, alpha=0.6)
    ax.add_collection(PolyCollection(polygons("road", lambda x, y: (x, y)), facecolors=colours["road"], edgecolors="none", alpha=0.55))
    ax.add_collection(PolyCollection(polygons("building", lambda x, y: (x, y)), facecolors=colours["building"], edgecolors="none", alpha=0.75))
outline = np.array(binding["terrain"]["published_outline"] + [binding["terrain"]["published_outline"][0]])
left.plot(outline[:, 0], outline[:, 1], color="#f5c542", lw=1, ls="--", label="SpatialDB published district (504.4 m)")
left.plot([0, size, size, 0, 0], [0, 0, size, size, 0], color="#ffffff", lw=1.2, label="MutSea region (512 m)")
e = binding["entry"]["position"]
left.scatter([e[0]], [e[1]], marker="*", s=180, color="#ffffff", zorder=5, label="binding entry (publication origin)")
if objects:
    xs = [o["x"] for o in objects]; ys = [o["y"] for o in objects]
    box = (min(xs) - 25, max(xs) + 25, min(ys) - 25, max(ys) + 25)
    left.plot([box[0], box[1], box[1], box[0], box[0]], [box[2], box[2], box[3], box[3], box[2]], color="#ff9f43", lw=1)
    for o in objects:
        for ax in (left, detail):
            ax.scatter([o["x"]], [o["y"]], marker=o.get("marker", "o"), s=90 if ax is detail else 45, color=o.get("colour", "#ff6b6b"), edgecolors="black", zorder=6,
                       label=o["label"] if ax is detail else None)
    detail.set_xlim(box[0], box[1]); detail.set_ylim(box[2], box[3])
    detail.set_title("Live MutSea objects and arrivals (detail of the orange box)", color="white", fontsize=10)
    detail.set_xlabel("region +X (m)", color="#c8c8d0")
    detail.legend(loc="lower left", fontsize=8, facecolor="#1b1f2a", labelcolor="white", edgecolor="#333")
left.set_xlim(-12, size + 12)
left.set_ylim(-12, size + 12)
left.set_title("MutSea region frame: live ground (hillshade, 2 m contours) under the published SpatialDB scene", color="white", fontsize=10)
left.set_xlabel("region +X (m)", color="#c8c8d0")
left.set_ylabel("region +Y (m)", color="#c8c8d0")
left.legend(loc="lower left", fontsize=7.5, facecolor="#1b1f2a", labelcolor="white", edgecolor="#333")

# ---- right: true east/north at the publication origin
m = np.array(binding["transform"]["region_to_publication"]).reshape(4, 4).T


def to_en(x, y, z=0.0):
    p = m @ np.array([x, y, z, 1.0])
    return (p[0], -p[2])  # publication x is east, z is south


right.add_collection(PolyCollection(polygons("road", to_en), facecolors=colours["road"], edgecolors="none", alpha=0.55))
right.add_collection(PolyCollection(polygons("building", to_en), facecolors=colours["building"], edgecolors="none", alpha=0.75))
square = np.array([to_en(x, y) for x, y in ((0, 0), (size, 0), (size, size), (0, size), (0, 0))])
right.plot(square[:, 0], square[:, 1], color="#ffffff", lw=1.2)
o = to_en(e[0], e[1])
for name, (x, y), colour in (("region +Y", to_en(e[0], e[1] + 120), "#ffffff"), ("region +X", to_en(e[0] + 120, e[1]), "#aaaaaa")):
    right.annotate("", xy=(x, y), xytext=o, arrowprops=dict(arrowstyle="-|>", color=colour, lw=1.4))
    right.text(x, y + 8, name, color=colour, fontsize=8, ha="center")
right.annotate("", xy=(o[0], o[1] + 120), xytext=o, arrowprops=dict(arrowstyle="-|>", color="#ff9f43", lw=1.4))
right.text(o[0], o[1] + 128, "true north", color="#ff9f43", fontsize=8, ha="center")
lim = 380
right.set_xlim(-lim, lim)
right.set_ylim(-lim, lim)
right.set_title(f"Geographic frame (true north up): region heading {heading:.4f}° east of north, scale 1", color="white", fontsize=10)
right.set_xlabel("east of publication origin (m)", color="#c8c8d0")
right.set_ylabel("north of publication origin (m)", color="#c8c8d0")
a = binding["transform"]["anchor"]
fig.suptitle(f"Old Montréal — MutSea region {binding['region']['region_id'][:8]} bound to SpatialDB publication {binding['publication']['version'][:12]} "
             f"(binding {binding['version'][:12]}, anchor {a['latitude_degrees']:.6f}, {a['longitude_degrees']:.6f})", color="white", fontsize=11)
fig.text(0.5, 0.004, "Sources: Natural Resources Canada HRDEM (OGL-Canada-2.0); Ville de Montréal roof outlines and roads (CC-BY-4.0). "
         "Building heights and road widths are SpatialDB estimates.", color="#9aa0aa", fontsize=7.5, ha="center")
fig.patch.set_facecolor("#0b0d12")
for ax in (left, detail, right):
    ax.tick_params(colors="#9aa0aa", labelsize=7)
    for s in ax.spines.values():
        s.set_color("#333")
plt.tight_layout(rect=[0, 0.035, 1, 0.95])
plt.savefig(sys.argv[4], facecolor=fig.get_facecolor())
print(sys.argv[4])
