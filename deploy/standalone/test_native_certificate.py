# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import importlib.util
import unittest
import json
import tempfile
from unittest.mock import patch
from types import SimpleNamespace
from pathlib import Path
spec=importlib.util.spec_from_file_location('refresh',Path(__file__).with_name('refresh-native-certificate.py'))
refresh=importlib.util.module_from_spec(spec);spec.loader.exec_module(refresh)
class NativeCertificateTests(unittest.TestCase):
    def test_empty_region(self):
        self.assertEqual(refresh.agent_counts('Total agents in region Finalverse Harbor: 0 (root 0, child 0)'),0)
    def test_child_and_root_both_block(self):
        self.assertEqual(refresh.agent_counts('Total agents in region Finalverse Harbor: 2 (root 1, child 1)'),2)
    def test_unknown_console_fails_closed(self):
        with self.assertRaises(RuntimeError):refresh.agent_counts('Disconnected console')
    def test_unexpected_multiple_regions_fail_closed(self):
        with self.assertRaises(RuntimeError):refresh.agent_counts('Total agents in region A: 0 (root 0, child 0)\nTotal agents in region B: 0 (root 0, child 0)')
    def test_active_session_defers_without_export_or_restart(self):
        info=[dict(State=dict(Running=True),HostConfig=dict(RestartPolicy=dict(Name='unless-stopped')),Config=dict(Labels={'com.docker.compose.project':'finalverse-backend','com.docker.compose.service':'mutsea'}))]
        with tempfile.TemporaryDirectory() as root, patch.object(refresh,'source_cert',return_value='new'),patch.object(refresh,'loaded_cert',return_value='old'),patch.object(refresh,'run',return_value=SimpleNamespace(stdout=json.dumps(info).encode())) as run,patch.object(refresh,'console',side_effect=['Login in Harbor are enabled','Logins are disabled','Total agents in region Harbor: 1 (root 1, child 0)','Logins are enabled']) as console:
            refresh.refresh(Path(root))
            commands=[call.args[0] for call in run.call_args_list]
            self.assertFalse(any('openssl' in cmd or 'start' in cmd for cmd in commands))
            self.assertEqual(console.call_args_list[-1].args[0],'login enable')
            self.assertFalse(list(Path(root).rglob('*.p12')))
