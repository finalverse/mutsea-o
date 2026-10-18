#!/usr/bin/env python3
"""Operator-only, bounded World Library acquisition. Never restores an OAR."""
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import time
import urllib.request
from urllib.parse import urlsplit

spec = importlib.util.spec_from_file_location('quarantine', Path(__file__).parents[1] / 'MutSea.Oar/quarantine.py')
quarantine = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = quarantine
spec.loader.exec_module(quarantine)
ALLOWED = {'files.zadaroo.com', 'openvce.net'}


def safe_url(value):
    u = urlsplit(value)
    if u.scheme != 'https' or u.hostname not in ALLOWED or u.username or u.password or u.port not in (None, 443) or u.fragment:
        raise ValueError('Source must be an approved HTTPS publisher URL')
    return value


class PublisherRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        safe_url(newurl)
        if urlsplit(newurl).hostname != urlsplit(req.full_url).hostname:
            raise ValueError('Cross-publisher redirect refused')
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def catalog(path):
    if path.stat().st_size > 256 * 1024:
        raise ValueError('Catalog too large')
    data = json.loads(path.read_text())
    if data.get('schema_version') != 1 or not isinstance(data.get('entries'), list) or len(data['entries']) > 128:
        raise ValueError('Unsupported catalog')
    ids = set()
    for entry in data['entries']:
        key = entry['id']
        if not re.fullmatch(r'[a-z0-9][a-z0-9.-]{1,95}', key) or key in ids:
            raise ValueError('Invalid or duplicate catalog ID')
        ids.add(key)
        safe_url(entry['url'])
        pin = entry.get('expected_sha256')
        if pin is not None and not re.fullmatch('[a-f0-9]{64}', pin):
            raise ValueError('Invalid source digest')
    return data


def fetch(url, target, limit, timeout=45, duration=180):
    """Exclusive output; TLS verified; size and wall-time bounded, including chunked bodies."""
    safe_url(url)
    start = time.monotonic()
    opener = urllib.request.build_opener(PublisherRedirect())
    request = urllib.request.Request(url, headers={'User-Agent': 'MutSea-WorldLibrary/0.1 (operator archive review)'})
    try:
        with opener.open(request, timeout=timeout) as response:
            safe_url(response.geturl())
            length = response.headers.get('Content-Length')
            if length is not None and int(length) > limit:
                raise ValueError('Compressed download exceeds configured limit')
            count = 0
            with target.open('xb') as out:
                os.chmod(target, 0o600)
                while True:
                    if time.monotonic() - start > duration:
                        raise ValueError('Download deadline exceeded')
                    block = response.read(min(65536, limit - count + 1))
                    if not block:
                        break
                    count += len(block)
                    if count > limit:
                        raise ValueError('Compressed download exceeds configured limit')
                    out.write(block)
        return count
    except Exception:
        # Only remove the file created by this call, never a pre-existing file.
        if 'out' in locals():
            target.unlink(missing_ok=True)
        raise


def acquire(entry, root, budget, downloader=fetch):
    result = dict(id=entry['id'], title=entry['title'], source=entry['url'], license=entry['license'],
                  license_url=entry['license_url'], redistribution='not-admitted', runtime='not-restored')
    if entry['admission'] != 'compatible' or entry['platform'] != 'opensimulator':
        return dict(result, status='deferred', reason=entry['admission'])
    if budget <= 0:
        return dict(result, status='deferred', reason='Run download budget exhausted')
    folder = root / entry['id']
    folder.mkdir(mode=0o700)  # An earlier run is preserved; choose a new run directory.
    source = folder / 'source.oar'
    try:
        downloader(entry['url'], source, min(budget, quarantine.Limits().compressed))
        size = source.stat().st_size
        digest = hashlib.sha256(source.read_bytes()).hexdigest()
        result.update(source_bytes=size, source_sha256=digest)
        if entry.get('expected_sha256') is not None and digest != entry['expected_sha256']:
            raise ValueError('Publisher bytes differ from pinned source')
        report = quarantine.prepare(source, folder / 'passive')
        result.update(status='quarantined', staging_sha256=report['staging_sha256'],
                      object_groups=report['object_groups'], scene_parts=report['scene_parts'], removed=report['removed'])
    except Exception as error:
        result.update(status='rejected', reason=str(error)[:300])
    (folder / 'receipt.json').write_text(json.dumps(result, indent=2) + '\n')
    return result


def collisions(root):
    """Different bytes sharing an asset UUID must not enter a shared asset store."""
    seen, conflicts = {}, []
    for path in sorted(root.glob('*/passive/report.json')):
        for member in json.loads(path.read_text())['members']:
            name = member.get('staging_path', '')
            if not name.startswith('assets/'):
                continue
            key = name.split('/')[1].split('_')[0].lower()
            digest = member['staging_sha256']
            old = seen.get(key)
            if old and old[0] != digest:
                conflicts.append(dict(asset_id=key, first=old[1], second=path.parent.parent.name))
            else:
                seen[key] = (digest, path.parent.parent.name)
    return conflicts


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('catalog', type=Path)
    p.add_argument('output', type=Path, help='New private run directory; no native runtime mount')
    p.add_argument('--budget-mib', type=int, default=512)
    p.add_argument('--only', action='append', default=[])
    a = p.parse_args()
    if not 1 <= a.budget_mib <= 4096:
        p.error('Budget must be 1–4096 MiB')
    data = catalog(a.catalog)
    unknown = set(a.only) - {e['id'] for e in data['entries']}
    if unknown:
        p.error('Unknown source ID')
    a.output.mkdir(mode=0o700)
    remaining, results = a.budget_mib * 1024 * 1024, []
    for entry in data['entries']:
        if a.only and entry['id'] not in a.only:
            continue
        result = acquire(entry, a.output, remaining)
        remaining -= result.get('source_bytes', 0)
        results.append(result)
        print(json.dumps(result), flush=True)
    report = dict(schema_version=1, catalog_sha256=hashlib.sha256(a.catalog.read_bytes()).hexdigest(),
                  entries=results, asset_conflicts=collisions(a.output),
                  gates_remaining=['asset dependency closure', 'archive-specific rights review', 'isolated native restore',
                                   'ownership/permission policy', 'visual/performance/teleport acceptance'])
    (a.output / 'run.json').write_text(json.dumps(report, indent=2) + '\n')
    return 1 if any(r['status'] == 'rejected' for r in results) or report['asset_conflicts'] else 0


if __name__ == '__main__':
    sys.exit(main())
