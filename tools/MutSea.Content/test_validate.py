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
    def pbr(self):
        self.data['definition'].update(materials=[dict(local_id='wood',base_color_linear=[.31,.12,.04,1],
            metallic_factor=0,roughness_factor=.72,emissive_linear=[0,0,0],alpha_mode='OPAQUE',alpha_cutoff=.5,double_sided=False)],
            material_bindings={'chair':['wood']})
    def test_pbr_valid(self):
        self.pbr();self.assertEqual(validate(self.write())['definition']['materials'][0]['base_color_linear'][0],.31)
    def test_pbr_reject_nonfinite_unknown_external_and_unbound(self):
        self.pbr()
        for mutate in (lambda d:d['definition']['materials'][0].update(roughness_factor=float('nan')),
                       lambda d:d['definition']['materials'][0].update(base_color_linear=[1,1,1,True]),
                       lambda d:d['definition']['materials'][0].update(texture_uri='https://example.invalid/a.png'),
                       lambda d:d['definition'].update(material_bindings={'chair':['missing']}),
                       lambda d:d['definition'].update(material_bindings={}),
                       lambda d:d['definition']['materials'].append(copy.deepcopy(d['definition']['materials'][0]))):
            data=copy.deepcopy(self.data);mutate(data)
            with self.subTest(data=data),self.assertRaises(InvalidPackage):validate(self.write(data))
    def test_native_seat_contract(self):
        self.pbr();self.data['definition']['seating']={'chair':dict(capacity=1,position=[0,0,.1],rotation=[0,0,0,1])}
        validate(self.write())
        for mutate in (lambda d:d['definition']['seating']['chair'].update(capacity=2),
                       lambda d:d['definition']['seating']['chair'].update(position=[0,0,0]),
                       lambda d:d['definition']['seating']['chair'].update(rotation=[0,0,0,0]),
                       lambda d:d['entities'][0].update(affordances=['inspect'])):
            data=copy.deepcopy(self.data);mutate(data)
            with self.subTest(data=data),self.assertRaises(InvalidPackage):validate(self.write(data))
    def test_reduced_lods_require_pinned_payloads_and_reduction(self):
        self.data['definition']['lods']={key:dict(path='missing.dae',triangles=count) for key,count in [('medium',8),('low',4),('lowest',2)]}
        with self.assertRaises(InvalidPackage):validate(self.write())
        for value in self.data['definition']['lods'].values():value['path']='mesh.glb'
        validate(self.write())
        self.data['definition']['lods']['low']['triangles']=8
        with self.assertRaises(InvalidPackage):validate(self.write())

    def test_native_lights_reject_unbounded_unknown_and_nonsemantic_sources(self):
        self.pbr();self.data['entities'][0]['semantic_type']='lighting'
        self.data['definition']['lighting']={'chair':dict(color_linear=[1,.7,.4],intensity=.8,radius=3.5,falloff=1)}
        validate(self.write())
        for mutate in (lambda d:d['definition']['lighting']['chair'].update(radius=4.1),
                       lambda d:d['definition']['lighting']['chair'].update(color_linear=[1,True,.4]),
                       lambda d:d['definition']['lighting']['chair'].update(color_linear=[0,0,0]),
                       lambda d:d['definition']['lighting']['chair'].update(intensity=float('nan')),
                       lambda d:d['definition']['lighting']['chair'].update(projector_uri='https://example.invalid'),
                       lambda d:d['entities'][0].update(semantic_type='seating')):
            data=copy.deepcopy(self.data);mutate(data)
            with self.subTest(data=data),self.assertRaises(InvalidPackage):validate(self.write(data))
    def test_collision_hulls_are_bounded_and_cannot_hide_unknown_parts(self):
        self.pbr();self.data['definition']['collision']={'chair':dict(shape='box',minimum=[-.1,-.1,-.5],maximum=[.1,.1,.5])}
        validate(self.write())
        for shape in (dict(shape='box',minimum=[-.6,-.1,-.5],maximum=[.1,.1,.5]),
                      dict(shape='box',minimum=[float('nan'),0,0],maximum=[.1,.1,.1]),
                      dict(shape='box',minimum=[0,0,0],maximum=[0,.1,.1]),
                      dict(shape='none',minimum=[0,0,0]),dict(shape='mesh')):
            data=copy.deepcopy(self.data);data['definition']['collision']['chair']=shape
            with self.subTest(shape=shape),self.assertRaises(InvalidPackage):validate(self.write(data))
        self.data['definition']['collision']['missing']=dict(shape='none')
        with self.assertRaises(InvalidPackage):validate(self.write())

if __name__=='__main__': unittest.main()
