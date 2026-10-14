#!/usr/bin/env python3
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
"""Stage an operator-approved pilot in the existing native newcomer pipeline.

This creates no web account, sends no message and executes no world operation.
Admission occurs at the next reviewed native start with a ready selected world.
"""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile
import uuid


def require(ok, message):
    if not ok:
        raise ValueError(message)


def read_private(path):
    require(path.is_file() and not path.is_symlink() and 0 < path.stat().st_size <= 65536,
            'Expected a bounded regular private file.')
    require(os.name == 'nt' or path.stat().st_mode & 0o077 == 0, 'Input must be owner-only.')
    def unique(pairs):
        value = {}
        for key, item in pairs:
            require(key not in value, 'Duplicate JSON key.')
            value[key] = item
        return value
    try:
        return json.loads(path.read_bytes(), object_pairs_hook=unique)
    except json.JSONDecodeError:
        raise ValueError('Invalid private JSON.') from None


def stage(config, request_path):
    require(config.is_dir() and not config.is_symlink(), 'Expected private native configuration directory.')
    directory = config / 'accounts'
    require(directory.is_dir() and not directory.is_symlink(), 'Missing private accounts directory.')
    lock = os.open(directory / '.admission.lock', os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return stage_locked(config, request_path)
    finally:
        os.close(lock)


def stage_locked(config, request_path):
    require(config.is_dir() and not config.is_symlink(), 'Expected private native configuration directory.')
    request = read_private(request_path)
    require(isinstance(request, dict) and set(request) == {'admission_id', 'first_name', 'last_name', 'password', 'avatar', 'experience'},
            'Expected admission UUID, name, password, avatar and experience only.')
    require(isinstance(request['admission_id'], str) and str(uuid.UUID(request['admission_id'])) == request['admission_id'] and uuid.UUID(request['admission_id']).int != 0,
            'Expected canonical nonzero admission UUID.')
    for field in ['first_name', 'last_name']:
        require(isinstance(request[field], str) and re.fullmatch(r'[A-Za-z][A-Za-z0-9]{0,30}', request[field]), 'Invalid pilot name.')
    password = request['password']
    require(isinstance(password, str) and 12 <= len(password) <= 128 and all(c.isprintable() for c in password), 'Invalid pilot password.')
    require(request['experience'] == 'harbor', 'Only the operator-provisioned Harbor experience is available.')
    choices = read_private(config / 'avatar-choices.json')
    require(isinstance(choices, list) and 1 <= len(choices) <= 8, 'Invalid server-controlled choices.')
    matches = [c for c in choices if c.get('id') == request['avatar']]
    require(len(matches) == 1, 'Choose a supported canonical character.')
    choice = matches[0]
    require(re.fullmatch(r'[a-z][a-z0-9-]{0,40}', choice['id']) and choice['directory'] == '/config/genesis/' + choice['id'], 'Invalid configured character path.')
    package = config / 'genesis' / choice['id']
    require(package.is_dir() and not package.is_symlink(), 'Character directory is missing or linked.')
    compiled_path = package / 'compiled.json'
    require(compiled_path.is_file() and not compiled_path.is_symlink() and compiled_path.stat().st_size <= 65536, 'Missing or invalid compiled character.')
    compiled = compiled_path.read_bytes()
    require(hashlib.sha256(compiled).hexdigest() == choice['compiled_sha256'], 'Configured character pin changed.')
    avatar = json.loads(compiled)
    require(avatar['representation'] == 'attachment' and avatar['package_id'] == choice['package_id'] and avatar['version'] == choice['version'], 'Character is not the configured wearable.')
    require(avatar['package_id'] != 'finalverse.genesis.elara-male', 'Provisional character is retired.')
    current_path = config / 'accounts' / 'newcomers.json'
    current = read_private(current_path)
    require(isinstance(current, dict) and set(current) == {'accounts'} and isinstance(current['accounts'], list) and len(current['accounts']) <= 16, 'Invalid native newcomer file.')
    admission = dict(first_name=request['first_name'], last_name=request['last_name'], password=password,
                     genesis=choice['directory'], genesis_sha256=choice['compiled_sha256'], admission_id=request['admission_id'])
    for account in current['accounts']:
        same_name = (account['first_name'] + ' ' + account['last_name']).lower() == (admission['first_name'] + ' ' + admission['last_name']).lower()
        same_id = account.get('admission_id') == admission['admission_id']
        if same_name or same_id:
            require(account == admission, 'Name or admission already staged; changes are refused.')
            return {'status': 'already_staged', 'admission_id': request['admission_id']}
    require(len(current['accounts']) < 16, 'Pilot batch is full; archive completed admissions during reviewed maintenance.')
    current['accounts'].append(admission)
    encoded = (json.dumps(current, indent=2) + '\n').encode()
    require(len(encoded) <= 65536, 'Native newcomer file would exceed its bound.')
    fd, temp = tempfile.mkstemp(prefix='.newcomers-', dir=current_path.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, 'wb') as out:
            out.write(encoded)
            out.flush()
            os.fsync(out.fileno())
        os.replace(temp, current_path)
        parent = os.open(current_path.parent, os.O_RDONLY)
        try:
            os.fsync(parent)
        finally:
            os.close(parent)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    return {'status': 'staged_for_native_provisioning', 'admission_id': request['admission_id']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, required=True)
    parser.add_argument('--request', type=Path, required=True, help='Owner-only JSON; never put credentials in arguments')
    args = parser.parse_args()
    try:
        print(json.dumps(stage(args.config, args.request)))
    except (ValueError, KeyError, TypeError, OSError):
        parser.exit(1, 'Pilot admission refused; check private configuration, selection and request. No secret details are logged.\n')
