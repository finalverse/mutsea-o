# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
"""Bounded loopback newcomer/concurrent-session smoke, not capacity certification.

Run source-matched WorldTemplateSmoke for 2-6 distinct private accounts on the
named hero fixture. Every child verifies home, scene bindings, COF ownership,
semantics, a 10 s connected hold and normal logout. Raw SDK logs stay private.
"""
import argparse
import concurrent.futures
import datetime
import json
import math
import os
from pathlib import Path
import re
import stat
import subprocess


def overlap(intervals):
    events=[]
    for start,end in intervals:
        if not math.isfinite(start) or not math.isfinite(end) or end<=start:raise ValueError('Invalid connected hold interval')
        events.extend(((start,1),(end,-1)))
    active=peak=0
    for _,delta in sorted(events):active+=delta;peak=max(peak,active)
    return peak


def utc_ticks(value):
    # .NET emits 1-7 fractional digits; Python 3.9 accepts only some lengths.
    # Preserve exact 100 ns interval ordering instead of rounding timestamps.
    match=re.fullmatch(r'(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?\+00:00',value)
    if not match:raise ValueError('Expected native UTC hold timestamp')
    seconds=int(datetime.datetime.fromisoformat(match[1]).replace(tzinfo=datetime.timezone.utc).timestamp())
    return seconds*10000000+int((match[2] or '').ljust(7,'0'))


def summarize(receipts):
    intervals=[tuple(utc_ticks(d[k]) for k in ('hold_start','hold_end')) for d in receipts]
    peak=overlap(intervals)
    if peak!=len(receipts):raise ValueError('All native session hold intervals did not overlap; do not claim this concurrency')
    latencies=sorted(d['login_and_stream_ms'] for d in receipts)
    if any(not math.isfinite(v) or v<=0 for v in latencies):raise ValueError('Invalid native latency')
    return dict(schema_version=1,verified=True,accounts=len(receipts),peak_connected_holds=peak,
                home_scene_cof_semantics_logout=True,login_and_stream_ms=dict(minimum=latencies[0],median=(latencies[(len(latencies)-1)//2]+latencies[len(latencies)//2])/2,
                p95=latencies[math.ceil(.95*len(latencies))-1],maximum=latencies[-1]),
                scope='One local protocol concurrency smoke; no rendered-avatar/device/public registration/capacity or sustained-load acceptance',
                receipts=[dict(label=d['label'],matched_parts=d['matched_parts'],outfit_links=d['current_outfit_links'],hold_start=d['hold_start'],hold_end=d['hold_end']) for d in receipts])


def read(path,limit,private=False):
    s=path.lstat()
    if not stat.S_ISREG(s.st_mode) or not 0<s.st_size<=limit or private and s.st_mode&0o077:raise ValueError('Bounded regular private input required')
    return json.loads(path.read_bytes())


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    for arg in ('accounts','proof','probe','output'):ap.add_argument('--'+arg,type=Path,required=True)
    a=ap.parse_args();files=sorted(a.accounts.glob('*.json'))
    if not 2<=len(files)<=6:raise ValueError('Use 2-6 accounts; do not expand into an unbounded load generator')
    proof=read(a.proof,16*1024*1024)
    if proof['world']['region_id']!='38bfc868-034e-4a29-acd6-5ff203e96e57' or proof['world']['version']!='0.3.0' or proof['world']['status']!='committed':raise ValueError('Committed local court required')
    users=set()
    for file in files:
        account=read(file,16384,True)
        if account['grid_url']!='http://127.0.0.1:18099/' or account['region_id']!=proof['world']['region_id'] or account['fixture']!='hero-arrival-v1' or account['registered'] is not True:raise ValueError('Named local account required')
        if account['user_id'] in users:raise ValueError('Never use simultaneous logins for one account')
        users.add(account['user_id'])
    if not a.probe.is_file() or a.probe.is_symlink() or a.probe.name!='MutSea.WorldTemplateSmoke.dll':raise ValueError('Use the source-matched native probe DLL')
    a.output.mkdir(mode=0o700)
    def child(file):
        receipt=a.output/(file.stem+'.json');log=a.output/(file.stem+'.log')
        with log.open('x') as out:
            log.chmod(0o600)
            result=subprocess.run(['dotnet',str(a.probe),str(file),str(a.proof),str(receipt),'--hero-arrival','--hold-seconds=10'],
                                  env={**os.environ,'DOTNET_ROLL_FORWARD':'Major'},stdout=out,stderr=subprocess.STDOUT,timeout=90)
        if result.returncode!=0:raise RuntimeError('Native account check failed; inspect private '+file.stem+' log')
        data=read(receipt,65536,True)
        if data['logout'] is not True or data['home_login'] is not True or data['matched_parts']!=data['expected_parts'] or data['appearance_sent'] is not False:raise ValueError('Native session postconditions failed')
        return dict(label=file.stem,**data)
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(files)) as executor:receipts=list(executor.map(child,files))
    summary=summarize(receipts)
    p=a.output/'summary.json';p.write_text(json.dumps(summary,indent=2)+'\n');p.chmod(0o600);print(json.dumps(summary))


if __name__=='__main__':main()
