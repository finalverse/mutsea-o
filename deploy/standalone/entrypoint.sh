#!/bin/sh
set -eu
# Simulator code/resources are ephemeral; databases and world history live in /data.
# A fresh config is prepared separately; credentials are never image layers.
[ -s /config/MutSea.ini ]
[ -s /config/StandaloneCommon.ini ]
[ -d /data ]
[ -w /data ]
ln -sf /config/StandaloneCommon.ini config-include/StandaloneCommon.ini
export LD_LIBRARY_PATH="/opt/mutsea/lib64${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec dotnet MutSea.dll --inifile=/config/MutSea.ini --logfile=/data/MutSea.log
