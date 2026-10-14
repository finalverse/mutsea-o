#!/bin/sh
# Certbot deploy hook; affects only the MutSea certificate.
set -eu
case "${RENEWED_LINEAGE:-}" in
  */live/mutsea.com)
    /usr/sbin/nginx -t && /bin/systemctl reload nginx
    # Native TLS caches its PFX at startup. Drain/retry independently of nginx.
    /bin/systemctl start --no-block finalverse-native-certificate.service
    ;;
esac
