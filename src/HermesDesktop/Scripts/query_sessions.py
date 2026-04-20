import sqlite3

def sanitize_preview(text):
    if text is None:
        return None
    return text.replace("\n", " ").replace("\r", " ").strip()[:120]

def sanitize_title(v):
    text = sanitize_preview(stringify(v))
    if not text:
        return None
    if text.lower().startswith("<think>"):
        return None
    return text[:120]

def session_matches_query(item, query):
    for field in (item.get("id"), item.get("title"), item.get("preview")):
        text = stringify(field)
        if text and query in text.lower():
            return True
    return False

def query_sqlite(hermes_home, home):
    for candidate in iter_session_store_candidates(hermes_home, home):
        try:
            conn = sqlite3.connect(f"file:{candidate}?mode=ro", uri=True)
            conn.execute("PRAGMA busy_timeout = 2000")
            tables = [r[0] for r in conn.execute(
                "SELECT name FROM sqlite_master WHERE type='table'").fetchall()]
            st = choose_table(tables, "sessions")
            mt = choose_table(tables, "messages")
            if st and mt:
                return conn, st, mt
            conn.close()
        except Exception:
            continue
    return None, None, None

def build_jsonl_items(hermes_home):
    sessions_dir = hermes_home / "sessions"
    if not sessions_dir.exists():
        return []
    items = []
    for f in sorted(sessions_dir.rglob("*.jsonl"), key=lambda x: x.stat().st_mtime, reverse=True):
        try:
            lines = f.read_text(encoding="utf-8").strip().split("\n")
            if not lines:
                continue
            first = json.loads(lines[0])
            items.append({
                "id": f.stem,
                "title": sanitize_title(first.get("content", f.stem)),
                "model": first.get("model"),
                "started_at": f.stat().st_mtime,
                "last_active": f.stat().st_mtime,
                "message_count": len(lines),
                "preview": sanitize_preview(stringify(first.get("content"))),
            })
        except Exception:
            continue
    return items

try:
    home = pathlib.Path.home()
    hermes_home = resolved_hermes_home()
    conn, session_table, message_table = query_sqlite(hermes_home, home)

    if conn is None:
        items = build_jsonl_items(hermes_home)
        if not items:
            fail(f"No readable session store found under {tilde(hermes_home, home)}.")
    else:
        scols = [r[1] for r in conn.execute(f"PRAGMA table_info({quote_ident(session_table)})").fetchall()]
        mcols = [r[1] for r in conn.execute(f"PRAGMA table_info({quote_ident(message_table)})").fetchall()]

        sid_col = choose_column(scols, ["id", "session_id"])
        title_col = choose_column(scols, ["title", "summary", "name"])
        started_col = choose_column(scols, ["started_at", "created_at", "timestamp"])
        model_col = choose_column(scols, ["model"])
        msg_sid_col = choose_column(mcols, ["session_id", "conversation_id"])
        msg_content_col = choose_column(mcols, ["content", "text", "body"])
        msg_role_col = choose_column(mcols, ["role", "sender", "author"])
        msg_ts_col = choose_column(mcols, ["timestamp", "created_at", "time"])

        if not sid_col or not msg_sid_col:
            fail("Unsupported session schema.")

        rows = conn.execute(f"SELECT * FROM {quote_ident(session_table)}").fetchall()
        items = []
        for row in rows:
            record = dict(zip(scols, row))
            session_id = stringify(record.get(sid_col))
            if not session_id:
                continue

            if msg_ts_col:
                stats = conn.execute(
                    f"SELECT COUNT(*), MAX({quote_ident(msg_ts_col)}) "
                    f"FROM {quote_ident(message_table)} WHERE {quote_ident(msg_sid_col)} = ?",
                    (session_id,)).fetchone()
            else:
                stats = conn.execute(
                    f"SELECT COUNT(*), NULL FROM {quote_ident(message_table)} "
                    f"WHERE {quote_ident(msg_sid_col)} = ?",
                    (session_id,)).fetchone()

            message_count = int(stats[0]) if stats else 0
            last_active = stats[1] if stats and stats[1] else record.get(started_col)

            preview = None
            if msg_content_col:
                pq = f"SELECT {quote_ident(msg_content_col)} FROM {quote_ident(message_table)} WHERE {quote_ident(msg_sid_col)} = ?"
                args = [session_id]
                if msg_role_col:
                    pq += f" AND {quote_ident(msg_role_col)} IN ('user','assistant','system')"
                pq += f" ORDER BY "
                if msg_ts_col:
                    pq += f"{quote_ident(msg_ts_col)}, "
                pq += "rowid LIMIT 1"
                pr = conn.execute(pq, args).fetchone()
                if pr and pr[0]:
                    preview = sanitize_preview(stringify(pr[0]))

            title = sanitize_title(record.get(title_col)) if title_col else None
            if title is None and preview:
                title = preview[:80]

            items.append({
                "id": session_id,
                "title": title,
                "model": stringify(record.get(model_col)) if model_col else None,
                "started_at": record.get(started_col),
                "last_active": last_active,
                "message_count": message_count,
                "preview": preview,
            })

        conn.close()
        items.sort(key=lambda x: x.get("last_active") or x.get("started_at") or 0, reverse=True)

    query = payload.get("query")
    if query:
        query = query.strip().lower()
        if query:
            items = [i for i in items if session_matches_query(i, query)]

    start = int(payload.get("offset", 0))
    limit = int(payload.get("limit", 50))
    print(json.dumps({
        "ok": True,
        "total_count": len(items),
        "items": items[start:start + limit],
    }, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to read the remote Hermes session list: {exc}")
