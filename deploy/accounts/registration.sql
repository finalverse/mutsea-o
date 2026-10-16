-- Additive registration queue. No native table is changed, and the public
-- portal retains zero native identity/inventory/appearance write privileges.
BEGIN;
SET ROLE mutsea_portal;
CREATE TABLE mutsea_portal.registrations (
    id uuid PRIMARY KEY,
    ticket_hash text UNIQUE NOT NULL CHECK(length(ticket_hash)=64),
    normal_name text UNIQUE NOT NULL,
    name text NOT NULL CHECK(length(name)<=63),
    ip_hash text NOT NULL CHECK(length(ip_hash)=64),
    payload bytea CHECK(octet_length(payload)<=8192),
    state text NOT NULL CHECK(state IN ('queued','processing','ready','failed')),
    created timestamptz NOT NULL,
    updated timestamptz NOT NULL,
    CHECK((state IN ('ready','failed'))=(payload IS NULL))
);
CREATE INDEX ON mutsea_portal.registrations(state,created);
CREATE INDEX ON mutsea_portal.registrations(ip_hash,created);
RESET ROLE;
CREATE TABLE mutsea_portal.registration_worker (
    singleton bool PRIMARY KEY CHECK(singleton),
    region_id uuid NOT NULL,
    catalog text NOT NULL CHECK(length(catalog)=64),
    seen timestamptz NOT NULL
);
GRANT SELECT ON mutsea_portal.registration_worker TO mutsea_portal;
COMMIT;
-- The existing native database role needs explicit access to this queue. Apply
-- its SELECT/UPDATE grant separately with the real operator-selected role.
