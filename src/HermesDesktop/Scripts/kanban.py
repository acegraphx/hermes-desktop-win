import contextlib
import inspect
import json
import os
import pathlib
import re
import shlex
import shutil
import sqlite3
import subprocess
import sys
import time

DEFAULT_BOARD = "default"
_BOARD_SLUG_RE = re.compile(r"^[a-z0-9][a-z0-9\-_]{0,63}$")

def kanban_home_path():
    home = pathlib.Path.home()
    requested = expand_remote_path(payload.get("kanban_home") or "~/.hermes", home)
    return requested or (home / ".hermes")

def normalize_board_slug(slug):
    if slug is None:
        return None
    value = str(slug).strip().lower()
    if not value:
        return None
    if not _BOARD_SLUG_RE.match(value):
        fail(
            f"Invalid Kanban board slug {slug!r}: use 1-64 lowercase letters, "
            "numbers, hyphens, or underscores."
        )
    return value

def requested_board_slug():
    return normalize_board_slug(payload.get("board_slug")) or DEFAULT_BOARD

def board_dir(board_slug=None):
    return kanban_home_path() / "kanban" / "boards" / (normalize_board_slug(board_slug) or DEFAULT_BOARD)

def current_board_file():
    return kanban_home_path() / "kanban" / "current"

def current_board_slug(kb=None):
    if kb is not None and hasattr(kb, "get_current_board"):
        try:
            return normalize_board_slug(kb.get_current_board()) or DEFAULT_BOARD
        except Exception:
            pass
    try:
        path = current_board_file()
        if path.exists():
            return normalize_board_slug(path.read_text(encoding="utf-8")) or DEFAULT_BOARD
    except Exception:
        pass
    return DEFAULT_BOARD

def try_set_current_board(kb, board_slug):
    normalized = normalize_board_slug(board_slug) or DEFAULT_BOARD
    if kb is not None and hasattr(kb, "set_current_board"):
        kb.set_current_board(normalized)
        return
    path = current_board_file()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(normalized + "\n", encoding="utf-8")

def kanban_db_path(board_slug=None):
    normalized = normalize_board_slug(board_slug) or DEFAULT_BOARD
    if normalized == DEFAULT_BOARD:
        return kanban_home_path() / "kanban.db"
    return board_dir(normalized) / "kanban.db"

def board_metadata_path(board_slug=None):
    return board_dir(board_slug) / "board.json"

def worker_log_path(task_id, board_slug=None):
    normalized = normalize_board_slug(board_slug) or DEFAULT_BOARD
    if normalized == DEFAULT_BOARD:
        return kanban_home_path() / "kanban" / "logs" / f"{task_id}.log"
    return board_dir(normalized) / "logs" / f"{task_id}.log"

def default_board_display_name(slug):
    parts = [part.capitalize() for part in str(slug).replace("_", "-").split("-") if part]
    return " ".join(parts) or str(slug)

def read_board_metadata_direct(board_slug=None):
    normalized = normalize_board_slug(board_slug) or DEFAULT_BOARD
    meta = {
        "slug": normalized,
        "name": default_board_display_name(normalized),
        "description": "",
        "icon": "",
        "color": "",
        "created_at": None,
        "archived": False,
    }
    try:
        path = board_metadata_path(normalized)
        if path.exists():
            raw = json.loads(path.read_text(encoding="utf-8"))
            if isinstance(raw, dict):
                raw["slug"] = normalized
                meta.update(raw)
    except Exception:
        pass
    meta["db_path"] = str(kanban_db_path(normalized))
    return meta

def board_exists(board_slug, kb=None):
    normalized = normalize_board_slug(board_slug) or DEFAULT_BOARD
    if normalized == DEFAULT_BOARD:
        return True
    if kb is not None and hasattr(kb, "board_exists"):
        try:
            return bool(kb.board_exists(normalized))
        except Exception:
            pass
    directory = board_dir(normalized)
    return directory.is_dir() or (directory / "kanban.db").exists()

def supports_keyword(function, keyword):
    try:
        return keyword in inspect.signature(function).parameters
    except Exception:
        return False

def require_board_api(kb, operation):
    if kb is None or not hasattr(kb, "list_boards"):
        fail(f"This Hermes Agent build does not support {operation}. Run `hermes update` on the host.")

def connect_for_board(kb, board_slug, writable=False):
    normalized = normalize_board_slug(board_slug) or DEFAULT_BOARD
    if kb is not None:
        if supports_keyword(kb.connect, "board"):
            return kb.connect(board=normalized)
        if normalized != DEFAULT_BOARD:
            fail("This Hermes Agent build does not support multiple Kanban boards. Run `hermes update` on the host.")
        return kb.connect(kanban_db_path(normalized))

    db_path = kanban_db_path(normalized)
    if writable:
        db_path.parent.mkdir(parents=True, exist_ok=True)
        conn = sqlite3.connect(str(db_path), timeout=30)
        conn.row_factory = sqlite3.Row
        return conn

    conn = connect_sqlite_readonly(db_path)
    conn.row_factory = sqlite3.Row
    return conn

def kanban_cli_args(board_slug, tail):
    normalized = normalize_board_slug(board_slug) or DEFAULT_BOARD
    args = ["kanban"]
    if normalized != DEFAULT_BOARD:
        args.extend(["--board", normalized])
    args.extend(list(tail))
    return args

def board_counts(board_slug):
    path = kanban_db_path(board_slug)
    if not path.exists():
        return {}
    conn = None
    try:
        conn = connect_sqlite_readonly(path)
        conn.row_factory = sqlite3.Row
        if not table_exists(conn, "tasks"):
            return {}
        rows = conn.execute("SELECT status, COUNT(*) AS n FROM tasks GROUP BY status").fetchall()
        return {row["status"]: int(row["n"] or 0) for row in rows}
    except Exception:
        return {}
    finally:
        if conn is not None:
            conn.close()

def hydrate_board_metadata(meta, kb=None):
    board = dict(meta or {})
    slug = normalize_board_slug(board.get("slug")) or DEFAULT_BOARD
    board["slug"] = slug
    if not board.get("name"):
        board["name"] = default_board_display_name(slug)
    if "archived" not in board:
        board["archived"] = False
    try:
        path = None
        if kb is not None and hasattr(kb, "kanban_db_path"):
            if supports_keyword(kb.kanban_db_path, "board"):
                path = kb.kanban_db_path(board=slug)
            elif slug == DEFAULT_BOARD:
                path = kb.kanban_db_path()
        if path is None:
            path = kanban_db_path(slug)
        board["db_path"] = tilde(pathlib.Path(path), pathlib.Path.home())
    except Exception:
        board["db_path"] = tilde(kanban_db_path(slug), pathlib.Path.home())
    counts = board_counts(slug)
    board["counts"] = counts
    board["total"] = sum(counts.values())
    board["is_current"] = slug == current_board_slug(kb)
    return board

