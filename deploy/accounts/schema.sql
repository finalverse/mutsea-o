-- Run as the operator in the existing native database, after creating the
-- dedicated mutsea_portal role. No native table/schema is changed.
CREATE SCHEMA mutsea_portal AUTHORIZATION mutsea_portal;
REVOKE ALL ON SCHEMA mutsea_portal FROM PUBLIC;
SET ROLE mutsea_portal;
CREATE TABLE mutsea_portal.sessions (hash text PRIMARY KEY CHECK(length(hash)=64),user_id uuid NOT NULL,stamp text NOT NULL,created timestamptz NOT NULL,seen timestamptz NOT NULL);
CREATE INDEX ON mutsea_portal.sessions(user_id);
CREATE TABLE mutsea_portal.profiles (user_id uuid PRIMARY KEY,about text NOT NULL DEFAULT '' CHECK(length(about)<=500),contact_email text NOT NULL DEFAULT '' CHECK(length(contact_email)<=254),language text NOT NULL DEFAULT 'en' CHECK(language IN ('en','zh')),version bigint NOT NULL CHECK(version>0));
CREATE TABLE mutsea_portal.recovery (user_id uuid NOT NULL,stamp text NOT NULL,hash text NOT NULL CHECK(length(hash)=64),created timestamptz NOT NULL,PRIMARY KEY(user_id,hash));
CREATE TABLE mutsea_portal.limits (key text NOT NULL,bucket bigint NOT NULL,n integer NOT NULL,PRIMARY KEY(key,bucket));
CREATE TABLE mutsea_portal.audit (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,user_id uuid,kind text NOT NULL,at timestamptz NOT NULL);
CREATE INDEX ON mutsea_portal.audit(at);
RESET ROLE;
GRANT USAGE ON SCHEMA public TO mutsea_portal;
GRANT SELECT ON public.useraccounts,public.griduser,public.auth,public.tokens,public.migrations TO mutsea_portal;
-- Existing password service stores the complete existing auth record.
-- No role, user-account, inventory, asset, prim or terrain writes are granted.
GRANT UPDATE ON public.auth TO mutsea_portal;
GRANT INSERT,UPDATE,DELETE ON public.tokens TO mutsea_portal;
