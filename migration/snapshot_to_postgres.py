#!/usr/bin/env python3
"""Idempotent, non-destructive SQLite snapshot into PostgreSQL staging tables.

Each logical source database must keep the same --source-id across backups/paths.
Rows are uniquely identified by (source_id, table_name, primary-key values), so a
repeat run updates existing staging rows instead of duplicating them. Rows absent
from a newer source snapshot are marked not present, not deleted. Defaults to dry-run.
Requires: pip install "psycopg[binary]"
Connection: KARTOTEKA_DB_CONNECTION (PostgreSQL URI or libpq conninfo).
"""
from __future__ import annotations
import argparse, base64, hashlib, json, os, sqlite3, sys
from pathlib import Path
from typing import Any


def json_value(value: Any) -> Any:
    if isinstance(value, bytes):
        return {"__type__": "sqlite_blob_base64", "base64": base64.b64encode(value).decode("ascii"),
                "sha256": hashlib.sha256(value).hexdigest(), "length": len(value)}
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    return str(value)


def stable_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def quote_ident(name: str) -> str:
    return '"' + name.replace('"', '""') + '"'


def inspect_source(path: Path):
    con = sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True)
    con.row_factory = sqlite3.Row
    try:
        tables = []
        names = [r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")]
        for table in names:
            info = list(con.execute(f"PRAGMA table_info({quote_ident(table)})"))
            pk_cols = [row['name'] for row in sorted(info, key=lambda r: r['pk']) if row['pk']]
            count = con.execute(f"SELECT COUNT(*) FROM {quote_ident(table)}").fetchone()[0]
            tables.append((table, pk_cols, count))
        return tables
    finally:
        con.close()


def make_row_key(payload: dict[str, Any], pk_cols: list[str], payload_hash: str, duplicate_ordinal: int = 0) -> str:
    """Use actual PK values; deterministic payload fallback for a table without a PK."""
    if pk_cols:
        return stable_json({k: payload.get(k) for k in pk_cols})
    # For no-PK tables, payload hash plus occurrence number preserves exact duplicate rows.
    return f"sha256:{payload_hash}:n:{duplicate_ordinal}"


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("database", type=Path, help="Path to a BACKUP COPY of kartoteka.db")
    p.add_argument("--source-id", required=True, help="Stable logical source name, e.g. main-kartoteka; reuse it for every backup of this same database")
    p.add_argument("--apply", action="store_true", help="Write to PostgreSQL; default is dry-run")
    p.add_argument("--batch-size", type=int, default=250)
    args = p.parse_args()
    if not args.database.is_file():
        p.error(f"Database file not found: {args.database}")
    if not args.source_id.strip() or len(args.source_id) > 200:
        p.error("--source-id must contain 1..200 non-whitespace characters")
    if not 1 <= args.batch_size <= 5000:
        p.error("--batch-size must be from 1 to 5000")
    conninfo = os.environ.get("KARTOTEKA_DB_CONNECTION")
    if args.apply and not conninfo:
        p.error("Set KARTOTEKA_DB_CONNECTION before using --apply")

    source_hash = sha256_file(args.database)
    tables = inspect_source(args.database)
    total = sum(count for _, _, count in tables)
    no_pk = [name for name, pk, _ in tables if not pk]
    print(f"Источник: {args.database.name}; логический ID: {args.source_id}; SHA-256: {source_hash}")
    print(f"Таблиц: {len(tables)}; строк всего: {total}")
    for name, pk, count in tables:
        print(f"  {name}: {count}; ключ: {', '.join(pk) if pk else 'детерминированный хэш строки'}")
    if no_pk:
        print("Внимание: таблицы без первичного ключа: " + ", ".join(no_pk))
    if not args.apply:
        print("DRY-RUN: PostgreSQL не изменён. Повторный импорт с тем же --source-id обновляет существующие строки, а не дублирует их.")
        return 0

    try:
        import psycopg
        from psycopg.types.json import Jsonb
    except ImportError:
        print('Не найден psycopg. Установите: py -3 -m pip install "psycopg[binary]"', file=sys.stderr)
        return 2

    # The whole import is one PostgreSQL transaction. Any error rolls it back.
    with psycopg.connect(conninfo) as pg:
        with pg.cursor() as cur:
            cur.execute("CREATE SCHEMA IF NOT EXISTS legacy_import")
            cur.execute("""CREATE TABLE IF NOT EXISTS legacy_import.dedup_sources (
                source_id text PRIMARY KEY,
                latest_sha256 char(64),
                latest_imported_at timestamptz,
                latest_row_count bigint NOT NULL DEFAULT 0
            )""")
            cur.execute("""CREATE TABLE IF NOT EXISTS legacy_import.dedup_runs (
                source_id text NOT NULL REFERENCES legacy_import.dedup_sources(source_id) ON DELETE CASCADE,
                source_sha256 char(64) NOT NULL,
                source_filename text NOT NULL,
                started_at timestamptz NOT NULL DEFAULT now(),
                completed_at timestamptz,
                table_count integer NOT NULL,
                row_count bigint NOT NULL,
                completed boolean NOT NULL DEFAULT false,
                PRIMARY KEY (source_id, source_sha256)
            )""")
            cur.execute("""CREATE TABLE IF NOT EXISTS legacy_import.dedup_rows (
                source_id text NOT NULL REFERENCES legacy_import.dedup_sources(source_id) ON DELETE CASCADE,
                table_name text NOT NULL,
                row_key text NOT NULL,
                payload jsonb NOT NULL,
                payload_sha256 char(64) NOT NULL,
                last_seen_sha256 char(64) NOT NULL,
                present_in_latest boolean NOT NULL DEFAULT true,
                PRIMARY KEY (source_id, table_name, row_key)
            )""")
            cur.execute("CREATE INDEX IF NOT EXISTS ix_dedup_rows_source_table ON legacy_import.dedup_rows(source_id, table_name)")
            cur.execute("SELECT completed FROM legacy_import.dedup_runs WHERE source_id=%s AND source_sha256=%s", (args.source_id, source_hash))
            previous = cur.fetchone()
            if previous and previous[0]:
                print("Этот точный снимок уже импортирован для данного --source-id; повторная запись не требуется.")
                return 0
            cur.execute("INSERT INTO legacy_import.dedup_sources(source_id) VALUES (%s) ON CONFLICT (source_id) DO NOTHING", (args.source_id,))
            cur.execute("""INSERT INTO legacy_import.dedup_runs
                (source_id, source_sha256, source_filename, table_count, row_count, completed)
                VALUES (%s,%s,%s,%s,%s,false)
                ON CONFLICT (source_id, source_sha256) DO UPDATE SET
                    source_filename=EXCLUDED.source_filename, table_count=EXCLUDED.table_count,
                    row_count=EXCLUDED.row_count, completed=false, completed_at=NULL""",
                (args.source_id, source_hash, args.database.name, len(tables), total))
            cur.execute("UPDATE legacy_import.dedup_rows SET present_in_latest=false WHERE source_id=%s", (args.source_id,))

            src = sqlite3.connect(f"file:{args.database.resolve().as_posix()}?mode=ro", uri=True)
            src.row_factory = sqlite3.Row
            try:
                for table, pk_cols, _ in tables:
                    cursor = src.execute(f"SELECT * FROM {quote_ident(table)}")
                    seen_no_pk: dict[str, int] = {}
                    ordinal = 0
                    while True:
                        batch_rows = cursor.fetchmany(args.batch_size)
                        if not batch_rows:
                            break
                        values = []
                        for row in batch_rows:
                            payload = {key: json_value(row[key]) for key in row.keys()}
                            payload_text = stable_json(payload)
                            payload_hash = hashlib.sha256(payload_text.encode("utf-8")).hexdigest()
                            duplicate_ordinal = seen_no_pk.get(payload_hash, 0)
                            seen_no_pk[payload_hash] = duplicate_ordinal + 1
                            row_key = make_row_key(payload, pk_cols, payload_hash, duplicate_ordinal)
                            values.append((args.source_id, table, row_key, Jsonb(json.loads(payload_text)), payload_hash, source_hash))
                            ordinal += 1
                        cur.executemany("""INSERT INTO legacy_import.dedup_rows
                            (source_id, table_name, row_key, payload, payload_sha256, last_seen_sha256, present_in_latest)
                            VALUES (%s,%s,%s,%s,%s,%s,true)
                            ON CONFLICT (source_id, table_name, row_key) DO UPDATE SET
                                payload=EXCLUDED.payload, payload_sha256=EXCLUDED.payload_sha256,
                                last_seen_sha256=EXCLUDED.last_seen_sha256, present_in_latest=true""", values)
                    print(f"  Обработана таблица {table}: {ordinal} строк")
            finally:
                src.close()

            # Detect if the SQLite file changed while it was being read; transaction rollback prevents a mixed snapshot.
            if sha256_file(args.database) != source_hash:
                raise RuntimeError("Исходный файл изменился во время импорта. Транзакция отменена; остановите старую программу и создайте новую резервную копию.")
            cur.execute("SELECT COUNT(*) FROM legacy_import.dedup_rows WHERE source_id=%s AND present_in_latest=true", (args.source_id,))
            staged_count = cur.fetchone()[0]
            cur.execute("""UPDATE legacy_import.dedup_runs SET completed=true, completed_at=now()
                WHERE source_id=%s AND source_sha256=%s""", (args.source_id, source_hash))
            cur.execute("""UPDATE legacy_import.dedup_sources SET latest_sha256=%s,
                latest_imported_at=now(), latest_row_count=%s WHERE source_id=%s""", (source_hash, staged_count, args.source_id))
            cur.execute("SELECT table_name, COUNT(*) FROM legacy_import.dedup_rows WHERE source_id=%s AND present_in_latest=true GROUP BY table_name ORDER BY table_name", (args.source_id,))
            print("Сверка текущего снимка в PostgreSQL:")
            for table, count in cur.fetchall():
                print(f"  {table}: {count}")
            cur.execute("SELECT COUNT(*) FROM legacy_import.dedup_rows WHERE source_id=%s AND present_in_latest=false", (args.source_id,))
            stale_count = cur.fetchone()[0]
            print(f"Строк из предыдущих снимков, отсутствующих в текущем: {stale_count}. Они сохранены, но помечены present_in_latest=false.")
            if staged_count != total:
                raise RuntimeError(f"Контрольная сумма количества строк не совпала: источник={total}, PostgreSQL={staged_count}. Транзакция отменена.")
    print("Снимок импортирован. Рабочие таблицы API не изменялись; повторные строки по стабильным ключам не создаются.")
    return 0

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ОШИБКА: {exc}", file=sys.stderr)
        raise
