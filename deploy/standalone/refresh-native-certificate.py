#!/usr/bin/env python3
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
"""Rotate only the dedicated Finalverse simulator's TLS identity.

Run as root under the installed service. It drains native login admission, defers
while real sessions remain, saves an owned backup, and requests native shutdown.
It never deletes volumes or restores old user data. The same timer retries a
deferred renewal. Certificate/private configuration values never enter logs.
"""
import argparse
import fcntl
import hashlib
import json
import os
import pty
import re
import select
import shutil
import socket
import ssl
import subprocess
import time
from pathlib import Path

SIM='finalverse-backend-mutsea-1'
PG='finalverse-backend-postgres-1'
LINEAGE=Path('/etc/letsencrypt/live/mutsea.com')

def run(args, **kw):
    return subprocess.run(args,check=True,capture_output=True,timeout=120,**kw)

def console(command, expected, wait=15):
    # Native console keeps stdin open; closing this attach does not signal PID1.
    master,slave=pty.openpty()
    process=subprocess.Popen(['docker','attach','--sig-proxy=false',SIM],stdin=slave,stdout=slave,stderr=slave,start_new_session=True)
    os.close(slave);output=b''
    try:
        # A subsequent native command proves all region rows were emitted. Do
        # not stop after the first region's count and overlook another session.
        query=command+'\n'+('show uptime\n' if command=='show users full' else '')
        time.sleep(.4);os.write(master,query.encode());deadline=time.monotonic()+wait
        while time.monotonic()<deadline:
            if select.select([master],[],[],.2)[0]:
                try: output+=os.read(master,65536)
                except OSError: break
                if len(output)>262144:raise RuntimeError('Native console output exceeded limit')
                value=output.decode(errors='replace');matched=re.search(expected,value)
                if matched and (command!='show users full' or 'That is an elapsed time of' in value[matched.end():]):return value
        raise RuntimeError('Native console did not acknowledge maintenance command')
    finally:
        os.close(master);process.terminate()
        try:process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            # Only kill the disposable attach CLI, never the simulator.
            process.kill();process.wait(timeout=2)

def agent_counts(output):
    counts=re.findall(r'Total agents in region [^\r\n]+: (\d+) \(root \d+, child \d+\)',output)
    if len(counts)!=1: raise RuntimeError('Expected exactly one dedicated native region')
    return int(counts[0])

def source_cert():
    cert=run(['openssl','x509','-in',str(LINEAGE/'fullchain.pem'),'-outform','DER']).stdout
    run(['openssl','x509','-in',str(LINEAGE/'fullchain.pem'),'-noout','-checkend','172800'])
    return hashlib.sha256(cert).hexdigest()

def loaded_cert():
    context=ssl.create_default_context()
    with socket.create_connection(('127.0.0.1',19100),timeout=5) as raw:
        with context.wrap_socket(raw,server_hostname='grid.mutsea.com') as tls:
            return hashlib.sha256(tls.getpeercert(binary_form=True)).hexdigest()

def verify_gateway(expected):
    # Verify the renewed frontend and its verified-TLS native proxy together,
    # using only the loopback allowance and a public read-only grid-info path.
    with socket.create_connection(('127.0.0.1',443),timeout=5) as raw:
        with ssl.create_default_context().wrap_socket(raw,server_hostname='grid.mutsea.com') as tls:
            if hashlib.sha256(tls.getpeercert(binary_form=True)).hexdigest()!=expected:raise OSError('Gateway certificate has not refreshed')
            tls.sendall(b'GET /get_grid_info HTTP/1.1\r\nHost: grid.mutsea.com\r\nConnection: close\r\n\r\n')
            reply=b''
            while chunk:=tls.recv(4096):
                reply+=chunk
                if len(reply)>65536:raise OSError('Gateway response exceeds limit')
            if not reply.startswith(b'HTTP/1.1 200 ') or b'<gridinfo>' not in reply:raise OSError('Verified native gateway is unavailable')

def fingerprints(root):
    result={}
    for table in ('prims','primshapes','inventoryitems','inventoryfolders','avatars','useraccounts'):
        data=run(['docker','exec',PG,'psql','-U','postgres','-d','finalverse','-Atc',f'SELECT row_to_json(t)::text FROM {table} t ORDER BY row_to_json(t)::text']).stdout
        result[table]=hashlib.sha256(data).hexdigest()
    result['journals']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'data/finalverse').glob('*.jsonl')}
    return result

