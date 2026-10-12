#!/usr/bin/env python3
"""Reconcile a passive OAR with a read-only native DB snapshot; reject ambiguous matches."""
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import argparse
from collections import defaultdict
import copy
import json
import math
import os
from pathlib import Path
from quarantine import Rejected, identity


def near(a, b):
    return len(a) == len(b) and all(type(x) in (int, float) and type(y) in (int, float)
        and math.isfinite(x) and math.isfinite(y) and abs(x-y) <= .001 for x,y in zip(a,b))


def reconcile(source, native, owner):
    owner=identity(owner)
    if len(native)!=source['scene_parts'] or len({p['id'] for p in native})!=len(native):
        raise Rejected('Native scene count/identity mismatch')
    groups=defaultdict(list)
    for p in native:
        if p['owner']!=owner:raise Rejected('Unexpected instance owner')
        identity(p['id']);identity(p['group_id'])
        groups[p['group_id']].append(p)
    result=copy.deepcopy(source)
    mapping=[];used=set()
    for original,converted in zip(source['objects'],result['objects']):
        root=original['parts'][0]
        candidates=[]
        for gid,parts in groups.items():
            actual=next((p for p in parts if p['id']==gid),None)
            if actual and gid not in used and len(parts)==len(original['parts']) and actual['name']==root['name'] and near(actual['position'],root['position']) and near(actual['scale'],root['scale']):
                candidates.append((gid,parts))
        if len(candidates)!=1:raise Rejected('Missing or ambiguous native group match')
        gid,parts=candidates[0];used.add(gid)
        assigned=set()
        for old,new in zip(original['parts'],converted['parts']):
            choices=[p for p in parts if p['id'] not in assigned and p['name']==old['name'] and
                (p['id']==gid if old is root else p['id']!=gid) and
                near(p['scale'],old['scale']) and near(p['offset'],old['offset']) and near(p['rotation'],old['rotation'])]
            if len(choices)>1:
                choices=[p for p in choices if p['link_number']==old['link_number']]
            if len(choices)!=1:raise Rejected(f'Native link/transform mismatch: {old["id"]}, candidates={len(choices)}')
            p=choices[0];assigned.add(p['id'])
            if not near(p['position'],root['position']):raise Rejected('Native group-position mismatch')
            mapping.append(dict(source_id=old['id'],runtime_id=p['id'],source_owner=old['owner'],instance_owner=owner,
                source_link_number=old['link_number'],runtime_link_number=p['link_number']))
            new.update(source_id=old['id'],id=p['id'],position=p['position'],offset=p['offset'],owner=owner)
    if len(used)!=len(groups):raise Rejected('Unmatched native groups')
    result.update(runtime_mapping=mapping,instance_owner=owner,
        mapping_method='unique root name/position/scale/count, then name and full local transform; retained link number breaks ties; ambiguity rejects',
        compatibility_notes=['Native ArchiveReadRequest.LoadObjects calls ResetIDs on every group.',
            'This receipt reconciles a single isolated restore; it is not a reusable transaction remapper.',
            'Child GroupPosition is normalized to the native root group position.',
            'Native deserialization can renumber links; original and actual numbers are retained in the mapping.'])
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('source',type=Path);p.add_argument('native',type=Path);p.add_argument('owner');p.add_argument('output',type=Path)
    a=p.parse_args()
    result=reconcile(json.loads(a.source.read_text()),json.loads(a.native.read_text()),a.owner)
    with a.output.open('x') as out:
        os.chmod(a.output,0o600);json.dump(result,out,indent=2);out.write('\n')
    print('Verified native ownership/transforms and source→runtime mapping:',len(result['runtime_mapping']))
