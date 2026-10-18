#!/usr/bin/env python3
"""Bounded offline OAR preflight. Writes a passive staging derivative, never restores a world."""
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import argparse
from collections import Counter
from dataclasses import dataclass
import gzip
import hashlib
import io
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import stat
import struct
import tarfile
import tempfile
import uuid
import xml.etree.ElementTree as ET


class Rejected(ValueError):
    pass


@dataclass(frozen=True)
class Limits:
    compressed: int = 64 * 1024 * 1024
    expanded: int = 256 * 1024 * 1024
    member: int = 8 * 1024 * 1024
    members: int = 10000
    xml_nodes: int = 100000
    xml_depth: int = 64


EXTENSIONS = frozenset(('animation.bvh', 'bodypart.txt', 'clothing.txt', 'gesture.txt',
    'image.jpg', 'image.tga', 'landmark.txt', 'bytecode.lso', 'script.lsl', 'mesh.llmesh',
    'notecard.txt', 'object.xml', 'simstate.bin', 'sound.ogg', 'sound.wav', 'texture.jp2',
    'texture.tga', 'material.xml', 'settings.bin', 'prmat.dat'))
EXECUTABLE = frozenset(('script.lsl', 'bytecode.lso', 'simstate.bin'))
ZERO = str(uuid.UUID(int=0))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def path_name(value):
    # No extraction occurs, but the derivative must also be safe for native readers.
    if not value or '\\' in value or '\x00' in value or ':' in value or any(ord(c) < 32 for c in value):
        raise Rejected('Unsafe archive path')
    if value.startswith('/') or any(p in ('', '.', '..') for p in value.rstrip('/').split('/')):
        raise Rejected('Noncanonical archive path')
    if len(value.encode('utf8')) > 4096 or len(PurePosixPath(value).parts) > 8:
        raise Rejected('Archive path limit')
    return value.rstrip('/')


class Bounded:
    def __init__(self, stream, limit):
        self.stream, self.limit, self.used = stream, limit, 0

    def read(self, count):
        if self.used + count > self.limit:
            raise Rejected('Expanded archive limit')
        data = self.stream.read(count)
        self.used += len(data)
        return data

    def exact(self, count):
        data = self.read(count)
        if len(data) != count:
            raise Rejected('Truncated archive')
        return data


def number(field):
    value = field.rstrip(b'\x00 ').lstrip(b' ')
    if not value or not re.fullmatch(b'[0-7]+', value):
        raise Rejected('Unsupported tar numeric field')
    return int(value, 8)


def entries(compressed, limits):
    # Explicit headers avoid unbounded GNU/PAX metadata reads inside generic tar readers.
    with gzip.GzipFile(fileobj=io.BytesIO(compressed)) as gz:
        stream = Bounded(gz, limits.expanded)
        names = set()
        pending = None
        for index in range(limits.members + 1):
            header = stream.exact(512)
            if not any(header):
                if pending is not None or any(stream.exact(512)):
                    raise Rejected('Invalid archive end marker')
                # Consume to validate gzip CRC and reject payloads hidden after the tar end.
                while True:
                    tail = stream.read(min(65536, limits.expanded - stream.used))
                    if any(tail):
                        raise Rejected('Data after archive end')
                    if not tail:
                        return
                    if stream.used == limits.expanded:
                        raise Rejected('Expanded archive limit')
            if index == limits.members:
                raise Rejected('Archive member limit')
            if number(header[148:156]) != sum(header[:148]) + 8 * 32 + sum(header[156:]):
                raise Rejected('Invalid tar checksum')
            size = number(header[124:136])
            if size > limits.member:
                raise Rejected('Archive member size limit')
            kind = header[156:157]
            if kind not in (b'0', b'\0', b'5', b'L'):
                raise Rejected('Links, devices, sparse and PAX entries are unsupported')
            if any(header[157:257]):
                raise Rejected('Unexpected tar link target')
            if kind == b'L' and size > 4096:
                raise Rejected('GNU name limit')
            payload = stream.exact(size)
            stream.exact((-size) % 512)
            if kind == b'L':
                if pending is not None:
                    raise Rejected('Invalid GNU long name')
                # The inherited TarArchiveWriter writes bare ASCII long names,
                # whereas GNU tar normally includes a terminal NUL.
                pending = (payload[:-1] if payload.endswith(b'\0') else payload).decode('utf8')
                path_name(pending)
                continue
            base = header[:100].split(b'\0', 1)[0].decode('utf8')
            prefix = header[345:500].split(b'\0', 1)[0].decode('utf8') if header[257:262] == b'ustar' else ''
            name = path_name(pending if pending is not None else (prefix + '/' if prefix else '') + base)
            pending = None
            if name in names:
                raise Rejected('Duplicate archive path')
            names.add(name)
            if kind == b'5':
                if size:
                    raise Rejected('Nonempty directory entry')
                continue
            yield name, payload
        raise Rejected('Archive member limit')


