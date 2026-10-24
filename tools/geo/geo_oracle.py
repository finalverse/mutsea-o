# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
"""Independent geographic oracle for MutSea bindings.

Runs SpatialDB's own normalization (pyproj EPSG:3979 -> NAD83(CSRS) -> WGS84 with the
pinned CGG2013a grid) and a separate numpy implementation of the binding definition. It
never trusts the C# calibration: every number it reports is recomputed from source rasters.

  python -I geo_oracle.py inputs <spatialdb-geo worker dir> <source dir> <geography.json>
  python -I geo_oracle.py verify <spatialdb-geo worker dir> <source dir> <geography.json> <binding.json> <terrain.r32>
"""
import hashlib
import json
import math
import sys
from pathlib import Path

import numpy as np

A = 6378137.0
F = 1 / 298.257223563
E2 = F * (2 - F)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def ecef(lon, lat, h):
    lo, la = np.radians(lon), np.radians(lat)
    n = A / np.sqrt(1 - E2 * np.sin(la) ** 2)
    return np.stack([(n + h) * np.cos(la) * np.cos(lo), (n + h) * np.cos(la) * np.sin(lo), (n * (1 - E2) + h) * np.sin(la)], axis=-1)


def enu(lon, lat):
    lo, la = math.radians(lon), math.radians(lat)
    return np.array([[-math.sin(lo), math.cos(lo), 0],
                     [-math.sin(la) * math.cos(lo), -math.sin(la) * math.sin(lo), math.cos(la)],
                     [math.cos(la) * math.cos(lo), math.cos(la) * math.sin(lo), math.sin(la)]])


def region_frame(binding):
    """Rows: region +X, +Y, +Z in ECEF; plus the anchor ECEF. Written from the contract text."""
    t = binding['transform']
    a = t['anchor']
    e, n, u = enu(a['longitude_degrees'], a['latitude_degrees'])
    h = math.radians(t['heading_degrees'])
    s = t['scale']
    x = s * (math.cos(h) * e - math.sin(h) * n)
    y = s * (math.sin(h) * e + math.cos(h) * n)
    z = s * u
    return np.array([x, y, z]), ecef(a['longitude_degrees'], a['latitude_degrees'], a['height_metres'])


def to_region(binding, xyz):
    axes, origin = region_frame(binding)
    s2 = binding['transform']['scale'] ** 2
    return (np.asarray(xyz) - origin) @ axes.T / s2


