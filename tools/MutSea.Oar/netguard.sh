#!/bin/sh
# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
# Run only in the named staging container's network namespace, with NET_ADMIN.
set -eu
[ "$#" = 1 ]
case "$1" in *[!0-9.]*|'') exit 2 ;; esac
iptables -N MUTSEA_OAR_EGRESS
iptables -A MUTSEA_OAR_EGRESS -o lo -j ACCEPT
iptables -A MUTSEA_OAR_EGRESS -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
iptables -A MUTSEA_OAR_EGRESS -p tcp --dport 5432 -d "$1" -j ACCEPT
iptables -A MUTSEA_OAR_EGRESS -j REJECT
iptables -I OUTPUT 1 -j MUTSEA_OAR_EGRESS
ip6tables -P OUTPUT DROP