def xml(data, limits):
    # The native serializer sometimes declares UTF-16 while emitting UTF-8.
    # Decode actual bytes before parsing, then prohibit DTD/entities in every encoding.
    text = data.decode('utf16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')
    if '\x00' in text or re.search(r'<!\s*(DOCTYPE|ENTITY)', text, re.I):
        raise Rejected('DTD/entities or unsupported XML encoding')
    # Check events as bounded chunks are parsed, before allocating the full tree.
    parser = ET.XMLPullParser(events=('start', 'end'))
    root, depth, count = None, 0, 0
    for offset in range(0, len(text), 4096):
        parser.feed(text[offset:offset + 4096])
        for event, node in parser.read_events():
            if event == 'end':
                depth -= 1
                continue
            depth += 1
            count += 1
            if count > limits.xml_nodes or depth > limits.xml_depth:
                raise Rejected('XML structure limit')
            if not isinstance(node.tag, str) or node.tag.startswith('{'):
                raise Rejected('Namespaced scene elements unsupported')
            if root is None:
                root = node
    parser.close()
    if root is None or depth:
        raise Rejected('Incomplete XML')
    return root


def text_of(node, name, default=''):
    child = node.find(name)
    return ''.join(child.itertext()).strip() if child is not None else default


def identity(value):
    try:
        parsed = uuid.UUID(value)
    except ValueError as error:
        raise Rejected('Invalid scene identity') from error
    if str(parsed) == ZERO:
        raise Rejected('Zero scene identity')
    return str(parsed)


def vector(node, name, axes='XYZ'):
    value = node.find(name)
    if value is None:
        raise Rejected('Missing native transform')
    result = [float(text_of(value, axis)) for axis in axes]
    if not all(math.isfinite(v) and abs(v) <= 65536 for v in result):
        raise Rejected('Invalid native transform')
    return result


def passive(root, counts):
    # Remove *all* task inventory, including nested object assets. This avoids retained
    # executable references under misleading inventory types. Original bytes remain intact.
    for parent in list(root.iter()):
        for child in list(parent):
            if child.tag in ('TaskInventory', 'SavedScriptState', 'ScriptState', 'ScriptStates', 'Media'):
                counts[child.tag] += 1
                parent.remove(child)
            elif child.tag in ('MediaURL', 'MusicURL'):
                child.text = ''
            elif child.tag == 'DisableScripts':
                child.text = 'True'
    return ET.tostring(root, encoding='utf8', xml_declaration=True)