def load(worker, sources, geography):
    sys.path.insert(0, str(Path(worker).resolve()))
    from spatialdb_geo.coordinates import Dem, Frames  # noqa: E402
    g = json.loads(Path(geography).read_text())
    norm = g['normalization']
    assert sha(Path(sources) / 'geoid.tif') == norm['vertical_grid_sha256'], 'geoid grid differs from the publication pin'
    bounds = g['projected_bounds']
    centre = np.array([(bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2])
    dem = Dem(Path(sources) / 'dtm.tif')
    h0 = float(dem.sample(*centre))
    frames = Frames(Path(sources) / 'geoid.tif', centre, h0)
    return g, dem, frames, centre, h0, bounds


def inputs(worker, sources, geography):
    g, dem, frames, centre, h0, bounds = load(worker, sources, geography)
    o = g['frame']['origin']
    origin_error = [frames.origin['longitude_degrees'] - o['longitude_degrees'], frames.origin['latitude_degrees'] - o['latitude_degrees'],
                    frames.origin['height_metres'] - o['height_metres']]
    n_eff = o['height_metres'] - h0
    xs = np.linspace(bounds[0], bounds[2], 33)
    ys = np.linspace(bounds[1], bounds[3], 33)
    xx, yy = np.meshgrid(xs, ys)
    lon, lat, h = frames.geographic(xx, yy, np.zeros_like(xx))
    variation = float(np.max(np.abs(np.asarray(h) - n_eff)))
    # Grid convergence and point scale at the centre from the projection itself.
    p = frames.local(np.array([centre[0], centre[0] + 1, centre[0]]), np.array([centre[1], centre[1], centre[1] + 1]), np.array([h0, h0, h0]))
    east, north = p[1] - p[0], p[2] - p[0]
    bearing = math.degrees(math.atan2(north[0], -north[2]))
    scale = 1 / math.hypot(north[0], north[2])
    print(json.dumps({'origin_orthometric_metres': h0, 'geoid_undulation_effective_metres': n_eff, 'geoid_variation_metres': variation,
                      'origin_reproduction_error': origin_error, 'grid_north_bearing_degrees': bearing,
                      'grid_conformal_residual_degrees': abs(math.degrees(math.acos(np.dot(east, north) / np.linalg.norm(east) / np.linalg.norm(north))) - 90),
                      'projected_scale_factor': scale, 'geoid_grid_sha256': g['normalization']['vertical_grid_sha256']}, indent=1))


def verify(worker, sources, geography, binding_path, terrain_path):
    g, dem, frames, centre, h0, bounds = load(worker, sources, geography)
    b = json.loads(Path(binding_path).read_text())
    size = b['region']['size_metres']
    terrain = np.frombuffer(Path(terrain_path).read_bytes(), dtype='<f4').reshape(size[1], size[0])
    assert sha(terrain_path) == b['terrain']['r32_sha256'], 'terrain differs from the binding pin'
    report = {'binding_version': b['version']}
    # 1. Matrices: the published 4x4 equals the contract text implemented independently here.
    axes, origin = region_frame(b)
    m = np.array(b['transform']['region_to_ecef']).reshape(4, 4).T
    report['matrix_rotation_max_abs_error'] = float(np.max(np.abs(m[:3, :3] - axes.T)))
    report['matrix_translation_error_metres'] = float(np.linalg.norm(m[:3, 3] - origin))
    # 2. Control points: geographic -> region with this implementation.
    cps = []
    for cp in b['calibration']['control_points']:
        q = cp['geographic']
        local = to_region(b, ecef(q['longitude_degrees'], q['latitude_degrees'], q['height_metres']))
        cps.append(float(np.linalg.norm(local - np.array(cp['region_local']))))
    report['control_point_max_error_metres'] = max(cps)
    # 3. The SpatialDB publication origin, from the source projection, lands on the region centre.
    o = frames.origin
    c = to_region(b, ecef(o['longitude_degrees'], o['latitude_degrees'], o['height_metres']))
    report['origin_region_local'] = c.tolist()
    report['origin_vs_region_centre_metres'] = float(math.hypot(c[0] - size[0] / 2, c[1] - size[1] / 2))
    report['origin_z_minus_orthometric_metres'] = float(c[2] - h0)
    # 4. Grid north from the projection maps to region +Y.
    p = frames.local(np.array([centre[0], centre[0]]), np.array([centre[1], centre[1] + 100]), np.array([h0, h0]))
    pub_to_region = np.linalg.inv(np.array(b['transform']['region_to_publication']).reshape(4, 4).T)
    q = (pub_to_region @ np.c_[p, np.ones(2)].T).T[:, :3]
    d = q[1] - q[0]
    report['grid_north_vs_region_y_degrees'] = math.degrees(math.atan2(d[0], d[1]))
    # 5. Source DTM samples (the 2 m raster SpatialDB compiled from) placed through the binding:
    #    horizontal placement, z versus orthometric H, and MutSea ground versus the source.
    xs = np.arange(bounds[0] + 2, bounds[2] - 1, 6.0)
    ys = np.arange(bounds[1] + 2, bounds[3] - 1, 6.0)
    xx, yy = np.meshgrid(xs, ys)
    hh = dem.sample(xx, yy)
    ok = np.isfinite(hh)
    lon, lat, h = frames.geographic(xx[ok], yy[ok], hh[ok])
    local = to_region(b, ecef(np.asarray(lon), np.asarray(lat), np.asarray(h)))
    inside = (local[:, 0] >= 0) & (local[:, 1] >= 0) & (local[:, 0] < size[0] - 1) & (local[:, 1] < size[1] - 1)
    local = local[inside]
    H = hh[ok][inside]
    i, j = np.floor(local[:, 0]).astype(int), np.floor(local[:, 1]).astype(int)
    u, v = local[:, 0] - i, local[:, 1] - j
    ground = terrain[j, i] * (1 - u) * (1 - v) + terrain[j, i + 1] * u * (1 - v) + terrain[j + 1, i] * (1 - u) * v + terrain[j + 1, i + 1] * u * v
    dz = local[:, 2] - H
    dg = ground - local[:, 2]
    report['dtm_samples'] = int(len(H))
    report['z_minus_orthometric'] = {'max_abs': float(np.max(np.abs(dz))), 'mean': float(np.mean(dz))}
    report['mutsea_ground_minus_source_dtm'] = {'max_abs': float(np.max(np.abs(dg))), 'rms': float(np.sqrt(np.mean(dg ** 2))),
                                                'p95_abs': float(np.percentile(np.abs(dg), 95)), 'p99_abs': float(np.percentile(np.abs(dg), 99))}
    print(json.dumps(report, indent=1))


if __name__ == '__main__':
    mode = sys.argv[1]
    if mode == 'inputs':
        inputs(*sys.argv[2:5])
    elif mode == 'verify':
        verify(*sys.argv[2:7])
    else:
        raise SystemExit('unknown mode')