def list_boards_response(include_archived=False):
    kb = import_kanban_module(required=False)
    entries = []
    seen = set()
    supports_board_management = bool(
        kb is not None
        and hasattr(kb, "list_boards")
        and hasattr(kb, "create_board")
        and hasattr(kb, "remove_board")
        and hasattr(kb, "kanban_db_path")
        and supports_keyword(kb.kanban_db_path, "board")
        and supports_keyword(kb.connect, "board")
    )
    if kb is not None and hasattr(kb, "list_boards"):
        try:
            entries = [hydrate_board_metadata(item, kb) for item in kb.list_boards(include_archived=bool(include_archived))]
            seen = {item["slug"] for item in entries}
        except Exception:
            entries = []
            seen = set()

    if not entries:
        default_meta = hydrate_board_metadata(read_board_metadata_direct(DEFAULT_BOARD), kb)
        entries.append(default_meta)
        seen.add(DEFAULT_BOARD)
        root = kanban_home_path() / "kanban" / "boards"
        if root.is_dir():
            for child in sorted(root.iterdir(), key=lambda item: item.name.lower()):
                if not child.is_dir():
                    continue
                slug = str(child.name).strip().lower()
                if not _BOARD_SLUG_RE.match(slug):
                    continue
                if not slug or slug in seen:
                    continue
                has_board = (child / "kanban.db").exists() or (child / "board.json").exists()
                if not has_board:
                    continue
                meta = hydrate_board_metadata(read_board_metadata_direct(slug), kb)
                if meta.get("archived") and not include_archived:
                    continue
                entries.append(meta)
                seen.add(slug)

    entries.sort(key=lambda item: (0 if item.get("slug") == DEFAULT_BOARD else 1, str(item.get("slug") or "")))
    return {
        "boards": entries,
        "current": current_board_slug(kb),
        "supports_board_management": supports_board_management,
    }

def find_hermes_binary():
    candidate = shutil.which("hermes")
    if candidate:
        return candidate
    fallback = pathlib.Path.home() / ".local" / "bin" / "hermes"
    if fallback.exists() and os.access(fallback, os.X_OK):
        return str(fallback)
    venv_fallback = pathlib.Path.home() / ".hermes" / "hermes-agent" / "venv" / "bin" / "hermes"
    if venv_fallback.exists() and os.access(venv_fallback, os.X_OK):
        return str(venv_fallback)
    return None

def run_hermes_cli(args, expect_json=False):
    hermes_binary = find_hermes_binary()
    if hermes_binary is None:
        fail("Hermes CLI was not found on the active host.")
    home = pathlib.Path.home()
    env = os.environ.copy()
    env["HERMES_HOME"] = str(kanban_home_path())
    env["HERMES_KANBAN_HOME"] = str(kanban_home_path())
    path_entries = [
        str(home / ".local" / "bin"),
        str(home / ".hermes" / "hermes-agent" / "venv" / "bin"),
        str(home / ".cargo" / "bin"),
        "/opt/homebrew/bin",
        "/usr/local/bin",
        env.get("PATH", ""),
    ]
    env["PATH"] = os.pathsep.join([entry for entry in path_entries if entry])
    completed = subprocess.run(
        [hermes_binary] + list(args),
        capture_output=True,
        text=True,
        env=env,
    )
    if completed.returncode != 0:
        message = (completed.stderr or completed.stdout or "Hermes Kanban command failed.").strip()
        fail(message)
    output = (completed.stdout or "").strip()
    if not expect_json:
        return output
    try:
        return json.loads(output or "{}")
    except Exception as exc:
        fail(f"Hermes Kanban command returned invalid JSON: {exc}")

def add_hermes_agent_import_paths():
    home = pathlib.Path.home()
    candidates = [
        home / ".hermes" / "hermes-agent",
        kanban_home_path() / "hermes-agent",
    ]
    for agent_root in list(candidates):
        venv_lib = agent_root / "venv" / "lib"
        if venv_lib.is_dir():
            for site_packages in sorted(venv_lib.glob("python*/site-packages")):
                candidates.append(site_packages)
    for candidate in candidates:
        try:
            path = str(candidate)
        except Exception:
            continue
        if path and pathlib.Path(path).exists() and path not in sys.path:
            sys.path.insert(0, path)

def load_hermes_env_file():
    home = pathlib.Path.home()
    seen = set()
    for path in [kanban_home_path() / ".env", home / ".hermes" / ".env"]:
        if path in seen:
            continue
        seen.add(path)
        if not path.exists():
            continue
        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except Exception:
            continue
        for line in lines:
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                continue
            try:
                parts = shlex.split(stripped, comments=True, posix=True)
            except Exception:
                continue
            if not parts:
                continue
            if parts[0] == "export":
                parts = parts[1:]
            if len(parts) != 1 or "=" not in parts[0]:
                continue
            key, value = parts[0].split("=", 1)
            key = key.strip()
            if not re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", key):
                continue
            if key not in os.environ:
                os.environ[key] = value

def import_kanban_module(required=False):
    os.environ["HERMES_HOME"] = str(kanban_home_path())
    os.environ["HERMES_KANBAN_HOME"] = str(kanban_home_path())
    load_hermes_env_file()
    try:
        import hermes_cli.kanban_db as kb
        return kb
    except Exception as exc:
        first_error = exc
    add_hermes_agent_import_paths()
    try:
        import hermes_cli.kanban_db as kb
        return kb
    except Exception as exc:
        if required:
            raise ImportError(f"{exc}; initial import failed with: {first_error}")
        return None

def dispatcher_status():
    os.environ["HERMES_HOME"] = str(kanban_home_path())
    add_hermes_agent_import_paths()
    load_hermes_env_file()
    try:
        import hermes_cli.kanban as kanban_cli
        running, message = kanban_cli._check_dispatcher_presence()
        return {
            "running": bool(running),
            "message": message or None,
        }
    except Exception:
        return {
            "running": None,
            "message": None,
        }

def configured_home_channels():
    try:
        add_hermes_agent_import_paths()
        load_hermes_env_file()
        from gateway.config import load_gateway_config
        gateway_config = load_gateway_config()
    except Exception:
        return []
    result = []
    try:
        platform_items = gateway_config.platforms.items()
    except Exception:
        platform_items = []
    for platform, platform_config in platform_items:
        if not platform_config:
            continue
        home_channel = getattr(platform_config, "home_channel", None)
        if not home_channel:
            continue
        platform_name = getattr(platform, "value", str(platform))
        result.append({
            "platform": platform_name,
            "chat_id": str(getattr(home_channel, "chat_id", "") or ""),
            "thread_id": str(getattr(home_channel, "thread_id", "") or ""),
            "name": str(getattr(home_channel, "name", "") or "Home"),
            "subscribed": False,
        })
    result.sort(key=lambda item: item["platform"])
    return result

def home_channel_for_platform(platform):
    normalized = normalize_text(platform)
    for home in configured_home_channels():
        if home.get("platform") == normalized:
            return home
    return None

def home_sub_matches(sub, home):
    return (
        str(sub.get("platform") or "") == str(home.get("platform") or "")
        and str(sub.get("chat_id") or "") == str(home.get("chat_id") or "")
        and str(sub.get("thread_id") or "") == str(home.get("thread_id") or "")
    )

def direct_notify_subs(conn, task_id):
    if not conn or not task_id or not table_exists(conn, "kanban_notify_subs"):
        return []
    rows = conn.execute(
        "SELECT task_id, platform, chat_id, thread_id, user_id, created_at, last_event_id "
        "FROM kanban_notify_subs WHERE task_id = ?",
        (task_id,),
    ).fetchall()
    return [dict(row) for row in rows]

def home_channels_for_task(conn, task_id):
    homes = configured_home_channels()
    if not homes:
        return []
    subscribed = direct_notify_subs(conn, task_id)
    result = []
    for home in homes:
        item = dict(home)
        item["subscribed"] = any(home_sub_matches(sub, home) for sub in subscribed)
        result.append(item)
    return result

def ensure_notify_subs_table(conn):
    if table_exists(conn, "kanban_notify_subs"):
        return
    fail("This Hermes Agent build does not support Kanban home-channel subscriptions. Run `hermes update` on the host.")

def get_task_any(kb, conn, task_id):
    if not conn or not task_id:
        return None
    if kb is not None and hasattr(kb, "get_task"):
        try:
            return kb.get_task(conn, task_id)
        except Exception:
            pass
    if not table_exists(conn, "tasks"):
        return None
    return conn.execute("SELECT id FROM tasks WHERE id = ?", (task_id,)).fetchone()

