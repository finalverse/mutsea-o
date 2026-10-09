#!/usr/bin/env python3
"""Offline validation of MutSea Starter Library build packages; never executes content."""
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import argparse
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re

KINDS = {'asset', 'prefab', 'building', 'place', 'world', 'avatar', 'outfit', 'experience', 'environment'}
LICENSES = {'CC0-1.0', 'CC-BY-4.0', 'BSD-3-Clause', 'MIT', 'LicenseRef-Finalverse-Original'}
ROLES = {'authoring', 'runtime', 'preview', 'metadata', 'license'}
AFFORDANCES = {'inspect', 'sit', 'walk', 'enter', 'look_at', 'speak', 'interact', 'customize'}
ID = re.compile(r'[a-z][a-z0-9]*(?:[.-][a-z0-9]+)+\Z')
VERSION = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z')
HASH = re.compile(r'[0-9a-f]{64}\Z')

class InvalidPackage(ValueError):
    pass

def require(condition, message):
    if not condition:
        raise InvalidPackage(message)

def keys(value, required, optional=()):
    require(isinstance(value, dict), 'Expected an object')
    require(set(required) <= set(value), 'Missing required fields: ' + ', '.join(sorted(set(required) - set(value))))
    require(set(value) <= set(required) | set(optional), 'Unknown fields: ' + ', '.join(sorted(set(value) - set(required) - set(optional))))

def text(value, limit=512):
    require(isinstance(value, str) and 0 < len(value) <= limit and not any(ord(c) < 32 for c in value), 'Invalid text')

def nonnegative(value):
    require(type(value) is int and 0 <= value <= 2**40, 'Invalid byte/count budget')

def choice(value, allowed, message):
    require(isinstance(value, str) and value in allowed, message)

def strict_json(path):
    require(not path.is_symlink() and path.is_file(), 'Manifest must be an ordinary file')
    require(path.stat().st_size <= 1024 * 1024, 'Manifest exceeds 1 MiB')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'Duplicate JSON field: ' + key)
            result[key] = value
        return result
    return json.loads(path.read_text(), object_pairs_hook=unique,
        parse_constant=lambda _: (_ for _ in ()).throw(InvalidPackage('Non-finite JSON number')))

def contained(root, relative):
    require(isinstance(relative, str) and '\\' not in relative, 'Invalid payload path')
    path = PurePosixPath(relative)
    require(path.as_posix() == relative, 'Payload path must be canonical')
    require(not path.is_absolute() and path.parts and all(part not in {'.', '..'} for part in path.parts), 'Payload path escapes package')
    candidate = root
    for part in path.parts:
        candidate /= part
        require(not candidate.is_symlink(), 'Symlinks are not package payloads')
    require(candidate.is_file() and candidate.resolve().is_relative_to(root.resolve()), 'Missing/outside payload')
    return candidate