def refresh(root, force=False):
    expected=source_cert()
    try:current=loaded_cert()==expected
    except (OSError,ssl.SSLError):current=False
    if current and not force:
        verify_gateway(expected)
        print('event=native_certificate_current');return
    info=json.loads(run(['docker','inspect',SIM]).stdout)[0]
    labels=info['Config'].get('Labels',{})
    if labels.get('com.docker.compose.project')!='finalverse-backend' or labels.get('com.docker.compose.service')!='mutsea':
        raise RuntimeError('Expected the dedicated Finalverse simulator')
    if not info['State']['Running']: raise RuntimeError('Simulator is not running; operator review required')
    prior_policy=info['HostConfig']['RestartPolicy']['Name']
    admission=console('login status',r'Login in [^\r\n]+ are (enabled|disabled)')
    if re.search(r'Login in [^\r\n]+ are disabled',admission):
        print('event=native_certificate_deferred reason=operator_admission_disabled');return
    # Disable only native region admission; existing sessions continue normally.
    console('login disable',r'Logins.*disabled|logins.*disabled')
    stopped=False;may_enable=True
    stage=root/'config/mutsea.p12.next';cert=root/'config/mutsea.p12'
    archive=root/'backups'/time.strftime('certificate-%Y%m%dT%H%M%SZ',time.gmtime())
    try:
        if agent_counts(console('show users full',r'Total agents in region [^\r\n]+: \d+ \(root \d+, child \d+\)')):
            print('event=native_certificate_deferred reason=active_sessions');return
        # One extra check covers a circuit entering during the admission fence.
        time.sleep(1)
        if agent_counts(console('show users full',r'Total agents in region [^\r\n]+: \d+ \(root \d+, child \d+\)')):
            print('event=native_certificate_deferred reason=entering_session');return
        archive.mkdir(mode=0o700)
        run(['openssl','pkcs12','-export','-in',str(LINEAGE/'fullchain.pem'),'-inkey',str(LINEAGE/'privkey.pem'),'-out',str(stage),'-passout','file:'+str(root/'config/certificate-password')])
        os.chown(stage,1000,1000);stage.chmod(0o600)
        shutil.copy2(cert,archive/'previous.p12');os.chown(archive/'previous.p12',0,0);(archive/'previous.p12').chmod(0o600)
        before=fingerprints(root)
        run(['docker','update','--restart=no',SIM])
        may_enable=False
        console('shutdown',r'Shutdown|shutdown',wait=15)
        deadline=time.monotonic()+60
        while time.monotonic()<deadline:
            status=json.loads(run(['docker','inspect',SIM]).stdout)[0]['State']
            if not status['Running']:
                stopped=True
                if status['ExitCode']!=0: raise RuntimeError('Native shutdown did not exit cleanly')
                break
            time.sleep(.5)
        if not stopped: raise RuntimeError('Native shutdown exceeded its deadline')
        # The backup is from the freshly stopped world, never an old snapshot.
        with (archive/'database.dump').open('wb') as backup:
            subprocess.run(['docker','exec',PG,'pg_dump','-U','postgres','-d','finalverse','-Fc'],stdout=backup,stderr=subprocess.PIPE,check=True,timeout=120)
        (archive/'database.dump').chmod(0o600)
        os.replace(stage,cert)
        run(['docker','start',SIM]);stopped=False
        deadline=time.monotonic()+60
        while time.monotonic()<deadline:
            try:
                if loaded_cert()==expected:
                    verify_gateway(expected);break
            except (OSError,ssl.SSLError):pass
            time.sleep(1)
        else:raise RuntimeError('Native TLS did not load the renewed certificate')
        if fingerprints(root)!=before:raise RuntimeError('Native user/world preservation check failed')
        may_enable=True
        (archive/'receipt.json').write_text(json.dumps(dict(status='verified',certificate_sha256=expected,data_preserved=True),indent=2))
        print('event=native_certificate_rotated tls_verified=true data_preserved=true')
    finally:
        stage.unlink(missing_ok=True)
        run(['docker','update','--restart='+prior_policy,SIM])
        if not stopped and may_enable:
            console('login enable',r'Logins.*enabled|logins.*enabled')
        # On a failed restart retain the new/old PFX and backup for review; do
        # not blindly restart or revert the world's database.

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--operations',required=True,type=Path);parser.add_argument('--force',action='store_true');args=parser.parse_args()
    if os.geteuid()!=0:raise SystemExit('Root-owned service required')
    root=args.operations.resolve(strict=True)
    if root.stat().st_mode&0o077:raise SystemExit('Private operator directory required')
    with (root/'certificate-refresh.lock').open('a') as lock:
        fcntl.flock(lock,fcntl.LOCK_EX)
        try:refresh(root,args.force)
        except Exception:
            print('event=native_certificate_refresh_failed operator_review_required=true')
            raise SystemExit(1)
