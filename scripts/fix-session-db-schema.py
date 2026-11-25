#!/usr/bin/env python3
"""修复 SQLite 会话库的列名，使之与 ORM（LuBan.Orm，EnableUnderLine=true）实际使用的下划线列名一致。

背景：宿主在库文件大于 100KB 时会跳过 CodeFirst，旧版本靠手工 ALTER 补列；其中若干列被写成了
PascalCase（DurationMs / Segments / IsCompacted / WorkspaceId / ModelName）。SQLite 标识符大小写
不敏感，单词列（Segments / Attachments）碰巧能命中，多词列（DurationMs）永远对不上，查询即报
"no such column: duration_ms"。

本脚本按实体声明幂等修复：
  * 表内只有驼峰列、下划线列缺失        -> RENAME COLUMN（保留数据）
  * 两边都缺                            -> ADD COLUMN
  * 下划线列已存在、驼峰列为空          -> DROP COLUMN（清掉死列）
  * 下划线列已存在、驼峰列仍有数据      -> 只报告，不擅自删（需人工确认合并）

用法：
  python scripts/fix-session-db-schema.py <db 路径> [<db 路径> ...] [--dry-run]
"""

from __future__ import annotations

import sqlite3
import sys

# (列名, ADD COLUMN 时的列定义)。列名与 DbSession / DbSessionMessage 属性经 ToUnderLine 后的落库名一致；
# 类型与 SqlSugar CodeFirst 建出的 DDL 对齐（bool -> bit，分页字段 -> varchar(n)）。
EXPECTED_COLUMNS: dict[str, list[tuple[str, str]]] = {
    "ai_session_message": [
        ("id", "bigint"),
        ("session_id", "varchar(64) NOT NULL DEFAULT ''"),
        ("role", "varchar(32) NOT NULL DEFAULT ''"),
        ("content", "text NOT NULL DEFAULT ''"),
        ("thinking", "text"),
        ("segments", "text"),
        ("tokens", "integer"),
        ("duration_ms", "integer"),
        ("tool_calls", "text"),
        ("attachments", "text"),
        ("is_compacted", "bit NOT NULL DEFAULT 0"),
        ("create_time", "datetime NOT NULL DEFAULT '1970-01-01 00:00:00'"),
        ("update_time", "datetime"),
        ("create_user_id", "bigint"),
        ("create_user_name", "varchar(64)"),
        ("update_user_id", "bigint"),
        ("update_user_name", "varchar(64)"),
        ("is_delete", "bit NOT NULL DEFAULT 0"),
    ],
    "ai_session": [
        ("id", "bigint"),
        ("session_id", "varchar(64) NOT NULL DEFAULT ''"),
        ("user_id", "varchar(64)"),
        ("title", "varchar(256)"),
        ("model_name", "varchar(128)"),
        ("provider", "varchar(64)"),
        ("message_count", "integer NOT NULL DEFAULT 0"),
        ("total_tokens", "integer NOT NULL DEFAULT 0"),
        ("workspace_id", "varchar(64)"),
        ("reasoning_effort", "varchar(32)"),
        ("context_tier", "integer"),
        ("create_time", "datetime NOT NULL DEFAULT '1970-01-01 00:00:00'"),
        ("update_time", "datetime"),
        ("create_user_id", "bigint"),
        ("create_user_name", "varchar(64)"),
        ("update_user_id", "bigint"),
        ("update_user_name", "varchar(64)"),
        ("is_delete", "bit NOT NULL DEFAULT 0"),
    ],
}


def to_under_line(text: str) -> str:
    """等价于 LuBan.Common StringUtil.ToUnderLine（已含下划线则原样返回）。"""
    if not text or "_" in text:
        return text
    out = text[0].lower()
    for i in range(1, len(text)):
        ch = text[i]
        if not ch.isupper():
            out += ch
            continue
        prev_lower = text[i - 1].islower()
        next_lower = i + 1 < len(text) and text[i + 1].islower()
        if prev_lower or next_lower:
            out += "_" + ch.lower()
        else:
            out += ch.lower()
    return out


def existing_columns(conn: sqlite3.Connection, table: str) -> list[str]:
    return [row[1] for row in conn.execute(f'PRAGMA table_info("{table}")').fetchall()]


def declared_type(conn: sqlite3.Connection, table: str, column: str) -> str:
    for row in conn.execute(f'PRAGMA table_info("{table}")'):
        if row[1] == column:
            return (row[2] or "").lower()
    return ""


def has_meaningful_data(conn: sqlite3.Connection, table: str, column: str) -> bool:
    """把「全 0 / 全空串 / 全 NULL」视为无数据：ALTER 出来的兜底列会带 NOT NULL DEFAULT 0，按 IS NOT NULL 判断会误判。"""
    typ = declared_type(conn, table, column)
    if any(k in typ for k in ("char", "text", "clob")):
        cond = f'"{column}" IS NOT NULL AND "{column}" <> \'\''
    else:
        cond = f'"{column}" IS NOT NULL AND "{column}" <> 0'
    return conn.execute(f'SELECT 1 FROM "{table}" WHERE {cond} LIMIT 1').fetchone() is not None


