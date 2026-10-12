# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import copy
import unittest
from runtime_report import reconcile, Rejected

OWNER='02f6b888-927a-4d6b-9dc4-508afcd82ebf'
SOURCE='fa7f662a-538a-48f7-9125-7210e2870d09'
TARGET='276a4660-6676-4d2f-a4db-2c039b71d5f2'

class MappingTests(unittest.TestCase):
    def setUp(self):
        p=dict(id=SOURCE,name='Bench',owner=SOURCE,link_number=0,position=[128,128,25],offset=[0,0,0],scale=[1,2,1],rotation=[0,0,0,1])
        self.source=dict(scene_parts=1,objects=[dict(parts=[p])])
        self.native=[dict(p,id=TARGET,group_id=TARGET,owner=OWNER,link_number=1)]
    def test_mapping_retains_source_and_does_not_mutate_inputs(self):
        result=reconcile(self.source,self.native,OWNER)
        self.assertEqual(result['objects'][0]['parts'][0]['id'],TARGET)
        self.assertEqual(result['runtime_mapping'][0]['source_id'],SOURCE)
        self.assertEqual(self.source['objects'][0]['parts'][0]['id'],SOURCE)
    def test_changed_transform_rejected(self):
        self.native[0]['scale']=[1,2,2]
        with self.assertRaises(Rejected):reconcile(self.source,self.native,OWNER)
    def test_foreign_owner_rejected(self):
        self.native[0]['owner']=SOURCE
        with self.assertRaises(Rejected):reconcile(self.source,self.native,OWNER)
    def test_duplicate_id_rejected(self):
        self.native.append(copy.deepcopy(self.native[0]))
        with self.assertRaises(Rejected):reconcile(self.source,self.native,OWNER)
    def test_ambiguous_stacked_groups_rejected(self):
        second=copy.deepcopy(self.source['objects'][0]);second['parts'][0]['id']=OWNER
        self.source['objects'].append(second);self.source['scene_parts']=2
        self.native.append(dict(self.native[0],id=OWNER,group_id=OWNER))
        with self.assertRaises(Rejected):reconcile(self.source,self.native,OWNER)

    def test_link_number_breaks_equal_geometry_ties(self):
        # Linked children can share name and geometry. Preserve their identities
        # by a retained native link number, never arbitrary list order.
        for link in (2,3):
            old=dict(self.source['objects'][0]['parts'][0],id=f'00000000-0000-0000-0000-{link:012d}',link_number=link)
            new=dict(old,id=f'10000000-0000-0000-0000-{link:012d}',group_id=TARGET,owner=OWNER)
            self.source['objects'][0]['parts'].append(old);self.native.insert(1,new)
        self.source['scene_parts']=3
        result=reconcile(self.source,self.native,OWNER)
        self.assertEqual([m['runtime_link_number'] for m in result['runtime_mapping']],[1,2,3])
        self.native[1]['link_number']=self.native[2]['link_number']=2
        with self.assertRaises(Rejected):reconcile(self.source,self.native,OWNER)

if __name__=='__main__':unittest.main()
