import platform
import sqlite3

def discover_session_store(hermes_home, home):
    for candidate in iter_session_store_candidates(hermes_home, home):
        try:
            conn = sqlite3.connect(f"file:{candidate}?mode=ro", uri=True)
            tables = [r[0] for r in conn.execute(
                "SELECT name FROM sqlite_master WHERE type='table'").fetchall()]
            st = choose_table(tables, "sessions")
            mt = choose_table(tables, "messages")
            conn.close()
            if st and mt:
                return {"kind": "sqlite", "path": tilde(candidate, home),
                        "session_table": st, "message_table": mt}
        except Exception:
            continue
    return None

def discover_profiles(home):
    profiles = []
    seen_names = set()

    default_home = home / ".hermes"
    profiles.append({
        "name": "default",
        "path": tilde(default_home, home),
        "is_default": True,
        "exists": default_home.is_dir(),
    })
    seen_names.add("default")

    profiles_root = home / ".hermes" / "profiles"
    if profiles_root.is_dir():
        try:
            for child in sorted(profiles_root.iterdir(), key=lambda p: p.name.lower()):
                if not child.is_dir():
                    continue
                name = child.name
                if name in seen_names:
                    continue
                seen_names.add(name)
                profiles.append({
                    "name": name,
                    "path": tilde(child, home),
                    "is_default": False,
                    "exists": True,
                })
        except Exception:
            pass

    return profiles

try:
    home = pathlib.Path.home()
    hermes_home = resolved_hermes_home()
    user_path = hermes_home / "memories" / "USER.md"
    memory_path = hermes_home / "memories" / "MEMORY.md"
    soul_path = hermes_home / "SOUL.md"
    sessions_dir = hermes_home / "sessions"
    hermes_binary = find_hermes_binary()

    result = {
        "ok": True,
        "home": str(home),
        "hermes_root": str(hermes_home),
        "hermes_home": tilde(hermes_home, home),
        "profile_name": payload.get("profile_name") or "default",
        "python_version": platform.python_version(),
        "hermes_cli_available": hermes_binary is not None,
        "hermes_cli_path": tilde(pathlib.Path(hermes_binary), home) if hermes_binary else None,
        "session_source": None,
        "session_store": None,
        "tracked_files": [],
        "available_profiles": discover_profiles(home),
    }

    if hermes_home.is_dir():
        store = discover_session_store(hermes_home, home)
        if store:
            result["session_store"] = store["path"]
            result["session_source"] = "sqlite"
        elif sessions_dir.is_dir() and list(sessions_dir.glob("*.jsonl")):
            result["session_source"] = "jsonl"
            result["session_store"] = tilde(sessions_dir, home)

    for p in [user_path, memory_path, soul_path]:
        info = {"path": str(p), "exists": p.is_file(), "size": None}
        if info["exists"]:
            try:
                info["size"] = p.stat().st_size
            except OSError:
                pass
        result["tracked_files"].append(info)

    print(json.dumps(result, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to discover the remote Hermes workspace: {exc}")
