#!/usr/bin/env python3
"""Offline, fail-closed transfer into an EMPTY native MutSea PostgreSQL schema.

Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
Uses Docker's local psql and SQLite's read-only snapshots. Credentials are never
arguments. This deliberately bounded development migration is not a general
cross-provider converter. Preserve the stopped source fixture and its backup.
"""
import argparse
import base64
import csv
import hashlib
import json
import re
import sqlite3
import subprocess
from pathlib import Path

csv.field_size_limit(32 * 1024 * 1024)

FILES = ("MutSea.db", "userprofiles.db", "inventory.db", "avatars.db", "Asset.db", "auth.db", "griduser.db", "friends.db")
RENAMES = {("land", "desc"): "description", ("spawn_points", "regionid"): "regionuuid", ("assets", "uuid"): "id", ("assets", "type"): "assettype"}
DEFAULT_ONLY = {("prims", c.lower()) for c in ("VolumeDetect", "KeyframeMotion", "AttachedPosX", "AttachedPosY", "AttachedPosZ")} | {("primshapes", "lastattachpoint"), ("regionwindlight", "water_color_i")}

def quote(name):
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
        raise ValueError("Unexpected native identifier")
    return '"' + name + '"'

def cell(value, column):
    if value is None:
        return r"\N"
    typ = column["data_type"]
    if typ == "bytea":
        if isinstance(value, str) and column["column_name"].lower() in ("particlesystem", "textureanimation"):
            value = base64.b64decode(value, validate=True)
        if not isinstance(value, bytes):
            raise ValueError("Binary column has nonbinary source: " + column["column_name"])
        text = "\\x" + value.hex()
    elif typ == "boolean":
        if isinstance(value, str) and value.lower() in ("true", "false", "1", "0"):
            value = value.lower() in ("true", "1")
        if value not in (0, 1, False, True):
            raise ValueError("Invalid native boolean")
        text = "t" if value else "f"
    else:
        text = str(value)
    return '"' + text.replace('"', '""') + '"'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-directory", type=Path, required=True)
    parser.add_argument("--postgres-container", required=True)
    parser.add_argument("--database", default="mutsea")
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--verify", action="store_true", help="Compare all migrated cells without changing the destination")
    args = parser.parse_args()
    # A database argument is a local name, never a credential-bearing libpq URI.
    quote(args.database)
    if args.apply and args.verify:
        raise ValueError("Apply and verify are separate operations")
    if args.report.exists():
        raise ValueError("Preserve prior receipts; choose a new report")
    base = ["docker", "exec", "-i", args.postgres_container, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", args.database]
    def query(sql):
        result = subprocess.run(base + ["-tA", "-c", sql], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        return result.stdout.decode().strip()
    metadata = json.loads(query("select json_agg(x) from (select table_name,column_name,data_type,is_nullable,column_default from information_schema.columns where table_schema='public' order by table_name,ordinal_position) x"))
    tables = {}
    for column in metadata:
        tables.setdefault(column["table_name"].lower(), {})[column["column_name"].lower()] = column
    if "startstr" not in tables.get("prims", {}):
        raise ValueError("Native PostgreSQL region schema is incomplete (StartStr)")
    for target in tables.values():
        table = next(iter(target.values()))["table_name"]
        if not args.verify and table != "migrations" and int(query("select count(*) from " + quote(table))):
            raise ValueError("Destination contains application data; refusing overwrite")
    statements = ["BEGIN;"]
    report = {"tables": [], "source_sha256": {}, "transient_tokens_skipped": True, "applied": args.apply}
    comparisons = []
    for filename in FILES:
        file = args.source_directory / filename
        report["source_sha256"][filename] = hashlib.sha256(file.read_bytes()).hexdigest()
        db = sqlite3.connect("file:" + str(file.resolve()) + "?mode=ro", uri=True)
        for (name,) in db.execute("select name from sqlite_master where type='table' order by name"):
            if name in ("migrations", "tokens"):
                continue
            target = tables.get(name.lower())
            rows = db.execute("select * from " + quote(name)).fetchall()
            source = [r[1] for r in db.execute("pragma table_info(" + quote(name) + ")")]
            if target is None:
                raise ValueError("Unsupported source table: " + name)
            mapping = []
            ignored = []
            for index, key in enumerate(source):
                column = target.get(RENAMES.get((name.lower(), key.lower()), key.lower()))
                if column:
                    mapping.append((index, column))
                elif (name.lower(), key.lower()) in DEFAULT_ONLY and all(r[index] in (None, 0, "", b"") for r in rows):
                    ignored.append(key)
                else:
                    raise ValueError("Unsupported or nondefault source column: " + name + "." + key)
            # SQLite does not persist link numbers. Reconstruct its deterministic
            # UUID-ordered load, keeping the canonical root and original IDs.
            extra = []
            if name.lower() == "prims":
                uid, gid = source.index("UUID"), source.index("SceneGroupID")
                groups = {}
                for row in rows:
                    groups.setdefault(row[gid], []).append(row[uid])
                links = {}
                for root, ids in groups.items():
                    if root not in ids:
                        raise ValueError("Orphan scene group; review before migration")
                    links[root] = 0 if len(ids) == 1 else 1
                    links.update({id: i + 2 for i, id in enumerate(sorted(x for x in ids if x != root))})
                extra.append((target["linknumber"], lambda row: links[row[uid]]))
            table = next(iter(target.values()))["table_name"]
            columns = [c for _, c in mapping] + [c for c, _ in extra]
            expected = []
            def native(value, column):
                if value is None:
                    return None
                text = next(csv.reader([cell(value, column)]))[0]
                if column["data_type"] == "boolean":
                    return text == "t"
                if column["data_type"] in ("smallint", "integer", "bigint"):
                    return int(text)
                if column["data_type"] in ("real", "double precision", "numeric"):
                    return float(text)
                return text
            for row in rows:
                expected.append({c["column_name"]: native(row[i], c) for i, c in mapping}
                    | {c["column_name"]: native(fn(row), c) for c, fn in extra})
            comparisons.append((table, columns, expected))
            if rows:
                statements.append("COPY " + quote(table) + " (" + ",".join(quote(c["column_name"]) for c in columns) + ") FROM STDIN WITH (FORMAT csv, NULL '\\N');")
                for row in rows:
                    values = [cell(row[i], c) for i, c in mapping] + [cell(fn(row), c) for c, fn in extra]
                    statements.append(",".join(values))
                statements.append("\\.")
            report["tables"].append({"source": filename, "table": table, "rows": len(rows), "default_only_columns": ignored})
        db.close()
    statements.append("COMMIT;")
    if args.apply:
        result = subprocess.run(base, input=("\n".join(statements) + "\n").encode(), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if result.returncode:
            path = args.report.with_suffix(".diagnostic")
            path.write_bytes(result.stderr); path.chmod(0o600)
            raise ValueError("Transfer failed and rolled back; private diagnostic saved")
        for table in report["tables"]:
            if int(query("select count(*) from " + quote(table["table"]))) != table["rows"]:
                raise ValueError("Post-transfer count mismatch; keep candidate offline")
    if args.apply or args.verify:
        def digest(rows):
            return hashlib.sha256("\n".join(sorted(json.dumps(row, sort_keys=True, separators=(",", ":")) for row in rows)).encode()).hexdigest()
        for table, columns, expected in comparisons:
            projection = ",".join(quote(c["column_name"]) for c in columns)
            actual = json.loads(query("select coalesce(json_agg(x),'[]') from (select " + projection + " from " + quote(table) + ") x"))
            # Match JSON numbers independently of native integer/float rendering.
            for row in actual:
                for column in columns:
                    key = column["column_name"]
                    if row[key] is not None and column["data_type"] in ("real", "double precision", "numeric"):
                        row[key] = float(row[key])
            if digest(actual) != digest(expected):
                raise ValueError("Post-transfer cell mismatch: " + table + "; keep candidate offline")
            next(t for t in report["tables"] if t["table"] == table)["verified_cells_sha256"] = digest(actual)
    report["all_cells_verified"] = args.apply or args.verify
    args.report.write_text(json.dumps(report, indent=2) + "\n"); args.report.chmod(0o600)
    print("Validated", len(report["tables"]), "tables;", sum(t["rows"] for t in report["tables"]), "rows; applied:", args.apply)

if __name__ == "__main__":
    main()
