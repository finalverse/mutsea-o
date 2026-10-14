#!/usr/bin/env python3
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
import uuid
import hashlib

spec=importlib.util.spec_from_file_location('admit',Path(__file__).with_name('admit-pilot.py'))
admit=importlib.util.module_from_spec(spec);spec.loader.exec_module(admit)

class AdmissionStaging(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name);self.config=self.root/'config';(self.config/'accounts').mkdir(parents=True);package=self.config/'genesis/auren';package.mkdir(parents=True)
  compiled=json.dumps(dict(representation='attachment',package_id='finalverse.genesis.auren',version='0.1.0')).encode();(package/'compiled.json').write_bytes(compiled)
  self.write(self.config/'avatar-choices.json',[dict(id='auren',directory='/config/genesis/auren',package_id='finalverse.genesis.auren',version='0.1.0',compiled_sha256=hashlib.sha256(compiled).hexdigest())])
  self.current=self.config/'accounts/newcomers.json';self.write(self.current,{'accounts':[]});self.before=self.current.read_bytes()
  self.request=self.root/'request.json';self.data=dict(admission_id=str(uuid.uuid4()),first_name='Pilot',last_name='Builder',password='synthetic-private-only',avatar='auren',experience='harbor');self.write(self.request,self.data)
 def tearDown(self):self.temp.cleanup()
 def write(self,path,value):path.write_text(json.dumps(value));path.chmod(0o600)
 def refused(self):
  before=self.current.read_bytes()
  with self.assertRaises((ValueError,OSError)):admit.stage(self.config,self.request)
  self.assertEqual(self.current.read_bytes(),before)
 def test_staging_preserves_native_fields_and_private_permissions(self):
  result=admit.stage(self.config,self.request);self.assertEqual(result['status'],'staged_for_native_provisioning');account=json.loads(self.current.read_text())['accounts'][0];self.assertEqual(account['admission_id'],self.data['admission_id']);self.assertEqual(account['genesis'],'/config/genesis/auren');self.assertEqual(self.current.stat().st_mode&0o777,0o600)
 def test_same_request_is_idempotent_without_rewriting_file(self):
  admit.stage(self.config,self.request);before=self.current.read_bytes();self.assertEqual(admit.stage(self.config,self.request)['status'],'already_staged');self.assertEqual(before,self.current.read_bytes())
 def test_changed_password_or_choice_cannot_repurpose_admission(self):
  admit.stage(self.config,self.request);self.data['password']='changed-private-password';self.write(self.request,self.data);self.refused()
 def test_unknown_character_refused(self):self.data['avatar']='lumi';self.write(self.request,self.data);self.refused()
 def test_unknown_experience_refused(self):self.data['experience']='arbitrary-region';self.write(self.request,self.data);self.refused()
 def test_changed_package_pin_refused(self):(self.config/'genesis/auren/compiled.json').write_text('{}');self.refused()
 def test_linked_request_refused(self):linked=self.root/'linked';linked.symlink_to(self.request);self.request=linked;self.refused()
 def test_world_readable_secret_request_refused(self):self.request.chmod(0o644);self.refused()
 def test_unknown_request_privilege_field_refused(self):self.data['admin']=True;self.write(self.request,self.data);self.refused()
 def test_other_admission_with_same_name_refused(self):
  admit.stage(self.config,self.request);self.data['admission_id']=str(uuid.uuid4());self.write(self.request,self.data);self.refused()
 def test_live_operator_lock_refuses_without_lost_updates(self):
  import fcntl
  with open(self.config/'accounts/.admission.lock','w') as lock:
   fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB);self.refused()
 def test_duplicate_json_keys_refused(self):self.request.write_text('{"password":"first","password":"second"}');self.refused()

if __name__=='__main__':unittest.main()