def prepare(source, output, limits=Limits()):
    source, output = Path(source), Path(output)
    flags = os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0)
    with os.fdopen(os.open(source, flags), 'rb') as inp:
        info = os.fstat(inp.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_size > limits.compressed:
            raise Rejected('Source must be a bounded regular file')
        compressed = inp.read(limits.compressed + 1)
        if len(compressed) > limits.compressed:
            raise Rejected('Compressed archive limit')
    if output.exists() or output.is_symlink():
        raise Rejected('Output already exists')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.oar-', dir=output.parent) as scratch:
        stage = Path(scratch) / 'package'
        stage.mkdir(mode=0o700)
        counts, removed, asset_types = Counter(), Counter(), Counter()
        members, objects, part_ids, asset_ids = [], [], set(), set()
        archive_version = None
        target = stage / 'staging.oar'
        with target.open('xb') as file:
            os.chmod(target, 0o600)
            with gzip.GzipFile(filename='', mode='wb', fileobj=file, mtime=0) as gz, tarfile.open(fileobj=gz, mode='w|', format=tarfile.USTAR_FORMAT) as tar:
                for name, data in entries(compressed, limits):
                    category = name.split('/')[0]
                    counts[category] += 1
                    record = dict(path=name, bytes=len(data), sha256=digest(data))
                    members.append(record)
                    out_name, transformed = name, data
                    if name == 'archive.xml':
                        root = xml(data, limits)
                        archive_version = [int(root.get('major_version', '-1')), int(root.get('minor_version', '-1'))]
                        if root.tag != 'archive' or archive_version not in ([0, 7], [0, 8]):
                            raise Rejected('Only single-region OAR 0.7/0.8 is supported')
                        size = root.findtext('region_info/size_in_meters', '256,256')
                        if size != '256,256':
                            raise Rejected('Only 256m staging regions are supported')
                        transformed = passive(root, removed)
                    elif category == 'assets':
                        match = re.fullmatch(r'assets/([0-9a-fA-F-]{36})_(.+)', name)
                        if not match or match[2] not in EXTENSIONS:
                            raise Rejected('Unknown native asset type')
                        asset_id = identity(match[1])
                        if asset_id in asset_ids:
                            raise Rejected('Duplicate asset UUID')
                        asset_ids.add(asset_id)
                        ext = match[2]
                        asset_types[ext] += 1
                        if ext in EXECUTABLE or ext == 'object.xml':
                            record['quarantined'] = True
                            # Inventory-object assets can recursively contain executable
                            # inventories. They are not needed for this passive scene restore.
                            removed['inventory_object_assets' if ext == 'object.xml' else 'executable_assets'] += 1
                            continue
                        if ext == 'material.xml':
                            xml(data, limits)
                    elif category == 'objects' and name.endswith('.xml'):
                        root = xml(data, limits)
                        if root.tag != 'SceneObjectGroup':
                            raise Rejected('Unsupported native object document')
                        parts = list(root.iter('SceneObjectPart'))
                        if not parts:
                            raise Rejected('Object has no parts')
                        object_record = dict(path=name, parts=[])
                        for part in parts:
                            part_id = identity(text_of(part, 'UUID'))
                            if part_id in part_ids:
                                raise Rejected('Duplicate scene part UUID')
                            part_ids.add(part_id)
                            scale = vector(part, 'Scale')
                            if any(v <= 0 or v > 256 for v in scale):
                                raise Rejected('Invalid native scale')
                            object_record['parts'].append(dict(id=part_id, name=part.findtext('Name', ''),
                                link_number=int(text_of(part, 'LinkNum', '0')),
                                position=vector(part, 'GroupPosition'), offset=vector(part, 'OffsetPosition'),
                                scale=scale, rotation=vector(part, 'RotationOffset', 'XYZW'),
                                creator=text_of(part, 'CreatorID'), owner=text_of(part, 'OwnerID'),
                                creator_data=part.findtext('CreatorData', '')))
                        objects.append(object_record)
                        out_name = 'objects/' + object_record['parts'][0]['id'] + '.xml'
                        transformed = passive(root, removed)
                    elif category in ('settings', 'landdata') and name.endswith('.xml'):
                        root = xml(data, limits)
                        expected = 'RegionSettings' if category == 'settings' else 'LandData'
                        if root.tag != expected:
                            raise Rejected('Unsupported native settings/parcel document')
                        if category == 'settings':
                            general = root.find('General')
                            if general is None:
                                raise Rejected('Missing region general settings')
                            if general.find('DisableScripts') is None:
                                ET.SubElement(general, 'DisableScripts').text = 'True'
                        transformed = passive(root, removed)
                        out_name = category + '/' + digest(name.encode())[:32] + '.xml'
                    elif category == 'terrains' and name.endswith('.r32'):
                        if len(data) != 256 * 256 * 4:
                            raise Rejected('Unexpected terrain size')
                        if any(not math.isfinite(v) or abs(v) > 65536
                               for (v,) in struct.iter_unpack('<f', data)):
                            raise Rejected('Invalid native terrain height')
                        out_name = 'terrains/region.r32'
                    else:
                        raise Rejected('Unsupported archive member category')
                    if len(out_name.encode('ascii')) > 99:
                        raise Rejected('Normalized native path limit')
                    record.update(staging_path=out_name, staging_sha256=digest(transformed))
                    member = tarfile.TarInfo(out_name)
                    member.size, member.mode, member.mtime = len(transformed), 0o600, 0
                    tar.addfile(member, io.BytesIO(transformed))
        if archive_version is None or counts['settings'] != 1 or counts['terrains'] != 1 or not objects:
            raise Rejected('Incomplete single-region archive')
        report = dict(schema_version=1, converter='mutsea.oar.quarantine/0.2.0',
            source_sha256=digest(compressed), source_bytes=len(compressed), archive_version=archive_version,
            staging_sha256=digest(target.read_bytes()), member_counts=dict(counts), asset_types=dict(asset_types),
            object_groups=len(objects), scene_parts=len(part_ids), removed=dict(removed),
            runtime_requirements=dict(isolated=True, script_engines_enabled=False, harbor_mounts=False),
            limitations=['Native dependency closure and asset decoding require separate validation.',
                'Source ownership/IDs retained for isolated restore; no production instance remap.',
                'Task inventory and nested object assets are excluded; no inventory compatibility claim.',
                'License clearance and visual quality are separate gates; not a WorldTemplate.'],
            members=members, objects=objects)
        report_path = stage / 'report.json'
        with report_path.open('x') as out:
            os.chmod(report_path, 0o600)
            json.dump(report, out, indent=2, ensure_ascii=True)
            out.write('\n')
        # Destination remains new. Refuse a concurrent directory/file/symlink rather than replacing it.
        output.mkdir(mode=0o700)
        for path in stage.iterdir():
            path.rename(output / path.name)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    try:
        result = prepare(args.source, args.output)
        print(json.dumps({k: result[k] for k in ('source_sha256', 'staging_sha256', 'object_groups', 'scene_parts', 'asset_types', 'removed')}))
    except (Rejected, OSError, ValueError, ET.ParseError, EOFError) as error:
        parser.exit(1, 'OAR rejected: ' + str(error) + '\n')
