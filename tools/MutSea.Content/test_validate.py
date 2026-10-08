# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from validate import validate, InvalidPackage

class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root/'mesh.glb').write_bytes(b'explicit synthetic test fixture')
        payload = (self.root/'mesh.glb').read_bytes()
        self.data = dict(schema_version=1, package_id='mutsea.test', version='0.1.0', kind='asset',
            title='Test', author='Test', publisher='Test', license={'spdx':'CC0-1.0','attribution':''},
            provenance=[dict(origin='original', author='Test', source='test fixture', license='CC0-1.0', modifications='none')],
            dependencies=[], files=[dict(path='mesh.glb', sha256=hashlib.sha256(payload).hexdigest(), size_bytes=len(payload), role='runtime')],
            entities=[dict(local_id='chair',name='Chair',semantic_type='seating',description='Synthetic test chair',tags=[],
                affordances=['sit'],relationships=[],ownership='inherit_instance_owner')],
            budget=dict(triangles=12,materials=1,texture_bytes=0,payload_bytes=1024),
            definition=dict(scale='asset',units='meters',up_axis='Z',minimum_client_version='0.1.0'))
    def write(self, data=None):
        path = self.root/'package.json'
        path.write_text(json.dumps(self.data if data is None else data))
        return path
    def test_valid(self):
        self.assertEqual(validate(self.write())['package_id'], 'mutsea.test')
    def test_reject_corrupt_payload(self):
        (self.root/'mesh.glb').write_bytes(b'corrupt')
        with self.assertRaises(InvalidPackage): validate(self.write())
    def test_paths(self):
        for path in ('../mesh.glb','/etc/passwd','a\\b','./mesh.glb','a//mesh.glb'):
            with self.subTest(path=path):
                data=copy.deepcopy(self.data);data['files'][0]['path']=path
                with self.assertRaises(InvalidPackage): validate(self.write(data))
    def test_symlink(self):
        (self.root/'link.glb').symlink_to(self.root/'mesh.glb')
        self.data['files'][0]['path']='link.glb'
        with self.assertRaises(InvalidPackage): validate(self.write())
    def test_semantics_and_rights(self):
        for mutate in (lambda d:d.update(entities=[]),lambda d:d['license'].update(spdx='unknown'),
                       lambda d:d['entities'][0].update(ownership='admin'),
                       lambda d:d['entities'][0].update(affordances=['execute_python'])):
            data=copy.deepcopy(self.data);mutate(data)
            with self.subTest(data=data),self.assertRaises(InvalidPackage): validate(self.write(data))
    def test_dangling_relationship(self):
        self.data['entities'][0]['relationships']=[dict(type='part_of',target='missing')]
        with self.assertRaises(InvalidPackage): validate(self.write())
    def test_budget(self):
        self.data['budget']['payload_bytes']=0
        with self.assertRaises(InvalidPackage): validate(self.write())
    def test_incomplete_avatar(self):
        self.data['kind']='avatar'
        with self.assertRaises(InvalidPackage): validate(self.write())
    def test_world_spawn_nonfinite(self):
        self.data['kind']='world';self.data['definition']['spawn_points']=[[0,0,float('nan')]]
        with self.assertRaises(InvalidPackage): validate(self.write())
    def test_dependency_missing(self):
        self.data['dependencies']=[dict(package_id='mutsea.other',version='0.1.0',manifest_sha256='0'*64)]
        with self.assertRaises(InvalidPackage): validate(self.write())
    def test_dependency_identity(self):
        dep=copy.deepcopy(self.data);dep['package_id']='mutsea.wrong'
        path=self.root/'dep.json';path.write_text(json.dumps(dep))
        self.data['dependencies']=[dict(package_id='mutsea.other',version='0.1.0',manifest_sha256=hashlib.sha256(path.read_bytes()).hexdigest())]
        with self.assertRaises(InvalidPackage):validate(self.write(),{('mutsea.other','0.1.0'):path})
    def test_dependency_resolved(self):
        dep=copy.deepcopy(self.data);dep['package_id']='mutsea.other'
        path=self.root/'dep.json';path.write_text(json.dumps(dep))
        self.data['dependencies']=[dict(package_id='mutsea.other',version='0.1.0',manifest_sha256=hashlib.sha256(path.read_bytes()).hexdigest())]
        self.assertEqual(validate(self.write(),{('mutsea.other','0.1.0'):path})['dependencies'][0]['package_id'],'mutsea.other')
    def test_manifest_symlink(self):
        path=self.write();link=self.root/'link.json';link.symlink_to(path)
        with self.assertRaises(InvalidPackage):validate(link)
    def test_malformed_field_types(self):
        for mutate in (lambda d:d.update(kind=[]),lambda d:d['license'].update(spdx=[]),
                       lambda d:d['files'][0].update(path=[]),lambda d:d['definition'].update(scale=[]),
                       lambda d:d['entities'][0].update(affordances=[{}])):
            data=copy.deepcopy(self.data);mutate(data)
            with self.subTest(data=data),self.assertRaises(InvalidPackage):validate(self.write(data))
    def test_duplicate_fields(self):
        path=self.root/'package.json';path.write_text('{"schema_version":1,"schema_version":1}')
        with self.assertRaises(InvalidPackage):validate(path)
    def test_unknown_executable_field(self):
        self.data['python']='arbitrary execution'
        with self.assertRaises(InvalidPackage):validate(self.write())

if __name__=='__main__': unittest.main()
