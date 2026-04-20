import sqlite3

try:
    session_id = stringify(payload.get("session_id"))
    if not session_id:
        fail("The session ID is required.")

    home = pathlib.Path.home()
    hermes_home = resolved_hermes_home()
    deleted_session_rows = 0
    deleted_message_rows = 0
    deleted_jsonl = False

    for candidate in iter_session_store_candidates(hermes_home, home):
        try:
            conn = sqlite3.connect(str(candidate))
            conn.execute("PRAGMA busy_timeout = 2000")
            tables = [r[0] for r in conn.execute(
                "SELECT name FROM sqlite_master WHERE type='table'").fetchall()]
            st = choose_table(tables, "sessions")
            mt = choose_table(tables, "messages")
            if st:
                scols = [r[1] for r in conn.execute(f"PRAGMA table_info({quote_ident(st)})").fetchall()]
                sid_col = choose_column(scols, ["id", "session_id"])
                if sid_col:
                    if mt:
                        mcols = [r[1] for r in conn.execute(f"PRAGMA table_info({quote_ident(mt)})").fetchall()]
                        msid_col = choose_column(mcols, ["session_id", "conversation_id"])
                        if msid_col:
                            with conn:
                                deleted_message_rows = conn.execute(
                                    f"DELETE FROM {quote_ident(mt)} WHERE {quote_ident(msid_col)} = ?",
                                    (session_id,)).rowcount
                    with conn:
                        deleted_session_rows = conn.execute(
                            f"DELETE FROM {quote_ident(st)} WHERE {quote_ident(sid_col)} = ?",
                            (session_id,)).rowcount
            conn.close()
            if deleted_session_rows > 0:
                break
        except Exception:
            continue

    sessions_dir = hermes_home / "sessions"
    if sessions_dir.exists():
        for f in sessions_dir.rglob("*.jsonl"):
            if f.stem == session_id:
                f.unlink()
                deleted_jsonl = True
                break

    if deleted_session_rows <= 0 and not deleted_jsonl:
        fail(f"No session matching '{session_id}' was found to delete.")

    print(json.dumps({"ok": True}, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to delete the remote Hermes session: {exc}")