def set_home_subscription(kb, conn, task_id, home, subscribed):
    ensure_notify_subs_table(conn)
    platform = home["platform"]
    chat_id = home["chat_id"]
    thread_id = home.get("thread_id") or ""
    if subscribed:
        if kb is not None and hasattr(kb, "add_notify_sub"):
            kb.add_notify_sub(
                conn,
                task_id=task_id,
                platform=platform,
                chat_id=chat_id,
                thread_id=thread_id or None,
            )
            return
        conn.execute(
            "INSERT OR IGNORE INTO kanban_notify_subs "
            "(task_id, platform, chat_id, thread_id, user_id, created_at, last_event_id) "
            "VALUES (?, ?, ?, ?, NULL, ?, 0)",
            (task_id, platform, chat_id, thread_id, int(time.time())),
        )
        conn.commit()
        return

    if kb is not None and hasattr(kb, "remove_notify_sub"):
        kb.remove_notify_sub(
            conn,
            task_id=task_id,
            platform=platform,
            chat_id=chat_id,
            thread_id=thread_id or None,
        )
        return
    conn.execute(
        "DELETE FROM kanban_notify_subs WHERE task_id = ? AND platform = ? AND chat_id = ? AND thread_id = ?",
        (task_id, platform, chat_id, thread_id),
    )
    conn.commit()

def table_exists(conn, table_name):
    row = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name = ?",
        (table_name,),
    ).fetchone()
    return row is not None

def table_columns(conn, table_name):
    try:
        return {row["name"] for row in conn.execute(f"PRAGMA table_info({quote_ident(table_name)})")}
    except Exception:
        return set()

def int_value(value, default=None):
    if value is None:
        return default
    try:
        return int(value)
    except Exception:
        return default

def parse_json_object(value):
    if value is None:
        return None
    if isinstance(value, dict):
        return value
    try:
        parsed = json.loads(value)
        return parsed if isinstance(parsed, dict) else None
    except Exception:
        return None

def parse_json_list(value):
    if value is None:
        return []
    if isinstance(value, list):
        return [str(item) for item in value if item]
    try:
        parsed = json.loads(value)
        if isinstance(parsed, list):
            return [str(item) for item in parsed if item]
    except Exception:
        pass
    return []

WARNING_EVENT_KINDS = (
    "completion_blocked_hallucination",
    "suspected_hallucinated_references",
)

def compute_warnings_for_tasks(conn, task_ids=None):
    if not conn or not table_exists(conn, "task_events"):
        return {}
    params = ()
    if task_ids is not None:
        task_ids = [str(item) for item in task_ids if item]
        if not task_ids:
            return {}
        placeholders = ",".join(["?"] * len(task_ids))
        sql = (
            "SELECT task_id, kind, created_at FROM task_events "
            f"WHERE task_id IN ({placeholders}) AND kind IN "
            "('completion_blocked_hallucination', "
            "'suspected_hallucinated_references', 'completed', 'edited') "
            "ORDER BY task_id, id"
        )
        params = tuple(task_ids)
    else:
        sql = (
            "SELECT task_id, kind, created_at FROM task_events "
            "WHERE kind IN "
            "('completion_blocked_hallucination', "
            "'suspected_hallucinated_references', 'completed', 'edited') "
            "ORDER BY task_id, id"
        )

    result = {}
    try:
        rows = conn.execute(sql, params).fetchall()
    except Exception:
        return {}
    for row in rows:
        task_id = row["task_id"]
        kind = row["kind"]
        if kind in ("completed", "edited"):
            result.pop(task_id, None)
            continue
        bucket = result.setdefault(task_id, {"count": 0, "kinds": {}, "latest_at": 0})
        bucket["count"] += 1
        bucket["kinds"][kind] = int(bucket["kinds"].get(kind, 0) or 0) + 1
        latest = int_value(row["created_at"], 0) or 0
        if latest > int(bucket.get("latest_at") or 0):
            bucket["latest_at"] = latest
    return result

def warning_for_task(conn, task_id):
    return compute_warnings_for_tasks(conn, [task_id]).get(task_id)

def task_object_to_dict(task, conn=None):
    task_id = getattr(task, "id", "")
    parent_ids = []
    child_ids = []
    comment_count = 0
    event_count = 0
    run_count = 0
    latest_event_at = None
    if conn is not None and task_id:
        parent_ids = link_ids(conn, task_id, parents=True)
        child_ids = link_ids(conn, task_id, parents=False)
        comment_count = count_rows(conn, "task_comments", "task_id", task_id)
        event_count = count_rows(conn, "task_events", "task_id", task_id)
        run_count = count_rows(conn, "task_runs", "task_id", task_id)
        latest_event_at = latest_event_timestamp(conn, task_id)
        warnings = warning_for_task(conn, task_id)
    else:
        warnings = None
    return {
        "id": task_id,
        "title": getattr(task, "title", None),
        "body": getattr(task, "body", None),
        "assignee": getattr(task, "assignee", None),
        "status": getattr(task, "status", "unknown"),
        "priority": int_value(getattr(task, "priority", 0), 0),
        "created_by": getattr(task, "created_by", None),
        "created_at": int_value(getattr(task, "created_at", None)),
        "started_at": int_value(getattr(task, "started_at", None)),
        "completed_at": int_value(getattr(task, "completed_at", None)),
        "workspace_kind": getattr(task, "workspace_kind", "scratch"),
        "workspace_path": getattr(task, "workspace_path", None),
        "tenant": getattr(task, "tenant", None),
        "result": getattr(task, "result", None),
        "skills": parse_json_list(getattr(task, "skills", None)) if not isinstance(getattr(task, "skills", None), list) else getattr(task, "skills", []),
        "spawn_failures": int_value(getattr(task, "spawn_failures", 0), 0),
        "worker_pid": int_value(getattr(task, "worker_pid", None)),
        "last_spawn_error": getattr(task, "last_spawn_error", None),
        "max_runtime_seconds": int_value(getattr(task, "max_runtime_seconds", None)),
        "last_heartbeat_at": int_value(getattr(task, "last_heartbeat_at", None)),
        "current_run_id": int_value(getattr(task, "current_run_id", None)),
        "parent_ids": parent_ids,
        "child_ids": child_ids,
        "progress": progress_for_task(conn, task_id),
        "comment_count": comment_count,
        "event_count": event_count,
        "run_count": run_count,
        "latest_event_at": latest_event_at,
        "warnings": warnings,
    }

def task_row_to_dict(row, conn=None):
    keys = set(row.keys())
    def get(name, default=None):
        return row[name] if name in keys else default
    task_id = get("id", "")
    return {
        "id": task_id,
        "title": get("title"),
        "body": get("body"),
        "assignee": get("assignee"),
        "status": get("status", "unknown"),
        "priority": int_value(get("priority"), 0),
        "created_by": get("created_by"),
        "created_at": int_value(get("created_at")),
        "started_at": int_value(get("started_at")),
        "completed_at": int_value(get("completed_at")),
        "workspace_kind": get("workspace_kind", "scratch"),
        "workspace_path": get("workspace_path"),
        "tenant": get("tenant"),
        "result": get("result"),
        "skills": parse_json_list(get("skills")),
        "spawn_failures": int_value(get("spawn_failures"), 0),
        "worker_pid": int_value(get("worker_pid")),
        "last_spawn_error": get("last_spawn_error"),
        "max_runtime_seconds": int_value(get("max_runtime_seconds")),
        "last_heartbeat_at": int_value(get("last_heartbeat_at")),
        "current_run_id": int_value(get("current_run_id")),
        "parent_ids": link_ids(conn, task_id, parents=True) if conn else [],
        "child_ids": link_ids(conn, task_id, parents=False) if conn else [],
        "progress": progress_for_task(conn, task_id),
        "comment_count": count_rows(conn, "task_comments", "task_id", task_id) if conn else 0,
        "event_count": count_rows(conn, "task_events", "task_id", task_id) if conn else 0,
        "run_count": count_rows(conn, "task_runs", "task_id", task_id) if conn else 0,
        "latest_event_at": latest_event_timestamp(conn, task_id) if conn else None,
        "warnings": warning_for_task(conn, task_id) if conn else None,
    }

