#!/usr/bin/env python3
"""Prepare a private, fresh standalone instance. Never overwrite an existing world."""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import re
import secrets
import uuid

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--host', required=True, help='Advertised hostname or IPv4 address')
p.add_argument('--port', type=int, default=19000, help='UDP region port; also HTTP port when TLS is off')
p.add_argument('--public-tls', action='store_true', help='Native TLS on container port 443 with /config/mutsea.p12')
a = p.parse_args()
labels = a.host.split('.')
valid_host = len(a.host) <= 253 and all(re.fullmatch(r'[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?', label) for label in labels)
if re.fullmatch(r'[0-9.]+', a.host):
    try: ipaddress.IPv4Address(a.host)
    except ValueError: valid_host = False
if not valid_host or not 1024 <= a.port <= 65535:
    p.error('Invalid host or port')
if a.output.exists() and any(a.output.iterdir()):
    p.error('Output must be empty; existing configuration and world data are preserved')
a.output.mkdir(parents=True, exist_ok=True, mode=0o700)
(a.output/'Regions').mkdir(mode=0o700)
owner, region = str(uuid.uuid4()), str(uuid.uuid4())
password, cert_password = secrets.token_urlsafe(24), secrets.token_urlsafe(24)
public_port = 443 if a.public_tls else a.port
base = ('https://' if a.public_tls else 'http://') + a.host
url = base + ('' if a.public_tls else ':' + str(a.port)) + '/'
def write(name, value):
    path = a.output/name
    fd = os.open(path, os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w') as out:
        out.write(value)
write('account.json', json.dumps(dict(first_name='MutSea', last_name='Developer', password=password,
    user_id=owner, region_id=region, region_name='MutSea Harbor', grid_url=url,
    registered=False), indent=2)+'\n')
write('certificate-password', cert_password+'\n')
ssl = (f'''http_listener_ssl = true
http_listener_sslport = 443
http_listener_cn = {a.host}
http_listener_cert_path = /config/mutsea.p12
http_listener_cert_pass = {cert_password}
''' if a.public_tls else '')
write('MutSea.ini', f'''[Const]
BaseURL = {base}
PublicPort = {public_port}
[Startup]
console = basic
physics = ubODE
meshing = ubODEMeshmerizer
ConsoleHistoryFileEnabled = false
MaxPoolThreads = 15
regionload_regionsdir = /config/Regions
startup_console_commands_file = /config/startup.txt
shutdown_console_commands_file = /config/shutdown.txt
[Network]
http_listener_port = {9000 if a.public_tls else a.port}
{ssl}[Estates]
DefaultEstateName = MutSea Harbor
DefaultEstateOwnerName = MutSea Developer
DefaultEstateOwnerUUID = {owner}
DefaultEstateOwnerEMail = ""
DefaultEstateOwnerPassword = {password}
[Architecture]
Include-Architecture = config-include/Standalone.ini
[GridInfoService]
DisableStatsEndpoint = true
login = {url}
gridname = MutSea
gridnick = mutsea
[RemoteAdmin]
enabled = false
[MapImageService]
CacheDirectory = /data/maptiles
[YEngine]
ScriptEnginesPath = /data/ScriptEngines
''')
common = '''[DatabaseService]
StorageProvider = MutSea.Data.SQLite.dll
ConnectionString = "URI=file:/data/MutSea.db;UseUTF16Encoding=True"
[AssetService]
DefaultAssetLoader = MutSea.Framework.AssetLoader.Filesystem.dll
AssetLoaderArgs = assets/AssetSets.xml
ConnectionString = "URI=file:/data/Asset.db"
[Modules]
AssetCaching = FlotsamAssetCache
[FlotsamCache]
CacheDirectory = /data/assetcache
[GridService]
StorageProvider = MutSea.Data.Null.dll:NullRegionData
Region_MutSea_Harbor = DefaultRegion, FallbackRegion
[LoginService]
WelcomeMessage = Welcome to MutSea Harbor
MapTileURL = ${Const|BaseURL}:${Const|PublicPort}/
'''
for section, name in [('InventoryService','inventory'),('AvatarService','avatars'),
    ('AuthenticationService','auth'),('UserAccountService','userprofiles'),
    ('GridUserService','griduser'),('FriendsService','friends')]:
    common += f'[{section}]\nConnectionString = "URI=file:/data/{name}.db"\n'
    if section == 'UserAccountService':
        # Reuse inherited wearable/COF/appearance provisioning for fresh users.
        # This is an initial bundle, not an atomic public registration service.
        common += 'CreateDefaultAvatarEntries = true\n'
write('StandaloneCommon.ini', common)
write('Regions/Harbor.ini', f'''[MutSea Harbor]
RegionUUID = {region}
Location = 1000,1000
InternalAddress = 0.0.0.0
InternalPort = {a.port}
ExternalHostName = {a.host}
DefaultLanding = <128,128,27>
''')
write('startup.txt', '')
write('shutdown.txt', '')
print('Private MutSea configuration prepared:', a.output.resolve())
print('Account credentials are in account.json; registration occurs on first successful initialization.')
