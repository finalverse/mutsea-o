# Experimental staging sidecar only. Runtime container retains cap_drop ALL.
FROM postgres:16-alpine
RUN apk add --no-cache iptables
COPY netguard.sh /usr/local/bin/mutsea-oar-netguard
ENTRYPOINT ["/bin/sh", "/usr/local/bin/mutsea-oar-netguard"]
