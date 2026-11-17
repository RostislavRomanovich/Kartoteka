#!/usr/bin/env python3
"""Read-only inventory of a Kartoteka SQLite database; never prints personal data."""
from __future__ import annotations
import argparse, hashlib, json, sqlite3
from pathlib import Path


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database", type=Path, help="Path to a COPY of kartoteka.db")
    parser.add_argument("--output", type=Path, default=Path("sqlite_inventory.json"))
    args = parser.parse_args()
    if not args.database.is_file():
        parser.error(f"Database file not found: {args.database}")
    digest = sha256_file(args.database)
    con = sqlite3.connect(f"file:{args.database.resolve().as_posix()}?mode=ro", uri=True)
    con.row_factory = sqlite3.Row
    tables = []
    try:
        names = [r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")]
        for name in names:
            escaped = '"' + name.replace('"', '""') + '"'
            cols = [dict(r) for r in con.execute(f"PRAGMA table_info({escaped})")]
            count = con.execute(f"SELECT COUNT(*) FROM {escaped}").fetchone()[0]
            fk = [dict(r) for r in con.execute(f"PRAGMA foreign_key_list({escaped})")]
            tables.append({"name": name, "row_count": count, "columns": cols, "foreign_keys": fk})
        report = {"source_file_name": args.database.name, "source_sha256": digest, "sqlite_version": sqlite3.sqlite_version, "tables": tables}
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Инвентаризация завершена: {args.output.resolve()}")
        print(f"SHA-256 базы: {digest}")
        for table in tables:
            print(f"{table['name']}: {table['row_count']} записей; полей: {len(table['columns'])}")
        return 0
    finally:
        con.close()

if __name__ == "__main__":
    raise SystemExit(main())