def fix_table(conn: sqlite3.Connection, table: str, expected: list[tuple[str, str]], dry_run: bool) -> None:
    columns = existing_columns(conn, table)
    if not columns:
        print(f"  [跳过] {table}：表不存在")
        return

    for name, ddl in expected:
        columns = existing_columns(conn, table) if not dry_run else columns
        actual = next((c for c in columns if c.lower() == name.lower()), None)
        if actual is not None:
            if actual != name:
                # 同表里已有目标名（大小写不同的同名变体）时改名会撞列，交给人工确认
                if any(c == name for c in columns):
                    print(f"  [保留] {table}.{actual}：{name} 已存在，需人工确认合并")
                    continue
                print(f"  [改名] {table}.{actual} -> {name}"
                      + ("（保数据）" if has_meaningful_data(conn, table, actual) else "（空列）"))
                if not dry_run:
                    conn.execute(f'ALTER TABLE "{table}" RENAME COLUMN "{actual}" TO "{name}"')
            continue

        # 只有驼峰列（如 DurationMs）时直接改名，避免新增空列把数据留在死列里
        source = next((c for c in columns if c != c.lower() and to_under_line(c) == name), None)
        if source is not None:
            print(f"  [改名] {table}.{source} -> {name}（保数据）")
            if not dry_run:
                conn.execute(f'ALTER TABLE "{table}" RENAME COLUMN "{source}" TO "{name}"')
        else:
            print(f"  [新增] {table}.{name} {ddl}")
            if not dry_run:
                conn.execute(f'ALTER TABLE "{table}" ADD COLUMN "{name}" {ddl}')


def drop_dead_pascal_columns(conn: sqlite3.Connection, table: str, dry_run: bool, supports_drop: bool) -> None:
    """下划线列就位后，清掉仍然残留的驼峰重复列（仅当无数据）。"""
    columns = existing_columns(conn, table)
    lower_names = {c.lower() for c in columns}
    for actual in columns:
        if actual == actual.lower():
            continue
        target = to_under_line(actual)
        if target.lower() not in lower_names or target.lower() == actual.lower():
            continue
        if has_meaningful_data(conn, table, actual):
            print(f"  [保留] {table}.{actual}：{target} 已存在，但该驼峰列仍有数据，需人工确认合并")
            continue
        if not supports_drop:
            print(f"  [待删] {table}.{actual}（空列；当前 SQLite 不支持 DROP COLUMN，请升级 3.35+ 后重跑）")
            continue
        print(f"  [删列] {table}.{actual}（空列，已被 {target} 取代）")
        if not dry_run:
            conn.execute(f'ALTER TABLE "{table}" DROP COLUMN "{actual}"')


def verify(conn: sqlite3.Connection, table: str, expected: list[tuple[str, str]]) -> bool | None:
    """按 PRAGMA 列清单校验。

    不能用 SELECT "列名" 校验：SQLite 遇到无法解析的双引号标识符会退化成字符串字面量，
    SELECT "不存在的列" 照样返回一行，永真。
    """
    columns = existing_columns(conn, table)
    if not columns:
        print(f"  [跳过] {table}：表不存在，无需校验（新库由 CodeFirst 建表）")
        return None
    missing = [name for name, _ in expected if name not in columns]
    if missing:
        print(f"  [失败] {table} 缺少列：{', '.join(missing)}")
        return False
    return True


def fix_database(path: str, dry_run: bool) -> bool:
    print(f"== {path}")
    conn = sqlite3.connect(path, timeout=10)
    conn.isolation_level = None  # 关掉 python 的隐式事务，DDL 由下面的显式 BEGIN 包裹
    try:
        version = tuple(int(x) for x in conn.execute("SELECT sqlite_version()").fetchone()[0].split("."))
        supports_drop = version >= (3, 35, 0)

        if not dry_run:
            conn.execute("BEGIN IMMEDIATE")  # DDL 在 python 里不会被隐式事务覆盖，必须显式开
        try:
            for table, expected in EXPECTED_COLUMNS.items():
                fix_table(conn, table, expected, dry_run)
                drop_dead_pascal_columns(conn, table, dry_run, supports_drop)
        except Exception:
            if not dry_run and conn.in_transaction:
                conn.execute("ROLLBACK")
            raise
        if not dry_run:
            conn.execute("COMMIT")

        if dry_run:
            print("  [dry-run] 未提交任何改动")
            return True

        try:
            conn.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        except sqlite3.Error as ex:
            print(f"  [提示] WAL checkpoint 未执行：{ex}")
        print(f"  [完成] sqlite {conn.execute('SELECT sqlite_version()').fetchone()[0]}")

        ok = True
        for table, expected in EXPECTED_COLUMNS.items():
            result = verify(conn, table, expected)
            if result is False:
                ok = False
        print("  [校验] 全部预期列存在" if ok else "  [校验] 存在缺失的预期列")
        return ok
    except sqlite3.OperationalError as ex:
        print(f"  [失败] {ex}（若宿主仍在运行，先退出程序再重试）")
        return False
    finally:
        conn.close()


def main(argv: list[str]) -> int:
    unknown = [a for a in argv[1:] if a.startswith("--") and a != "--dry-run"]
    if unknown:
        print(f"未知参数：{' '.join(unknown)}\n（仅支持 --dry-run）")
        return 2

    paths = [a for a in argv[1:] if not a.startswith("--")]
    dry_run = "--dry-run" in argv[1:]
    if not paths:
        print(__doc__)
        return 2

    all_ok = True
    for path in paths:
        all_ok = fix_database(path, dry_run) and all_ok
    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))