def validate(manifest, catalog=None, stack=None):
    manifest = Path(manifest)
    root = manifest.parent
    data = strict_json(manifest)
    keys(data, {'schema_version', 'package_id', 'version', 'kind', 'title', 'author', 'publisher',
        'license', 'provenance', 'dependencies', 'files', 'entities', 'budget', 'definition'})
    require(type(data['schema_version']) is int and data['schema_version'] == 1, 'Unsupported schema')
    require(isinstance(data['package_id'], str) and ID.fullmatch(data['package_id']), 'Invalid package ID')
    require(isinstance(data['version'], str) and VERSION.fullmatch(data['version']), 'Version must be exact semver')
    choice(data['kind'], KINDS, 'Unsupported package kind')
    for key in ('title', 'author', 'publisher'):
        text(data[key])
    keys(data['license'], {'spdx', 'attribution'})
    choice(data['license']['spdx'], LICENSES, 'Unreviewed license')
    require(isinstance(data['license']['attribution'], str), 'Attribution must be explicit')
    if data['license']['spdx'] == 'CC-BY-4.0':
        text(data['license']['attribution'], 4096)
    require(isinstance(data['provenance'], list) and 0 < len(data['provenance']) <= 128, 'Provenance required')
    for source in data['provenance']:
        keys(source, {'origin', 'author', 'source', 'license', 'modifications'})
        choice(source['origin'], {'original', 'external', 'inherited'}, 'Invalid provenance origin')
        for key in ('author', 'source', 'license', 'modifications'):
            text(source[key], 4096)
        require(source['license'] in LICENSES, 'Unreviewed source rights')
    require(isinstance(data['files'], list) and 0 < len(data['files']) <= 1024, 'Payload required')
    seen = set()
    total = 0
    for entry in data['files']:
        keys(entry, {'path', 'sha256', 'size_bytes', 'role'})
        text(entry['path'], 4096)
        require(entry['path'] not in seen, 'Duplicate payload')
        seen.add(entry['path'])
        require(isinstance(entry['sha256'], str) and HASH.fullmatch(entry['sha256']), 'Invalid payload digest')
        nonnegative(entry['size_bytes'])
        choice(entry['role'], ROLES, 'Invalid payload role')
        payload = contained(root, entry['path'])
        require(payload.stat().st_size == entry['size_bytes'], 'Payload size mismatch')
        require(payload.stat().st_size <= 128 * 1024**2, 'Payload exceeds development limit')
        require(hashlib.sha256(payload.read_bytes()).hexdigest() == entry['sha256'], 'Payload digest mismatch')
        total += entry['size_bytes']
    require(any(f['role'] == 'runtime' for f in data['files']), 'Runtime representation required')
    keys(data['budget'], {'triangles', 'materials', 'texture_bytes', 'payload_bytes'})
    for value in data['budget'].values():
        nonnegative(value)
    require(total <= data['budget']['payload_bytes'] <= 256 * 1024**2, 'Payload budget exceeded')
    require(data['budget']['triangles'] <= 200000 and data['budget']['materials'] <= 32,
        'Development geometry/material budget exceeded')
    require(isinstance(data['entities'], list) and 0 < len(data['entities']) <= 4096, 'Semantic entities required')
    ids = set()
    for entity in data['entities']:
        keys(entity, {'local_id', 'name', 'semantic_type', 'description', 'tags', 'affordances', 'relationships', 'ownership'})
        for key in ('local_id', 'name', 'semantic_type', 'description'):
            text(entity[key])
        require(entity['local_id'] not in ids, 'Duplicate semantic entity')
        ids.add(entity['local_id'])
        require(entity['ownership'] == 'inherit_instance_owner', 'Package cannot assign privileges or world owners')
        for key in ('tags', 'affordances', 'relationships'):
            require(isinstance(entity[key], list) and len(entity[key]) <= 64, 'Invalid semantic list')
        for tag in entity['tags']:
            text(tag, 64)
        require(all(isinstance(a, str) and a in AFFORDANCES for a in entity['affordances']), 'Unsupported affordance')
    for entity in data['entities']:
        for relationship in entity['relationships']:
            keys(relationship, {'type', 'target'})
            text(relationship['type'], 64)
            text(relationship['target'])
            require(relationship['target'] in ids, 'Dangling semantic relationship')
    definition = data['definition']
    keys(definition, {'scale', 'units', 'up_axis', 'minimum_client_version'},
        {'spawn_points', 'required_slots', 'components', 'environment', 'materials', 'material_bindings', 'seating', 'lods', 'collision'})
    choice(definition['scale'], {'asset', 'prefab', 'building', 'place', 'neighborhood', 'community', 'city', 'world'}, 'Invalid composition scale')
    require(definition['units'] == 'meters' and definition['up_axis'] == 'Z', 'Unsupported authoring coordinates')
    text(definition['minimum_client_version'], 64)
    if 'materials' in definition or 'material_bindings' in definition:
        materials=definition.get('materials');bindings=definition.get('material_bindings')
        require(isinstance(materials,list) and 0<len(materials)<=8 and isinstance(bindings,dict), 'Bounded material definitions and face bindings required')
        material_ids=set()
        for material in materials:
            keys(material,{'local_id','base_color_linear','metallic_factor','roughness_factor','emissive_linear','alpha_mode','alpha_cutoff','double_sided'})
            text(material['local_id'],120);require(material['local_id'] not in material_ids,'Duplicate material ID');material_ids.add(material['local_id'])
            for key,length in [('base_color_linear',4),('emissive_linear',3)]:
                require(isinstance(material[key],list) and len(material[key])==length and all(type(v) in (int,float) and math.isfinite(v) and 0<=v<=1 for v in material[key]),'Invalid linear material factors')
            for key in ['metallic_factor','roughness_factor','alpha_cutoff']:
                require(type(material[key]) in (int,float) and math.isfinite(material[key]) and 0<=material[key]<=1,'Invalid material factor')
            choice(material['alpha_mode'],{'OPAQUE','MASK','BLEND'},'Invalid alpha mode');require(type(material['double_sided']) is bool,'Invalid sidedness')
        require(set(bindings)==ids,'Material bindings must cover every semantic part')
        for faces in bindings.values():
            require(isinstance(faces,list) and 0<len(faces)<=8 and all(isinstance(x,str) and x in material_ids for x in faces),'Unknown face material')
    if 'seating' in definition:
        seats=definition['seating']
        require(isinstance(seats,dict) and 0<len(seats)<=8 and set(seats)<=ids,'Invalid native seats')
        for local_id,seat in seats.items():
            keys(seat,{'capacity','position','rotation'})
            entity=next(e for e in data['entities'] if e['local_id']==local_id)
            require(type(seat['capacity']) is int and seat['capacity']==1 and entity['semantic_type']=='seating' and 'sit' in entity['affordances'],'Native seat needs seating semantics and capacity one')
            for key,length,bound in [('position',3,2),('rotation',4,1)]:
                require(isinstance(seat[key],list) and len(seat[key])==length and all(type(v) in (int,float) and math.isfinite(v) and -bound<=v<=bound for v in seat[key]),'Invalid native sit transform')
            require(any(seat['position']) and abs(sum(v*v for v in seat['rotation'])-1)<.001,'Invalid native sit target')
    if 'collision' in definition:
        shapes=definition['collision']
        require('materials' in definition and isinstance(shapes,dict) and 0<len(shapes)<=32 and set(shapes)<=ids,'Collision requires bounded native material-package parts')
        for shape in shapes.values():
            keys(shape,{'shape'},{'minimum','maximum'})
            choice(shape['shape'],{'box','none'},'Unsupported small-part collision')
            if shape['shape']=='none': require(set(shape)=={'shape'},'Noncolliding part cannot contain hull bounds')
            else:
                for key in ('minimum','maximum'):
                    require(isinstance(shape.get(key),list) and len(shape[key])==3 and all(type(v) in (int,float) and math.isfinite(v) and -.5<=v<=.5 for v in shape[key]),'Collision hull must stay inside the normalized visual bounds')
                require(all(high-low>=.001 for low,high in zip(shape['minimum'],shape['maximum'])),'Collision hull must have positive extents')
    if 'lods' in definition:
        lods=definition['lods'];keys(lods,{'medium','low','lowest'})
        previous=data['budget']['triangles']
        for level in ('medium','low','lowest'):
            keys(lods[level],{'path','triangles'});nonnegative(lods[level]['triangles'])
            require(lods[level]['path'] in seen and 0<lods[level]['triangles']<previous,'Missing or unreduced LOD payload')
            previous=lods[level]['triangles']
    if data['kind'] == 'world':
        require(isinstance(definition.get('spawn_points'), list) and definition['spawn_points'], 'World requires spawn points')
        for position in definition['spawn_points']:
            require(isinstance(position, list) and len(position) == 3 and
                all(type(v) in (int, float) and math.isfinite(v) for v in position), 'Invalid spawn point')
    if data['kind'] == 'avatar':
        slots = definition.get('required_slots', [])
        require(isinstance(slots, list) and all(isinstance(slot, str) for slot in slots), 'Invalid appearance slots')
        require(set(slots) >= {'shape', 'skin', 'hair', 'eyes', 'outfit'}, 'Incomplete avatar appearance bundle')
    require(isinstance(data['dependencies'], list) and len(data['dependencies']) <= 256, 'Invalid dependencies')
    identity = (data['package_id'], data['version'])
    stack = set() if stack is None else set(stack)
    require(identity not in stack, 'Dependency cycle')
    stack.add(identity)
    deps = set()
    for dep in data['dependencies']:
        keys(dep, {'package_id', 'version', 'manifest_sha256'})
        require(isinstance(dep['package_id'], str) and ID.fullmatch(dep['package_id']) and
            isinstance(dep['version'], str) and VERSION.fullmatch(dep['version']) and
            isinstance(dep['manifest_sha256'], str) and HASH.fullmatch(dep['manifest_sha256']), 'Unpinned dependency')
        key = (dep['package_id'], dep['version'])
        require(key not in deps, 'Duplicate dependency')
        deps.add(key)
        require(catalog is not None and key in catalog, 'Dependency unavailable in offline catalog')
        path = Path(catalog[key])
        require(not path.is_symlink() and path.stat().st_size <= 1024 * 1024, 'Invalid dependency manifest')
        require(hashlib.sha256(path.read_bytes()).hexdigest() == dep['manifest_sha256'], 'Dependency digest mismatch')
        resolved = validate(path, catalog, stack)
        require((resolved['package_id'], resolved['version']) == key, 'Dependency identity mismatch')
    return data

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    args = parser.parse_args()
    try:
        result = validate(args.manifest)
    except (InvalidPackage, OSError, json.JSONDecodeError, TypeError) as error:
        parser.exit(1, 'Invalid package: ' + str(error) + '\n')
    print('Validated', result['package_id'], result['version'], '(metadata/payload only; not provisioned)')
