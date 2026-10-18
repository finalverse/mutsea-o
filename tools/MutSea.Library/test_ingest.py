# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from ingest import acquire, catalog, fetch, safe_url, collisions
sys.path.insert(0, str(Path(__file__).parents[1] / 'MutSea.Oar'))
from test_quarantine import fixture, ID

class IngestTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.entry = dict(id='test.garden', title='Garden', url='https://files.zadaroo.com/oars/Garden.tgz',
                          license='CC0-1.0', license_url='https://www.zadaroo.com/licensing.html',
                          admission='compatible', platform='opensimulator', expected_sha256=None)
    def test_urls(self):
        for u in ('http://files.zadaroo.com/a','https://127.0.0.1/a','https://user:pass@files.zadaroo.com/a','https://files.zadaroo.com:8443/a'):
            with self.assertRaises(ValueError): safe_url(u)
    def download(self,url,path,limit): path.write_bytes(fixture())
    def test_quarantine_is_not_admission(self):
        r=acquire(self.entry,self.root,1000000,self.download)
        self.assertEqual(r['status'],'quarantined');self.assertEqual(r['redistribution'],'not-admitted')
        self.assertEqual(r['runtime'],'not-restored');self.assertTrue((self.root/'test.garden/passive/staging.oar').exists())
        self.assertEqual(collisions(self.root),[])
    def test_pin_mismatch_retains_source_without_derivative(self):
        r=acquire(dict(self.entry,expected_sha256='0'*64),self.root,1000000,self.download)
        self.assertEqual(r['status'],'rejected');self.assertFalse((self.root/'test.garden/passive').exists())
    def test_unsupported_and_mixed_not_downloaded(self):
        def never(*args): self.fail('Deferred source downloaded')
        for status in ('mixed-license-review','unsupported-format'):
            self.assertEqual('deferred',acquire(dict(self.entry,admission=status),self.root,1000000,never)['status'])
        self.assertEqual('deferred',acquire(self.entry,self.root,0,never)['status'])
    def test_rerun_preserves_previous_run(self):
        acquire(self.entry,self.root,1000000,self.download)
        with self.assertRaises(FileExistsError): acquire(self.entry,self.root,1000000,self.download)
    def test_catalog_rejects_path_escape_duplicate_and_pin(self):
        p=self.root/'catalog.json'
        for entries in ([dict(self.entry,id='../escape')],[self.entry,self.entry],[dict(self.entry,expected_sha256='bad')]):
            p.write_text(json.dumps(dict(schema_version=1,entries=entries)))
            with self.assertRaises(ValueError): catalog(p)
    def test_chunked_size_bound_and_partial_cleanup(self):
        class Response(io.BytesIO):
            headers={}
            def geturl(self): return self.url
        r=Response(b'x'*100);r.url=self.entry['url']
        with patch('urllib.request.OpenerDirector.open',return_value=r):
            with self.assertRaises(ValueError): fetch(self.entry['url'],self.root/'partial',99)
        self.assertFalse((self.root/'partial').exists())
    def test_cross_archive_uuid_collision(self):
        for key,digest in (('one','1'*64),('two','2'*64)):
            p=self.root/key/'passive';p.mkdir(parents=True)
            (p/'report.json').write_text(json.dumps(dict(members=[dict(staging_path=f'assets/{ID}_texture.jp2',staging_sha256=digest)])))
        self.assertEqual(collisions(self.root)[0]['asset_id'],ID)

if __name__=='__main__': unittest.main()
