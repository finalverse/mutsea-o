// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Earth Core consumer conformance for MutSea geographic bindings, in a third language.
//   node core-conformance.mjs <earth-core sdk dist/index.js> <binding.json> [<discovery worlds.json> <discovery entry.json>]
// Recomputes the RFC 8785 version with JSON.stringify, rebuilds the transform from the contract
// text, and runs Earth Core's own validators on every projection MutSea serves.
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";

const [, , coreDist, bindingPath, worldsPath, entryPath] = process.argv;
const core = await import(pathToFileURL(coreDist).href);
const binding = JSON.parse(readFileSync(bindingPath, "utf8"));
const results = [];
function check(name, ok, detail = "") {
  results.push({ name, ok, detail });
  if (!ok) { console.error("FAIL " + name + " " + detail); process.exitCode = 1; } else console.log("PASS " + name + (detail ? " " + detail : ""));
}
function jcs(v) {
  if (Array.isArray(v)) return "[" + v.map(jcs).join(",") + "]";
  if (v && typeof v === "object") return "{" + Object.keys(v).sort().map((k) => JSON.stringify(k) + ":" + jcs(v[k])).join(",") + "}";
  return JSON.stringify(v);
}
const { version, ...body } = binding;
check("binding version reproduces as SHA-256(JCS) in JavaScript", createHash("sha256").update(jcs(body)).digest("hex") === version);

// WGS84 and the binding transform, written independently from the contract text.
const A = 6378137, F = 1 / 298.257223563, E2 = F * (2 - F), rad = (d) => (d * Math.PI) / 180;
function ecef({ longitude_degrees: lo, latitude_degrees: la, height_metres: h }) {
  const sl = Math.sin(rad(lo)), cl = Math.cos(rad(lo)), sp = Math.sin(rad(la)), cp = Math.cos(rad(la)), n = A / Math.sqrt(1 - E2 * sp * sp);
  return [(n + h) * cp * cl, (n + h) * cp * sl, (n * (1 - E2) + h) * sp];
}
function geodetic([x, y, z]) {
  const p = Math.hypot(x, y); let lat = Math.atan2(z, p * (1 - E2));
  for (let i = 0; i < 32; i++) { const s = Math.sin(lat), n = A / Math.sqrt(1 - E2 * s * s); lat = Math.atan2(z + E2 * n * s, p); }
  const s = Math.sin(lat), c = Math.cos(lat);
  return { longitude_degrees: (Math.atan2(y, x) * 180) / Math.PI, latitude_degrees: (lat * 180) / Math.PI, height_metres: p * c + z * s - A * Math.sqrt(1 - E2 * s * s) };
}
const t = binding.transform, a = t.anchor, lo = rad(a.longitude_degrees), la = rad(a.latitude_degrees), h = rad(t.heading_degrees), s = t.scale;
const east = [-Math.sin(lo), Math.cos(lo), 0], north = [-Math.sin(la) * Math.cos(lo), -Math.sin(la) * Math.sin(lo), Math.cos(la)];
const up = [Math.cos(la) * Math.cos(lo), Math.cos(la) * Math.sin(lo), Math.sin(la)];
const X = east.map((e, i) => s * (Math.cos(h) * e - Math.sin(h) * north[i])), Y = east.map((e, i) => s * (Math.sin(h) * e + Math.cos(h) * north[i])), Z = up.map((u) => s * u);
const o = ecef(a);
const local = (p) => o.map((v, i) => v + X[i] * p[0] + Y[i] * p[1] + Z[i] * p[2]);
const m = t.region_to_ecef;
const rot = Math.max(...[X, Y, Z].flatMap((col, c) => col.map((v, r) => Math.abs(v - m[c * 4 + r]))));
check("region_to_ecef equals the contract transform", rot < 1e-12 && Math.hypot(o[0] - m[12], o[1] - m[13], o[2] - m[14]) < 1e-6, `rotation error ${rot.toExponential(2)}`);
let worst = 0;
for (const cp of binding.calibration.control_points) {
  const q = local(cp.region_local), g = ecef(cp.geographic);
  worst = Math.max(worst, Math.hypot(q[0] - g[0], q[1] - g[1], q[2] - g[2]));
}
check("every control point reproduces through the JavaScript transform", worst < 1e-6, `max ${worst.toExponential(2)} m`);

function coreBinding(b) {
  return { schema_version: "earth.world-binding/v1", address: "usap://mutsea/entity/" + b.region.region_id, authority: "mutsea", world_id: b.world_id,
    region_id: b.region.region_id, anchor: b.transform.anchor, frame: { axes: ["east", "north", "up"], unit: "metre" }, heading_degrees: b.transform.heading_degrees,
    scale: b.transform.scale, version: b.version };
}
const projected = coreBinding(binding);
check("Earth Core validateWorldBinding accepts the projection", (() => { core.validateWorldBinding(projected); return true; })());
for (const [field, value] of [["heading_degrees", 360], ["scale", 0], ["version", "x"], ["authority", "spatialdb"]])
  check(`Earth Core rejects ${field}=${JSON.stringify(value)}`, (() => { try { core.validateWorldBinding({ ...projected, [field]: value }); return false; } catch { return true; } })());

function entryShape(e) {
  return Object.keys(e).sort().join(",") === "address,anchor,authority,authorization,binding_relation,region_id,schema_version,world_id" &&
    e.schema_version === "earth.world-entry/v1" && e.authorization === "required" && e.authority === "mutsea" && core.parseAddress(e.address) && core.validatePosition(e.anchor) !== undefined;
}
if (worldsPath) {
  const worlds = JSON.parse(readFileSync(worldsPath, "utf8"));
  check("discovery lists the bound region", worlds.ok === true && worlds.worlds.length >= 1);
  for (const w of worlds.worlds) {
    core.validateWorldBinding(w.world_binding);
    check("served world_binding equals the projection of the issued binding", jcs(w.world_binding) === jcs(projected));
    check("served world_entry has the Earth Core shape and requires authorization", entryShape(w.world_entry));
    const at = geodetic(local(binding.entry.position)), e = ecef(w.world_entry.anchor), want = ecef(at);
    check("served entry anchor is the binding entry position", Math.hypot(e[0] - want[0], e[1] - want[1], e[2] - want[2]) < 1e-6);
    check("served region_to_ecef is the issued column-major matrix", JSON.stringify(w.region_to_ecef) === JSON.stringify(binding.transform.region_to_ecef));
    check("discovery leaks no credential or capability", !/password|token|secret|cap\/|seed/i.test(JSON.stringify(w)));
  }
}
if (entryPath) {
  const entry = JSON.parse(readFileSync(entryPath, "utf8"));
  check("entry resolves inside the bound region", entry.ok === true && entry.target.inside === true);
  check("entry world_entry has the Earth Core shape", entryShape(entry.target.world_entry));
  const g = ecef(entry.target.geographic), l = local(entry.target.region_local);
  check("entry geographic and region-local positions agree", Math.hypot(g[0] - l[0], g[1] - l[1], g[2] - l[2]) < 1e-6);
  check("Earth Core accepts the return destination", (() => { core.validateDestination({ schema_version: "earth.navigation/v1", kind: "geographic", position: entry.target.geographic }); return true; })());
}
console.log(JSON.stringify({ passed: results.filter((r) => r.ok).length, failed: results.filter((r) => !r.ok).length }));
