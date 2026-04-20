import sqlite3

try:
    session_id = payload.get("session_id")
    if not session_id:
        fail("The session ID is required.")

    home = pathlib.Path.home()
    hermes_home = resolved_hermes_home()
    conn = None
    session_table = None
    message_table = None

    for candidate in iter_session_store_candidates(hermes_home, home):
        try:
            c = sqlite3.connect(f"file:{candidate}?mode=ro", uri=True)
            c.execute("PRAGMA busy_timeout = 2000")
            tables = [r[0] for r in c.execute(
                "SELECT name FROM sqlite_master WHERE type='table'").fetchall()]
            st = choose_table(tables, "sessions")
            mt = choose_table(tables, "messages")
            if st and mt:
                conn = c
                session_table, message_table = st, mt
                break
            c.close()
        except Exception:
            continue

    if conn is None:
        sessions_dir = hermes_home / "sessions"
        artifact = None
        if sessions_dir.exists():
            for f in sessions_dir.rglob("*.jsonl"):
                if f.stem == session_id:
                    artifact = f
                    break

        if artifact is None:
            fail(f"No session found for '{session_id}'.")

        items = []
        with artifact.open("r", encoding="utf-8") as handle:
            for idx, line in enumerate(handle, start=1):
                line = line.strip()
                if not line:
                    continue
                try:
                    record = json.loads(line)
                except Exception:
                    continue
                if not isinstance(record, dict):
                    continue
                role = stringify(record.get("role")) or "event"
                if role == "session_meta":
                    continue
                items.append({
                    "id": str(idx),
                    "role": role,
                    "content": stringify(record.get("content")),
                    "timestamp": record.get("timestamp"),
                })
        print(json.dumps({"ok": True, "items": items}, ensure_ascii=False))
    else:
        mcols = [r[1] for r in conn.execute(
            f"PRAGMA table_info({quote_ident(message_table)})").fetchall()]

        mid_col = choose_column(mcols, ["id", "message_id"])
        msid_col = choose_column(mcols, ["session_id", "conversation_id"])
        mrole_col = choose_column(mcols, ["role", "sender", "author"])
        mcontent_col = choose_column(mcols, ["content", "text", "body"])
        mts_col = choose_column(mcols, ["timestamp", "created_at", "time"])

        if not msid_col:
            fail("Unsupported message schema.")

        q = f"SELECT * FROM {quote_ident(message_table)} WHERE {quote_ident(msid_col)} = ? ORDER BY "
        if mts_col:
            q += f"{quote_ident(mts_col)}, "
        if mid_col:
            q += quote_ident(mid_col)
        else:
            q += "rowid"

        rows = conn.execute(q, (session_id,)).fetchall()
        items = []
        for row in rows:
            record = dict(zip(mcols, row))
            items.append({
                "id": stringify(record.get(mid_col)) if mid_col else str(len(items) + 1),
                "role": stringify(record.get(mrole_col)) if mrole_col else None,
                "content": stringify(record.get(mcontent_col)) if mcontent_col else None,
                "timestamp": record.get(mts_col) if mts_col else None,
            })

        conn.close()
        print(json.dumps({"ok": True, "items": items}, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to read the remote Hermes transcript: {exc}")
