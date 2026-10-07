#!/usr/bin/env python3
"""Build the development protocol probe with a pinned, Unix-compatible test SDK.
The SDK remains outside the simulator tree and never replaces server assemblies.
"""
import argparse
from pathlib import Path
import subprocess

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--workspace',type=Path,required=True,help='New empty scratch directory outside the repository')
a=p.parse_args()
if a.workspace.exists() and any(a.workspace.iterdir()):p.error('Workspace must be empty')
a.workspace.mkdir(parents=True,exist_ok=True)
a.workspace=a.workspace.resolve()
sdk=a.workspace/'sdk'
revision='f3ee229dc2a17e0dfd7256f8f634afe8e7610487'
def run(args,cwd=None):subprocess.run(args,cwd=cwd,check=True)
run(['git','clone','--no-checkout','https://github.com/opensim/libopenmetaverse.git',str(sdk)])
run(['git','checkout','--detach',revision],sdk)
# Preserve the upstream CRLF format and notices; change one reviewed catch only.
source=sdk/'OpenMetaverse/UDPBase.cs';content=source.read_bytes()
old=b'catch (SocketException)'
if content.count(old)<1:raise ValueError('Unreviewed UDPBase implementation')
source.write_bytes(content.replace(old,b'catch (Exception error) when (error is SocketException or PlatformNotSupportedException)',1))
# The idle texture pipeline must terminate cooperatively on .NET 8 (Thread.Abort is unsupported).
source=sdk/'OpenMetaverse/TexturePipeline.cs';content=source.read_bytes()
if content.count(b'bool _Running;') != 1 or content.count(b'downloadMaster.Abort();') != 1:
    raise ValueError('Unreviewed texture pipeline implementation')
content=content.replace(b'bool _Running;',b'volatile bool _Running;')
content=content.replace(b'downloadMaster.Abort();',b'_Running = false;\r\n                downloadMaster.Join(2000);')
source.write_bytes(content)
run(['bash','runprebuild.sh'],sdk)
run(['dotnet','build','-c','Release','OpenMetaverse.sln','-m:2'],sdk)
run(['dotnet','build',str(Path(__file__).parent/'MutSea.ProtocolSmoke.csproj'),'-c','Release','-p:MutSeaBin='+str(sdk/'bin')])
print('Protocol probe built; SDK revision:',revision)
