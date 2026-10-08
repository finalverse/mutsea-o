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
p.add_argument('--database', choices=('postgresql', 'sqlite'), default='postgresql',
    help='PostgreSQL is the default; SQLite is an explicit development/rollback option')
p.add_argument('--database-config', type=Path,
    help='Private JSON: host, database, user, password, optional port; PostgreSQL must already exist')
a = p.parse_args()
labels = a.host.split('.')
valid_host = len(a.host) <= 253 and all(re.fullmatch(r'[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?', label) for label in labels)
if re.fullmatch(r'[0-9.]+', a.host):
    try: ipaddress.IPv4Address(a.host)
    except ValueError: valid_host = False
if not valid_host or not 1024 <= a.port <= 65535:
    p.error('Invalid host or port')
db_connection = None
if a.database == 'postgresql':
    if not a.database_config:
        p.error('PostgreSQL requires --database-config; no silent SQLite fallback')
    try:
        if os.name != 'nt' and a.database_config.stat().st_mode & 0o077:
            raise ValueError()
        db = json.loads(a.database_config.read_text())
        # Restrict the fresh-instance format to generated URL-safe passwords.
        # This prevents INI/Npgsql delimiter injection without printing secrets.
        db_host = db['host']
        if not isinstance(db_host, str) or not re.fullmatch(r'[A-Za-z0-9.-]{1,253}', db_host):
            raise ValueError()
        for field in ('database', 'user'):
            if not isinstance(db[field], str) or not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]{0,62}', db[field]):
                raise ValueError()
        if not isinstance(db['password'], str) or not re.fullmatch(r'[A-Za-z0-9_-]{24,256}', db['password']):
            raise ValueError()
        db_port = db.get('port', 5432)
        if type(db_port) is not int or not 1 <= db_port <= 65535:
            raise ValueError()
        db_connection = f"Host={db_host};Port={db_port};Database={db['database']};Username={db['user']};Password={db['password']}"
    except (KeyError, ValueError, TypeError, OSError):
        p.error('Invalid private database configuration; see PostgreSQL deployment documentation')
if a.output.exists() and any(a.output.iterdir()):
    p.error('Output must be empty; existing configuration and world data are preserved')
a.output.mkdir(parents=True, exist_ok=True, mode=0o700)
if os.name != 'nt':
    a.output.chmod(0o700)
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
if db_connection:
    common = ''
    for section in ('DatabaseService', 'SimulationDataStore', 'EstateDataStore',
        'AssetService', 'InventoryService', 'AvatarService', 'AuthenticationService',
        'UserAccountService', 'GridUserService', 'FriendsService', 'PresenceService',
        'UserProfilesService', 'AgentPreferencesService'):
        # Nini treats unquoted semicolons as comments. Quote the entire value.
        common += f'[{section}]\nStorageProvider = MutSea.Data.PGSQL.dll\nConnectionString = "{db_connection}"\n'
        if section == 'AssetService':
            common += 'DefaultAssetLoader = MutSea.Framework.AssetLoader.Filesystem.dll\nAssetLoaderArgs = assets/AssetSets.xml\n'
        if section == 'UserAccountService':
            common += 'CreateDefaultAvatarEntries = true\n'
else:
    common = '''[DatabaseService]
StorageProvider = MutSea.Data.SQLite.dll
ConnectionString = "URI=file:/data/MutSea.db;UseUTF16Encoding=True"
'''
    for section, name in [('AssetService','Asset'), ('InventoryService','inventory'),
        ('AvatarService','avatars'), ('AuthenticationService','auth'),
        ('UserAccountService','userprofiles'), ('GridUserService','griduser'), ('FriendsService','friends')]:
        common += f'[{section}]\nConnectionString = "URI=file:/data/{name}.db"\n'
        if section == 'AssetService':
            common += 'DefaultAssetLoader = MutSea.Framework.AssetLoader.Filesystem.dll\nAssetLoaderArgs = assets/AssetSets.xml\n'
        if section == 'UserAccountService':
            common += 'CreateDefaultAvatarEntries = true\n'
common += '''[Modules]
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
