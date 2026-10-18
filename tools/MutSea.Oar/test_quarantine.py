# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import gzip
import io
from pathlib import Path
import tarfile
import tempfile
import struct
import unittest
import xml.etree.ElementTree as ET
from quarantine import Limits, Rejected, prepare, xml

ID = 'e09bac3a-f595-46ca-a676-0e984b2b01a9'
SCRIPT = '5a5f25d2-dede-4605-8eef-e1d880e2270d'
OBJECT = f'''<SceneObjectGroup><SceneObjectPart><UUID><UUID>{ID}</UUID></UUID>
<Name>Test bench</Name><Scale><X>1</X><Y>2</Y><Z>1</Z></Scale>
<GroupPosition><X>128</X><Y>128</Y><Z>25</Z></GroupPosition>
<OffsetPosition><X>0</X><Y>0</Y><Z>0</Z></OffsetPosition>
<RotationOffset><X>0</X><Y>0</Y><Z>0</Z><W>1</W></RotationOffset>
<TaskInventory><TaskInventoryItem><AssetID>{SCRIPT}</AssetID><Type>10</Type></TaskInventoryItem></TaskInventory>
<Shape><Media><URL>https://example.invalid</URL></Media></Shape></SceneObjectPart>
<SavedScriptState UUID="{SCRIPT}"><State>hostile</State></SavedScriptState></SceneObjectGroup>'''.encode()


def fixture(extra=(), object_data=OBJECT, control=None, terrain=None):
    members = [('archive.xml', control or b'<?xml version="1.0" encoding="utf-16"?><archive major_version="0" minor_version="8"><region_info><size_in_meters>256,256</size_in_meters></region_info></archive>'),
        ('settings/region.xml', b'<RegionSettings><General><DisableScripts>False</DisableScripts></General></RegionSettings>'),
        ('terrains/region.r32', terrain if terrain is not None else bytes(256*256*4)), ('objects/bench.xml', object_data), *extra]
    out = io.BytesIO()
    with tarfile.open(fileobj=out, mode='w', format=tarfile.USTAR_FORMAT) as tar:
        for name, data in members:
            if isinstance(data, tarfile.TarInfo):
                data.name = name
                tar.addfile(data)
            else:
                m = tarfile.TarInfo(name)
                m.size = len(data)
                tar.addfile(m, io.BytesIO(data))
    return gzip.compress(out.getvalue(), mtime=0)


class QuarantineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.source = self.root/'input.oar'

    def tearDown(self):
        self.tmp.cleanup()

    def run_fixture(self, content, limits=Limits()):
        self.source.write_bytes(content)
        return prepare(self.source, self.root/'out', limits)

    def reject(self, content, limits=Limits()):
        with self.assertRaises((Rejected, ValueError, ET.ParseError, EOFError, OSError)):
            self.run_fixture(content, limits)
        self.assertFalse((self.root/'out').exists())

    def test_passive_derivative_preserves_transforms_and_provenance(self):
        content = fixture([(f'assets/{SCRIPT}_script.lsl', b'default{state_entry(){}}'),
            (f'assets/{ID}_object.xml', OBJECT)])
        report = self.run_fixture(content)
        self.assertEqual(report['scene_parts'], 1)
        self.assertEqual(report['objects'][0]['parts'][0]['position'], [128,128,25])
        self.assertEqual(report['removed'], {'TaskInventory':1, 'Media':1, 'SavedScriptState':1, 'executable_assets':1, 'inventory_object_assets':1})
        self.assertEqual(self.source.read_bytes(), content)
        with tarfile.open(self.root/'out/staging.oar', 'r:gz') as tar:
            for m in tar:
                data = tar.extractfile(m).read()
                self.assertNotIn(b'TaskInventory', data)
                self.assertNotIn(b'SavedScriptState', data)
                self.assertNotIn(b'<Media>', data)
                self.assertNotIn('script.lsl', m.name)
                if m.name.startswith('settings/'):
                    self.assertEqual(ET.fromstring(data).findtext('General/DisableScripts'), 'True')

    def test_legacy_single_region_07_uses_same_validation(self):
        content = fixture(control=b'<archive major_version="0" minor_version="7"/>')
        self.assertEqual([0, 7], self.run_fixture(content)['archive_version'])
        __import__('shutil').rmtree(self.root/'out')
        self.reject(fixture(control=b'<archive major_version="0" minor_version="6"/>'))
        self.reject(fixture(control=b'<archive major_version="0" minor_version="7"><region_info><size_in_meters>512,512</size_in_meters></region_info></archive>'))

    def test_reproducible_derivative(self):
        content = fixture()
        first = self.run_fixture(content)
        second = prepare(self.source, self.root/'second')
        self.assertEqual(first['staging_sha256'], second['staging_sha256'])

    def test_native_name_whitespace_is_preserved(self):
        report=self.run_fixture(fixture(object_data=OBJECT.replace(b'Test bench',b'Test bench ')))
        self.assertEqual(report['objects'][0]['parts'][0]['name'],'Test bench ')

    def test_normalized_object_path_uses_validated_uuid(self):
        report=self.run_fixture(fixture(object_data=OBJECT.replace(ID.encode(), ('\n'+ID.upper()+'\n').encode())))
        member=next(m for m in report['members'] if m['path']=='objects/bench.xml')
        self.assertEqual(member['staging_path'],f'objects/{ID}.xml')

    def test_paths(self):
        for path in ('../escape', '/absolute', 'objects/../escape', 'objects//escape', 'C:/escape', 'objects\\escape'):
            with self.subTest(path=path):
                self.reject(fixture([(path,b'bad')]))

    def test_links_devices_pax(self):
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.CHRTYPE, tarfile.FIFOTYPE, tarfile.XHDTYPE):
            m = tarfile.TarInfo();m.type=kind;m.linkname='objects/bench.xml'
            with self.subTest(kind=kind):self.reject(fixture([('objects/link.xml',m)]))

    def test_duplicate_paths(self):
        self.reject(fixture([('objects/bench.xml',OBJECT)]))

    def test_duplicate_scene_ids(self):
        self.reject(fixture([('objects/another.xml',OBJECT)]))

    def test_duplicate_asset_ids(self):
        self.reject(fixture([(f'assets/{ID}_texture.jp2',b'image'),(f'assets/{ID}_sound.ogg',b'sound')]))

    def test_unsupported_asset_and_region(self):
        self.reject(fixture([(f'assets/{ID}_unknown.bin',b'unknown')]))
        self.reject(fixture(control=b'<archive major_version="1" minor_version="0"/>'))

    def test_compressed_and_expanded_limits(self):
        self.reject(fixture(), Limits(compressed=100))
        self.reject(fixture(), Limits(expanded=2048))

    def test_member_size_and_count(self):
        self.reject(fixture(), Limits(member=10000))
        self.reject(fixture(), Limits(members=2))

    def test_dtd_both_encodings(self):
        text='<!DOCTYPE archive [<!ENTITY x "boom">]><archive major_version="0" minor_version="8">&x;</archive>'
        for enc in ('utf8','utf16'):
            with self.subTest(enc=enc):self.reject(fixture(control=text.encode(enc)))

    def test_xml_depth_and_nodes(self):
        self.reject(fixture(), Limits(xml_depth=3))
        self.reject(fixture(), Limits(xml_nodes=5))

    def test_xml_structure_rejected_before_malformed_tail_is_parsed(self):
        # A parser that builds the whole tree first would report the malformed
        # tail instead of rejecting this earlier node/depth budget violation.
        for data in (b'<root>' + b'<node/>' * 2000 + b'<bad>',
                     b'<root>' + b'<node>' * 2000 + b'<bad>'):
            with self.subTest(prefix=data[:30]):
                with self.assertRaisesRegex(Rejected, 'XML structure limit'):
                    xml(data, Limits(xml_nodes=20, xml_depth=20))

    def test_nonfinite_and_excessive_terrain(self):
        for height in (float('nan'), float('inf'), -float('inf'), 100000):
            with self.subTest(height=height):
                self.reject(fixture(terrain=struct.pack('<f', height) + bytes(256*256*4 - 4)))

    def test_checksum_truncation_crc_and_hidden_tail(self):
        raw=bytearray(gzip.decompress(fixture()))
        raw[0] ^= 1
        self.reject(gzip.compress(raw))
        self.reject(fixture()[:-20])
        broken=bytearray(fixture());broken[-8] ^= 1
        self.reject(broken)
        self.reject(gzip.compress(gzip.decompress(fixture())+b'hidden'))

    def test_native_long_name_without_nul(self):
        raw = gzip.decompress(fixture())
        # Insert a native-style bounded GNU long-name header before the object.
        offset = raw.index(b'objects/bench.xml')
        name = ('objects/' + 'native-long-name-'*8 + '.xml').encode()
        h = bytearray(512)
        h[:13] = b'././@LongLink'
        h[124:136] = f'{len(name):011o}\0'.encode()
        h[156:157] = b'L'
        h[148:156] = b'        '
        h[148:156] = f'{sum(h):06o}\0 '.encode()
        content = gzip.compress(raw[:offset] + h + name + bytes((-len(name))%512) + raw[offset:])
        report = self.run_fixture(content)
        self.assertEqual(report['object_groups'], 1)
        self.assertEqual(report['objects'][0]['path'], name.decode())

    def test_nonfinite_transform(self):
        self.reject(fixture(object_data=OBJECT.replace(b'<X>128</X>',b'<X>nan</X>')))

    def test_no_overwrite_and_no_linked_source(self):
        self.run_fixture(fixture())
        report=(self.root/'out/report.json').read_bytes()
        with self.assertRaises(Rejected):prepare(self.source,self.root/'out')
        self.assertEqual((self.root/'out/report.json').read_bytes(), report)
        link=self.root/'linked.oar';link.symlink_to(self.source)
        with self.assertRaises(OSError):prepare(link,self.root/'new')


if __name__ == '__main__':unittest.main()