def count_rows(conn, table, column, value):
    if not conn or not table_exists(conn, table):
        return 0
    row = conn.execute(
        f"SELECT COUNT(*) AS n FROM {quote_ident(table)} WHERE {quote_ident(column)} = ?",
        (value,),
    ).fetchone()
    return int(row["n"] or 0) if row else 0

def delete_task_rows(conn, task_id, author):
    if not conn or not task_id or not table_exists(conn, "tasks"):
        return False
    try:
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute("SELECT id FROM tasks WHERE id = ?", (task_id,)).fetchone()
        if row is None:
            conn.rollback()
            return False

        if table_exists(conn, "task_links"):
            conn.execute(
                "DELETE FROM task_links WHERE parent_id = ? OR child_id = ?",
                (task_id, task_id),
            )
        if table_exists(conn, "task_comments"):
            conn.execute("DELETE FROM task_comments WHERE task_id = ?", (task_id,))
        if table_exists(conn, "task_events"):
            conn.execute("DELETE FROM task_events WHERE task_id = ?", (task_id,))
        if table_exists(conn, "task_runs"):
            conn.execute("DELETE FROM task_runs WHERE task_id = ?", (task_id,))
        if table_exists(conn, "kanban_notify_subs"):
            conn.execute("DELETE FROM kanban_notify_subs WHERE task_id = ?", (task_id,))

        cur = conn.execute("DELETE FROM tasks WHERE id = ?", (task_id,))
        conn.commit()
        return cur.rowcount == 1
    except Exception:
        try:
            conn.rollback()
        except Exception:
            pass
        raise

def recompute_ready_rows(conn):
    if not conn or not table_exists(conn, "tasks") or not table_exists(conn, "task_links"):
        return 0
    import time
    promoted = 0
    try:
        conn.execute("BEGIN IMMEDIATE")
        todo_rows = conn.execute("SELECT id FROM tasks WHERE status = 'todo'").fetchall()
        for row in todo_rows:
            task_id = row["id"]
            parents = conn.execute(
                "SELECT t.status FROM tasks t "
                "JOIN task_links l ON l.parent_id = t.id "
                "WHERE l.child_id = ?",
                (task_id,),
            ).fetchall()
            if all(parent["status"] == "done" for parent in parents):
                cur = conn.execute(
                    "UPDATE tasks SET status = 'ready' WHERE id = ? AND status = 'todo'",
                    (task_id,),
                )
                if cur.rowcount == 1:
                    if table_exists(conn, "task_events"):
                        conn.execute(
                            "INSERT INTO task_events (task_id, kind, payload, created_at) "
                            "VALUES (?, 'promoted', NULL, ?)",
                            (task_id, int(time.time())),
                        )
                    promoted += 1
        conn.commit()
        return promoted
    except Exception:
        try:
            conn.rollback()
        except Exception:
            pass
        raise

def link_ids(conn, task_id, parents):
    if not conn or not table_exists(conn, "task_links"):
        return []
    column = "parent_id" if parents else "child_id"
    where_column = "child_id" if parents else "parent_id"
    rows = conn.execute(
        f"SELECT {quote_ident(column)} AS id FROM task_links WHERE {quote_ident(where_column)} = ? ORDER BY {quote_ident(column)}",
        (task_id,),
    ).fetchall()
    return [row["id"] for row in rows]

def progress_for_task(conn, task_id):
    if not conn or not table_exists(conn, "task_links") or not table_exists(conn, "tasks"):
        return None
    row = conn.execute(
        "SELECT COUNT(*) AS total, "
        "SUM(CASE WHEN t.status = 'done' THEN 1 ELSE 0 END) AS done "
        "FROM task_links l JOIN tasks t ON t.id = l.child_id "
        "WHERE l.parent_id = ?",
        (task_id,),
    ).fetchone()
    total = int(row["total"] or 0) if row else 0
    if total <= 0:
        return None
    return {
        "done": int(row["done"] or 0),
        "total": total,
    }

def latest_event_timestamp(conn, task_id):
    if not conn or not table_exists(conn, "task_events"):
        return None
    row = conn.execute(
        "SELECT created_at FROM task_events WHERE task_id = ? ORDER BY created_at DESC, id DESC LIMIT 1",
        (task_id,),
    ).fetchone()
    return int_value(row["created_at"]) if row else None

def latest_event_id(conn):
    if not conn or not table_exists(conn, "task_events"):
        return None
    row = conn.execute("SELECT MAX(id) AS id FROM task_events").fetchone()
    return int_value(row["id"]) if row else None

def direct_tasks(conn, include_archived):
    if not table_exists(conn, "tasks"):
        return []
    query = "SELECT * FROM tasks"
    if not include_archived:
        query += " WHERE status != 'archived'"
    query += " ORDER BY priority DESC, created_at ASC"
    return [task_row_to_dict(row, conn) for row in conn.execute(query).fetchall()]

def direct_assignees(conn):
    names = set()
    counts = {}
    if table_exists(conn, "tasks"):
        for row in conn.execute(
            "SELECT assignee, status, COUNT(*) AS n FROM tasks "
            "WHERE status != 'archived' AND assignee IS NOT NULL "
            "GROUP BY assignee, status"
        ).fetchall():
            name = row["assignee"]
            names.add(name)
            counts.setdefault(name, {})[row["status"]] = int(row["n"] or 0)
    profiles_dir = pathlib.Path.home() / ".hermes" / "profiles"
    on_disk = set()
    if profiles_dir.exists():
        for item in sorted(profiles_dir.iterdir()):
            if item.is_dir() and (item / "config.yaml").exists():
                on_disk.add(item.name)
                names.add(item.name)
    return [
        {"name": name, "on_disk": name in on_disk, "counts": counts.get(name, {})}
        for name in sorted(names)
    ]

def direct_stats(conn):
    import time
    by_status = {}
    by_assignee = {}
    oldest_ready = None
    if table_exists(conn, "tasks"):
        for row in conn.execute(
            "SELECT status, COUNT(*) AS n FROM tasks "
            "WHERE status != 'archived' GROUP BY status"
        ).fetchall():
            by_status[row["status"]] = int(row["n"] or 0)
        for row in conn.execute(
            "SELECT assignee, status, COUNT(*) AS n FROM tasks "
            "WHERE status != 'archived' AND assignee IS NOT NULL "
            "GROUP BY assignee, status"
        ).fetchall():
            by_assignee.setdefault(row["assignee"], {})[row["status"]] = int(row["n"] or 0)
        ready = conn.execute(
            "SELECT MIN(created_at) AS created_at FROM tasks WHERE status = 'ready'"
        ).fetchone()
        if ready and ready["created_at"] is not None:
            oldest_ready = max(0, int(time.time()) - int(ready["created_at"]))
    return {
        "by_status": by_status,
        "by_assignee": by_assignee,
        "oldest_ready_age_seconds": oldest_ready,
        "now": int(time.time()),
    }

