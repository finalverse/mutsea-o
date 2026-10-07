#!/usr/bin/env python3
"""Keep only the MutSea apex A record aligned with the configured DDNS gateway."""
import ipaddress
import json
import os
import socket
import subprocess
import sys

def aws(*args):
    result = subprocess.run(['aws', 'route53', '--region', 'us-east-1', '--no-cli-pager',
        '--cli-connect-timeout', '5', '--cli-read-timeout', '15', *args],
        capture_output=True, text=True, timeout=45)
    if result.returncode:
        raise RuntimeError('Route 53 request failed: ' + result.stderr.strip())
    return json.loads(result.stdout)

def main():
    zone = os.environ['MUTSEA_ZONE_ID']
    gateway = os.environ['MUTSEA_GATEWAY_DDNS']
    domain = os.environ.get('MUTSEA_DOMAIN', 'mutsea.com').rstrip('.') + '.'
    if domain != 'mutsea.com.':
        raise ValueError('This deployment updater is restricted to mutsea.com')
    address = str(ipaddress.IPv4Address(socket.gethostbyname(gateway)))
    if not ipaddress.ip_address(address).is_global:
        raise ValueError('Gateway must resolve to a public IPv4 address')
    record = {'Name': domain, 'Type': 'A', 'TTL': 60,
              'ResourceRecords': [{'Value': address}]}
    records = aws('list-resource-record-sets', '--hosted-zone-id', zone)['ResourceRecordSets']
    current = next((r for r in records if r['Name'] == domain and r['Type'] == 'A'), None)
    if current is None or 'AliasTarget' in current or len(current.get('ResourceRecords', [])) != 1:
        raise ValueError('Expected the existing single apex A record; refusing to replace another DNS layout')
    if current == record:
        print('MutSea apex already matches gateway')
        return
    result = aws('change-resource-record-sets', '--hosted-zone-id', zone,
                 '--change-batch', json.dumps({'Comment': 'MutSea DDNS gateway update',
                     'Changes': [{'Action': 'UPSERT', 'ResourceRecordSet': record}]}))
    print('MutSea apex updated:', address, result['ChangeInfo']['Status'])

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
