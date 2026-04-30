import shutil
import subprocess


def resolve_wiki_root():
    expanded = expand_remote_path(payload.get("wiki_path"))
    if expanded is not None:
        return expanded
    return resolved_hermes_home() / "home" / "wiki"


try:
    query = normalize_text(payload.get("query"))
    if query is None:
        print(json.dumps({"ok": True, "results": []}))
        sys.exit(0)

    max_results = int(payload.get("max_results") or 200)
    if max_results <= 0:
        max_results = 200

    root = resolve_wiki_root()
    if not root.exists() or not root.is_dir():
        fail(f"Wiki path does not exist: {root}")

    root_str = str(root)
    root_resolved = root.resolve()

    rg_path = shutil.which("rg")
    if rg_path is not None:
        args = [
            rg_path,
            "-n", "-i", "--no-heading", "--color=never",
            "-g", "*.md",
            "-F",
            "--max-count", str(max_results),
            "--",
            query,
            root_str,
        ]
    else:
        args = [
            "grep", "-rni", "-F",
            "--include=*.md",
            "--", query, root_str,
        ]

    try:
        proc = subprocess.run(
            args,
            capture_output=True,
            text=True,
            timeout=20,
            errors="replace",
        )
    except FileNotFoundError:
        fail("Neither 'rg' nor 'grep' is available on the active host.")
    except subprocess.TimeoutExpired:
        fail("Search timed out.")

    # rg/grep exit code 1 = no matches; treat as empty, not error.
    if proc.returncode not in (0, 1):
        fail((proc.stderr or "Search failed.").strip())

    raw = proc.stdout or ""
    results = []
    for line in raw.splitlines():
        if len(results) >= max_results:
            break
        # Format: <path>:<line_no>:<content>
        try:
            path_part, line_no_part, content_part = line.split(":", 2)
        except ValueError:
            continue
        try:
            line_no = int(line_no_part)
        except ValueError:
            continue
        try:
            file_path = pathlib.Path(path_part).resolve()
            file_path.relative_to(root_resolved)
            rel = file_path.relative_to(root_resolved).as_posix()
        except (ValueError, OSError):
            continue
        snippet = content_part.strip()
        if len(snippet) > 240:
            snippet = snippet[:240] + "…"
        results.append({
            "relative_path": rel,
            "line_no": line_no,
            "snippet": snippet,
        })

    print(json.dumps({"ok": True, "results": results}, ensure_ascii=False))
except Exception as exc:
    fail(f"Search failed: {exc}")