def direct_tenants(conn):
    if not conn or not table_exists(conn, "tasks"):
        return []
    rows = conn.execute(
        "SELECT DISTINCT tenant FROM tasks "
        "WHERE tenant IS NOT NULL ORDER BY tenant"
    ).fetchall()
    return [row["tenant"] for row in rows]

def load_board(board_slug, include_archived=False):
    board_slug = normalize_board_slug(board_slug) or DEFAULT_BOARD
    db_path = kanban_db_path(board_slug)
    has_cli = find_hermes_binary() is not None
    kb = import_kanban_module(required=False)
    has_module = kb is not None
    base = {
        "database_path": tilde(db_path, pathlib.Path.home()),
        "host_wide": True,
        "is_initialized": db_path.exists(),
        "has_kanban_module": has_module,
        "has_hermes_cli": has_cli,
        "dispatcher": dispatcher_status(),
        "latest_event_id": None,
        "tasks": [],
        "assignees": [],
        "tenants": [],
        "stats": None,
    }
    if not db_path.exists():
        return base

    conn = None
    try:
        if kb is not None:
            conn = connect_for_board(kb, board_slug, writable=False)
            tasks = [
                task_object_to_dict(task, conn)
                for task in kb.list_tasks(conn, include_archived=include_archived)
            ]
            try:
                assignees = kb.known_assignees(conn)
            except Exception:
                assignees = direct_assignees(conn)
            try:
                stats = kb.board_stats(conn)
            except Exception:
                stats = direct_stats(conn)
        else:
            conn = connect_sqlite_readonly(db_path)
            conn.row_factory = sqlite3.Row
            tasks = direct_tasks(conn, include_archived)
            assignees = direct_assignees(conn)
            stats = direct_stats(conn)
        base.update({
            "tasks": tasks,
            "assignees": assignees,
            "tenants": direct_tenants(conn),
            "stats": stats,
            "latest_event_id": latest_event_id(conn),
        })
        return base
    finally:
        if conn is not None:
            conn.close()

def comment_to_dict(comment):
    return {
        "id": int_value(getattr(comment, "id", 0), 0),
        "task_id": getattr(comment, "task_id", ""),
        "author": getattr(comment, "author", ""),
        "body": getattr(comment, "body", ""),
        "created_at": int_value(getattr(comment, "created_at", 0), 0),
    }

def event_to_dict(event):
    return {
        "id": int_value(getattr(event, "id", 0), 0),
        "task_id": getattr(event, "task_id", ""),
        "kind": getattr(event, "kind", ""),
        "payload": getattr(event, "payload", None),
        "created_at": int_value(getattr(event, "created_at", 0), 0),
        "run_id": int_value(getattr(event, "run_id", None)),
    }

def run_to_dict(run):
    return {
        "id": int_value(getattr(run, "id", 0), 0),
        "task_id": getattr(run, "task_id", ""),
        "profile": getattr(run, "profile", None),
        "step_key": getattr(run, "step_key", None),
        "status": getattr(run, "status", ""),
        "outcome": getattr(run, "outcome", None),
        "summary": getattr(run, "summary", None),
        "error": getattr(run, "error", None),
        "metadata": getattr(run, "metadata", None),
        "worker_pid": int_value(getattr(run, "worker_pid", None)),
        "started_at": int_value(getattr(run, "started_at", 0), 0),
        "ended_at": int_value(getattr(run, "ended_at", None)),
    }

def load_task_detail(task_id, board_slug):
    board_slug = normalize_board_slug(board_slug) or DEFAULT_BOARD
    db_path = kanban_db_path(board_slug)
    if not db_path.exists():
        return None
    kb = import_kanban_module(required=False)
    conn = None
    try:
        if kb is not None:
            conn = connect_for_board(kb, board_slug, writable=False)
            task = kb.get_task(conn, task_id)
            if task is None:
                return None
            parent_ids = kb.parent_ids(conn, task_id)
            child_ids = kb.child_ids(conn, task_id)
            comments = [comment_to_dict(item) for item in kb.list_comments(conn, task_id)]
            events = [event_to_dict(item) for item in kb.list_events(conn, task_id)]
            runs = [run_to_dict(item) for item in kb.list_runs(conn, task_id)]
            worker_log = None
            try:
                if supports_keyword(kb.read_worker_log, "board"):
                    worker_log = kb.read_worker_log(task_id, tail_bytes=65536, board=board_slug)
                else:
                    worker_log = kb.read_worker_log(task_id, tail_bytes=65536)
            except Exception:
                worker_log = None
            return {
                "task": task_object_to_dict(task, conn),
                "parent_ids": parent_ids,
                "child_ids": child_ids,
                "comments": comments,
                "events": events,
                "runs": runs,
                "worker_log": worker_log,
                "home_channels": home_channels_for_task(conn, task_id),
            }

        conn = connect_sqlite_readonly(db_path)
        conn.row_factory = sqlite3.Row
        if not table_exists(conn, "tasks"):
            return None
        row = conn.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
        if row is None:
            return None
        comments = []
        if table_exists(conn, "task_comments"):
            comments = [
                {
                    "id": int_value(item["id"], 0),
                    "task_id": item["task_id"],
                    "author": item["author"],
                    "body": item["body"],
                    "created_at": int_value(item["created_at"], 0),
                }
                for item in conn.execute(
                    "SELECT * FROM task_comments WHERE task_id = ? ORDER BY created_at ASC, id ASC",
                    (task_id,),
                ).fetchall()
            ]
        events = []
        if table_exists(conn, "task_events"):
            for item in conn.execute(
                "SELECT * FROM task_events WHERE task_id = ? ORDER BY created_at ASC, id ASC",
                (task_id,),
            ).fetchall():
                events.append({
                    "id": int_value(item["id"], 0),
                    "task_id": item["task_id"],
                    "kind": item["kind"],
                    "payload": parse_json_object(item["payload"]),
                    "created_at": int_value(item["created_at"], 0),
                    "run_id": int_value(item["run_id"]) if "run_id" in item.keys() else None,
                })
        runs = []
        if table_exists(conn, "task_runs"):
            for item in conn.execute(
                "SELECT * FROM task_runs WHERE task_id = ? ORDER BY started_at ASC, id ASC",
                (task_id,),
            ).fetchall():
                runs.append({
                    "id": int_value(item["id"], 0),
                    "task_id": item["task_id"],
                    "profile": item["profile"],
                    "step_key": item["step_key"],
                    "status": item["status"],
                    "outcome": item["outcome"],
                    "summary": item["summary"],
                    "error": item["error"],
                    "metadata": parse_json_object(item["metadata"]),
                    "worker_pid": int_value(item["worker_pid"]),
                    "started_at": int_value(item["started_at"], 0),
                    "ended_at": int_value(item["ended_at"]),
                })
        log_path = worker_log_path(task_id, board_slug)
        worker_log = None
        if log_path.exists():
            try:
                data = log_path.read_bytes()[-65536:]
                worker_log = data.decode("utf-8", errors="replace")
            except Exception:
                worker_log = None
        return {
            "task": task_row_to_dict(row, conn),
            "parent_ids": link_ids(conn, task_id, parents=True),
            "child_ids": link_ids(conn, task_id, parents=False),
            "comments": comments,
            "events": events,
            "runs": runs,
            "worker_log": worker_log,
            "home_channels": home_channels_for_task(conn, task_id),
        }
    finally:
        if conn is not None:
            conn.close()

