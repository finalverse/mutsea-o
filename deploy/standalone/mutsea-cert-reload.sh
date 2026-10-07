#!/bin/sh
# Certbot deploy hook; affects only the MutSea certificate.
set -eu
case "${RENEWED_LINEAGE:-}" in
  */live/mutsea.com) /usr/sbin/nginx -t && /bin/systemctl reload nginx ;;
esac