operation = normalize_text(payload.get("operation")) or "load_board"
if operation == 'list_boards':
    try:
        response = list_boards_response(include_archived=bool(payload.get("include_archived")))
        print(json.dumps({
            "ok": True,
            "boards": response.get("boards", []),
            "current": response.get("current"),
            "supports_board_management": response.get("supports_board_management", False),
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"Unable to load remote Hermes Kanban boards: {exc}")
elif operation == 'load_board':
    try:
        board_slug = requested_board_slug()
        board = load_board(board_slug, include_archived=bool(payload.get("include_archived")))
        print(json.dumps({
            "ok": True,
            "board": board,
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"Unable to load the remote Hermes Kanban board: {exc}")
elif operation == 'task_detail':
    try:
        board_slug = requested_board_slug()
        task_id = normalize_text(payload.get("task_id"))
        if not task_id:
            fail("The Kanban task ID is required.")
        detail = load_task_detail(task_id, board_slug)
        if detail is None:
            fail(f"No such Kanban task: {task_id}")
        print(json.dumps({
            "ok": True,
            "detail": detail,
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"Unable to load the remote Kanban task: {exc}")
elif operation == 'create_board':
    try:
        kb = import_kanban_module(required=True)
        require_board_api(kb, "create Kanban boards")
        if not hasattr(kb, "create_board"):
            fail("This Hermes Agent build does not support creating Kanban boards. Run `hermes update` on the host.")
        slug = normalize_board_slug(payload.get("slug"))
        if not slug:
            fail("Board slug is required.")
        meta = kb.create_board(
            slug,
            name=normalize_text(payload.get("name")),
            description=normalize_text(payload.get("description")),
            icon=normalize_text(payload.get("icon")),
            color=normalize_text(payload.get("color")),
        )
        if bool(payload.get("switch_after_create")):
            try_set_current_board(kb, meta.get("slug") or slug)
        board = hydrate_board_metadata(meta, kb)
        print(json.dumps({
            "ok": True,
            "board": board,
            "current": current_board_slug(kb),
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"Unable to create the remote Hermes Kanban board: {exc}")
elif operation == 'archive_board':
    try:
        kb = import_kanban_module(required=True)
        require_board_api(kb, "archive Kanban boards")
        if not hasattr(kb, "remove_board"):
            fail("This Hermes Agent build does not support archiving Kanban boards. Run `hermes update` on the host.")
        board_slug = requested_board_slug()
        if board_slug == DEFAULT_BOARD:
            fail("The default Kanban board cannot be archived.")
        if not board_exists(board_slug, kb):
            fail(f"No such Kanban board: {board_slug}")
        result = kb.remove_board(board_slug, archive=True)
        print(json.dumps({
            "ok": True,
            "result": result,
            "current": current_board_slug(kb),
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"Unable to archive the remote Hermes Kanban board: {exc}")
elif operation == 'home_subscription':
    try:
        board_slug = requested_board_slug()
        task_id = normalize_text(payload.get("task_id"))
        platform = normalize_text(payload.get("platform"))
        if not task_id:
            fail("The Kanban task ID is required.")
        if not platform:
            fail("The gateway platform is required.")
        home = home_channel_for_platform(platform)
        if home is None:
            fail(f"No home channel configured for platform {platform!r}.")
        db_path = kanban_db_path(board_slug)
        if not db_path.exists():
            fail(f"No such Kanban task: {task_id}")
        kb = import_kanban_module(required=False)
        conn = connect_for_board(kb, board_slug, writable=True)
        try:
            if get_task_any(kb, conn, task_id) is None:
                fail(f"No such Kanban task: {task_id}")
            set_home_subscription(
                kb,
                conn,
                task_id,
                home,
                subscribed=bool(payload.get("subscribed")),
            )
        finally:
            conn.close()
        detail = load_task_detail(task_id, board_slug)
        print(json.dumps({
            "ok": True,
            "task_id": task_id,
            "detail": detail,
            "message": "Home channel subscription updated.",
        }, ensure_ascii=False))
    except Exception as exc:
        fail(f"Unable to update Kanban home-channel subscription: {exc}")
elif operation == 'mutate':
    def mutation_result(message=None, task_id=None, dispatch=None):
        board_slug = requested_board_slug()
        detail = load_task_detail(task_id, board_slug) if task_id else None
        print(json.dumps({
            "ok": True,
            "message": message,
            "task_id": task_id,
            "detail": detail,
            "dispatch": dispatch,
        }, ensure_ascii=False))

    def normalized_payload_list(name):
        raw = payload.get(name) or []
        if isinstance(raw, str):
            raw = re.split(r"[\s,]+", raw)
        result = []
        seen = set()
        for item in raw:
            value = normalize_text(item)
            if not value or value in seen:
                continue
            seen.add(value)
            result.append(value)
        return result

    def normalized_skill_list():
        result = []
        seen = set()
        for item in payload.get("skills") or []:
            value = normalize_text(item)
            if not value or value in seen:
                continue
            if "," in value:
                fail(f"Skill names must be comma-separated without embedded commas: {value!r}")
            seen.add(value)
            result.append(value)
        return result

    def normalized_metadata_object():
        raw = payload.get("metadata")
        if raw is None:
            raw = payload.get("metadata_json")
        if raw is None:
            return None
        if isinstance(raw, dict):
            return raw
        if not isinstance(raw, str):
            fail("Recovery metadata must be a JSON object.")
        text = raw.strip()
        if not text:
            return None
        try:
            parsed = json.loads(text)
        except Exception as exc:
            fail(f"Recovery metadata is not valid JSON: {exc}")
        if not isinstance(parsed, dict):
            fail("Recovery metadata must be a JSON object.")
        return parsed

    @contextlib.contextmanager
    def write_txn_for(kb, conn):
        if kb is not None and hasattr(kb, "write_txn"):
            with kb.write_txn(conn):
                yield
            return
        try:
            conn.execute("BEGIN IMMEDIATE")
            yield
            conn.commit()
        except Exception:
            try:
                conn.rollback()
            except Exception:
                pass
            raise

    def append_event(kb, conn, task_id, kind, event_payload=None):
        if kb is not None and hasattr(kb, "_append_event"):
            kb._append_event(conn, task_id, kind, event_payload)
            return
        if not table_exists(conn, "task_events"):
            return
        conn.execute(
            "INSERT INTO task_events (task_id, kind, payload, created_at) VALUES (?, ?, ?, ?)",
            (
                task_id,
                kind,
                json.dumps(event_payload, ensure_ascii=False) if event_payload is not None else None,
                int(time.time()),
            ),
        )

    def task_exists(conn, task_id):
        return bool(conn.execute("SELECT 1 FROM tasks WHERE id = ?", (task_id,)).fetchone())

    def update_task_fields(kb, conn, task_id):
        if not table_exists(conn, "tasks"):
            fail("The Kanban tasks table is missing.")
        body = normalize_text(payload.get("body"))
        tenant = normalize_text(payload.get("tenant"))
        priority = int(payload.get("priority") or 0)
        skills = normalized_skill_list()
        skills_json = json.dumps(skills, ensure_ascii=False) if skills else None

        with write_txn_for(kb, conn):
            row = conn.execute(
                "SELECT body, tenant, priority, skills FROM tasks WHERE id = ?",
                (task_id,),
            ).fetchone()
            if row is None:
                fail(f"No such Kanban task: {task_id}")

            changed = []
            if row["body"] != body:
                changed.append("body")
            if row["tenant"] != tenant:
                changed.append("tenant")
            if int_value(row["priority"], 0) != priority:
                changed.append("priority")
            if parse_json_list(row["skills"]) != skills:
                changed.append("skills")

            if not changed:
                return False

            conn.execute(
                "UPDATE tasks SET body = ?, tenant = ?, priority = ?, skills = ? WHERE id = ?",
                (body, tenant, priority, skills_json, task_id),
            )
            append_event(kb, conn, task_id, "updated", {"fields": changed})
        return True

    def link_task(kb, conn, parent_id, child_id):
        if kb is not None and hasattr(kb, "link_tasks"):
            kb.link_tasks(conn, parent_id, child_id)
            return
        fail("This Hermes Agent build does not support Kanban dependency links. Run `hermes update` on the host.")

    def unlink_task(kb, conn, parent_id, child_id):
        if kb is not None and hasattr(kb, "unlink_tasks"):
            return bool(kb.unlink_tasks(conn, parent_id, child_id))
        fail("This Hermes Agent build does not support Kanban dependency links. Run `hermes update` on the host.")

    def recompute_ready_any(kb, conn):
        if kb is not None and hasattr(kb, "recompute_ready"):
            return int(kb.recompute_ready(conn) or 0)
        return recompute_ready_rows(conn)

    def set_task_links(kb, conn, task_id, desired_ids, parents):
        if not task_exists(conn, task_id):
            fail(f"No such Kanban task: {task_id}")
        if task_id in desired_ids:
            fail("A Kanban task cannot depend on itself.")

        current = link_ids(conn, task_id, parents=parents)
        current_set = set(current)
        desired_set = set(desired_ids)
        removals = [item for item in current if item not in desired_set]
        additions = [item for item in desired_ids if item not in current_set]

        for linked_id in removals:
            if parents:
                unlink_task(kb, conn, linked_id, task_id)
            else:
                unlink_task(kb, conn, task_id, linked_id)

        for linked_id in additions:
            if parents:
                link_task(kb, conn, linked_id, task_id)
            else:
                link_task(kb, conn, task_id, linked_id)

        promoted = recompute_ready_any(kb, conn)
        return len(removals) + len(additions), promoted

    def perform_with_module(action, task_id, author):
        kb = import_kanban_module(required=True)
        board_slug = requested_board_slug()
        db_path = kanban_db_path(board_slug)
        if action == "delete" and not db_path.exists():
            fail(f"No such Kanban task: {task_id}")
        os.environ["HERMES_HOME"] = str(kanban_home_path())
        with connect_for_board(kb, board_slug, writable=True) as conn:
            if action == "create":
                title = normalize_text(payload.get("title"))
                if not title:
                    fail("Task title is required.")
                parent_ids = normalized_payload_list("parent_ids")
                if parent_ids and not supports_keyword(kb.create_task, "parents"):
                    fail("This Hermes Agent build does not support Kanban parent links at task creation. Run `hermes update` on the host.")
                kwargs = {
                    "title": title,
                    "body": normalize_text(payload.get("body")),
                    "assignee": normalize_text(payload.get("assignee")),
                    "created_by": author,
                    "tenant": normalize_text(payload.get("tenant")),
                    "priority": int(payload.get("priority") or 0),
                    "triage": bool(payload.get("triage")),
                    "skills": payload.get("skills") or None,
                }
                if supports_keyword(kb.create_task, "parents"):
                    kwargs["parents"] = parent_ids
                created_id = kb.create_task(conn, **kwargs)
                return ("Kanban task created.", created_id, None)

            if not task_id and action != "dispatch":
                fail("The Kanban task ID is required.")

            if action == "comment":
                text = normalize_text(payload.get("text"))
                if not text:
                    fail("Comment text is required.")
                kb.add_comment(conn, task_id, author, text)
                return ("Comment added.", task_id, None)

            if action == "update_fields":
                update_task_fields(kb, conn, task_id)
                return ("Task details updated.", task_id, None)

            if action == "set_parents":
                changed, promoted = set_task_links(kb, conn, task_id, normalized_payload_list("parent_ids"), parents=True)
                return (f"Task parents updated. {changed} link changes, {promoted} promoted.", task_id, None)

            if action == "set_children":
                changed, promoted = set_task_links(kb, conn, task_id, normalized_payload_list("child_ids"), parents=False)
                return (f"Task children updated. {changed} link changes, {promoted} promoted.", task_id, None)

            if action == "assign":
                assignee = normalize_text(payload.get("assignee"))
                if not kb.assign_task(conn, task_id, assignee):
                    fail(f"No such Kanban task: {task_id}")
                return ("Task assigned.", task_id, None)

            if action == "reclaim":
                if not hasattr(kb, "reclaim_task"):
                    fail("This Hermes Agent build does not support Kanban recovery reclaim. Run `hermes update` on the host.")
                if not kb.reclaim_task(conn, task_id, reason=normalize_text(payload.get("text"))):
                    fail(f"Cannot reclaim Kanban task: {task_id}")
                return ("Task reclaimed.", task_id, None)

            if action == "reassign":
                if not hasattr(kb, "reassign_task"):
                    fail("This Hermes Agent build does not support Kanban recovery reassign. Run `hermes update` on the host.")
                assignee = normalize_text(payload.get("assignee"))
                if not kb.reassign_task(
                    conn,
                    task_id,
                    assignee,
                    reclaim_first=bool(payload.get("reclaim_first")),
                    reason=normalize_text(payload.get("text")),
                ):
                    fail(f"Cannot reassign Kanban task: {task_id}")
                return ("Task reassigned.", task_id, None)

            if action == "block":
                reason = normalize_text(payload.get("text"))
                if reason:
                    kb.add_comment(conn, task_id, author, f"BLOCKED: {reason}")
                if not kb.block_task(conn, task_id, reason=reason):
                    fail(f"Cannot block Kanban task: {task_id}")
                return ("Task blocked.", task_id, None)

            if action == "unblock":
                if not kb.unblock_task(conn, task_id):
                    fail(f"Cannot unblock Kanban task: {task_id}")
                return ("Task unblocked.", task_id, None)

            if action == "complete":
                result = normalize_text(payload.get("result"))
                if not kb.complete_task(conn, task_id, result=result, summary=result):
                    fail(f"Cannot complete Kanban task: {task_id}")
                return ("Task completed.", task_id, None)

            if action == "edit_result":
                if not hasattr(kb, "edit_completed_task_result"):
                    fail("This Hermes Agent build does not support editing completed Kanban task results. Run `hermes update` on the host.")
                result = normalize_text(payload.get("result"))
                if result is None:
                    fail("A recovery result is required.")
                if not kb.edit_completed_task_result(
                    conn,
                    task_id,
                    result=result,
                    summary=normalize_text(payload.get("summary")),
                    metadata=normalized_metadata_object(),
                ):
                    fail(f"Cannot edit completed Kanban task: {task_id}")
                return ("Task result edited.", task_id, None)

            if action == "archive":
                if not kb.archive_task(conn, task_id):
                    fail(f"Cannot archive Kanban task: {task_id}")
                return ("Task archived.", task_id, None)

            if action == "delete":
                if not delete_task_rows(conn, task_id, author):
                    fail(f"No such Kanban task: {task_id}")
                try:
                    kb.recompute_ready(conn)
                except Exception:
                    pass
                return ("Task deleted.", None, None)

            if action == "dispatch":
                if supports_keyword(kb.dispatch_once, "board"):
                    res = kb.dispatch_once(
                        conn,
                        max_spawn=int(payload.get("max_spawn") or 8),
                        board=board_slug,
                    )
                else:
                    if board_slug != DEFAULT_BOARD:
                        fail("This Hermes Agent build does not support multiple Kanban boards. Run `hermes update` on the host.")
                    res = kb.dispatch_once(
                        conn,
                        max_spawn=int(payload.get("max_spawn") or 8),
                    )
                dispatch = {
                    "reclaimed": int(getattr(res, "reclaimed", 0) or 0),
                    "crashed": list(getattr(res, "crashed", []) or []),
                    "timed_out": list(getattr(res, "timed_out", []) or []),
                    "auto_blocked": list(getattr(res, "auto_blocked", []) or []),
                    "promoted": int(getattr(res, "promoted", 0) or 0),
                    "spawned": [
                        {"task_id": tid, "assignee": who, "workspace": ws}
                        for (tid, who, ws) in list(getattr(res, "spawned", []) or [])
                    ],
                    "skipped_unassigned": list(getattr(res, "skipped_unassigned", []) or []),
                }
                return ("Dispatcher nudged.", None, dispatch)

        fail(f"Unsupported Kanban action: {action}")

    def perform_with_cli(action, task_id, author):
        board_slug = requested_board_slug()
        if action == "create":
            title = normalize_text(payload.get("title"))
            if not title:
                fail("Task title is required.")
            args = kanban_cli_args(board_slug, ["create", "--json", "--created-by", author])
            body = normalize_text(payload.get("body"))
            if body:
                args.extend(["--body", body])
            assignee = normalize_text(payload.get("assignee"))
            if assignee:
                args.extend(["--assignee", assignee])
            tenant = normalize_text(payload.get("tenant"))
            if tenant:
                args.extend(["--tenant", tenant])
            priority = int(payload.get("priority") or 0)
            if priority:
                args.extend(["--priority", str(priority)])
            if bool(payload.get("triage")):
                args.append("--triage")
            for skill in payload.get("skills") or []:
                skill_text = normalize_text(skill)
                if skill_text:
                    args.extend(["--skill", skill_text])
            for parent_id in normalized_payload_list("parent_ids"):
                args.extend(["--parent", parent_id])
            args.append(title)
            data = run_hermes_cli(args, expect_json=True)
            return ("Kanban task created.", data.get("id"), None)

        if not task_id and action != "dispatch":
            fail("The Kanban task ID is required.")

        if action == "comment":
            text = normalize_text(payload.get("text"))
            if not text:
                fail("Comment text is required.")
            run_hermes_cli(kanban_cli_args(board_slug, ["comment", "--author", author, task_id, text]))
            return ("Comment added.", task_id, None)

        if action == "update_fields":
            db_path = kanban_db_path(board_slug)
            if not db_path.exists():
                fail(f"No such Kanban task: {task_id}")
            conn = sqlite3.connect(db_path)
            conn.row_factory = sqlite3.Row
            try:
                update_task_fields(None, conn, task_id)
            finally:
                conn.close()
            return ("Task details updated.", task_id, None)

        if action in ("set_parents", "set_children"):
            db_path = kanban_db_path(board_slug)
            if not db_path.exists():
                fail(f"No such Kanban task: {task_id}")
            conn = sqlite3.connect(db_path)
            conn.row_factory = sqlite3.Row
            try:
                if not task_exists(conn, task_id):
                    fail(f"No such Kanban task: {task_id}")
                parents = action == "set_parents"
                key = "parent_ids" if parents else "child_ids"
                desired_ids = normalized_payload_list(key)
                if task_id in desired_ids:
                    fail("A Kanban task cannot depend on itself.")
                current = link_ids(conn, task_id, parents=parents)
            finally:
                conn.close()

            current_set = set(current)
            desired_set = set(desired_ids)
            for linked_id in [item for item in current if item not in desired_set]:
                if parents:
                    run_hermes_cli(kanban_cli_args(board_slug, ["unlink", linked_id, task_id]))
                else:
                    run_hermes_cli(kanban_cli_args(board_slug, ["unlink", task_id, linked_id]))
            for linked_id in [item for item in desired_ids if item not in current_set]:
                if parents:
                    run_hermes_cli(kanban_cli_args(board_slug, ["link", linked_id, task_id]))
                else:
                    run_hermes_cli(kanban_cli_args(board_slug, ["link", task_id, linked_id]))
            conn = sqlite3.connect(db_path)
            conn.row_factory = sqlite3.Row
            try:
                recompute_ready_rows(conn)
            finally:
                conn.close()
            return ("Task dependencies updated.", task_id, None)

        if action == "assign":
            assignee = normalize_text(payload.get("assignee")) or "none"
            run_hermes_cli(kanban_cli_args(board_slug, ["assign", task_id, assignee]))
            return ("Task assigned.", task_id, None)

        if action == "reclaim":
            args = kanban_cli_args(board_slug, ["reclaim", task_id])
            reason = normalize_text(payload.get("text"))
            if reason:
                args.extend(["--reason", reason])
            run_hermes_cli(args)
            return ("Task reclaimed.", task_id, None)

        if action == "reassign":
            assignee = normalize_text(payload.get("assignee")) or "none"
            args = kanban_cli_args(board_slug, ["reassign", task_id, assignee])
            if bool(payload.get("reclaim_first")):
                args.append("--reclaim")
            reason = normalize_text(payload.get("text"))
            if reason:
                args.extend(["--reason", reason])
            run_hermes_cli(args)
            return ("Task reassigned.", task_id, None)

        if action == "block":
            reason = normalize_text(payload.get("text"))
            args = kanban_cli_args(board_slug, ["block", task_id])
            if reason:
                args.append(reason)
            run_hermes_cli(args)
            return ("Task blocked.", task_id, None)

        if action == "unblock":
            run_hermes_cli(kanban_cli_args(board_slug, ["unblock", task_id]))
            return ("Task unblocked.", task_id, None)

        if action == "complete":
            args = kanban_cli_args(board_slug, ["complete", task_id])
            result = normalize_text(payload.get("result"))
            if result:
                args.extend(["--result", result])
            run_hermes_cli(args)
            return ("Task completed.", task_id, None)

        if action == "edit_result":
            result = normalize_text(payload.get("result"))
            if result is None:
                fail("A recovery result is required.")
            args = kanban_cli_args(board_slug, ["edit", task_id, "--result", result])
            summary = normalize_text(payload.get("summary"))
            if summary:
                args.extend(["--summary", summary])
            metadata = normalized_metadata_object()
            if metadata is not None:
                args.extend(["--metadata", json.dumps(metadata, ensure_ascii=False)])
            run_hermes_cli(args)
            return ("Task result edited.", task_id, None)

        if action == "archive":
            run_hermes_cli(kanban_cli_args(board_slug, ["archive", task_id]))
            return ("Task archived.", task_id, None)

        if action == "delete":
            db_path = kanban_db_path(board_slug)
            if not db_path.exists():
                fail(f"No such Kanban task: {task_id}")
            conn = sqlite3.connect(db_path)
            conn.row_factory = sqlite3.Row
            try:
                if not delete_task_rows(conn, task_id, author):
                    fail(f"No such Kanban task: {task_id}")
                recompute_ready_rows(conn)
            finally:
                conn.close()
            return ("Task deleted.", None, None)

        if action == "dispatch":
            data = run_hermes_cli(
                kanban_cli_args(board_slug, ["dispatch", "--max", str(int(payload.get("max_spawn") or 8)), "--json"]),
                expect_json=True,
            )
            return ("Dispatcher nudged.", None, data)

        fail(f"Unsupported Kanban action: {action}")

    try:
        action = normalize_text(payload.get("action"))
        if not action:
            fail("The Kanban action is required.")
        task_id = normalize_text(payload.get("task_id"))
        author = normalize_text(payload.get("author")) or "desktop"

        try:
            message, affected_task_id, dispatch = perform_with_module(action, task_id, author)
        except ImportError:
            message, affected_task_id, dispatch = perform_with_cli(action, task_id, author)

        mutation_result(message=message, task_id=affected_task_id, dispatch=dispatch)
    except Exception as exc:
        fail(f"Unable to update the remote Kanban board: {exc}")
else:
    fail(f"Unsupported Kanban operation: {operation}")
