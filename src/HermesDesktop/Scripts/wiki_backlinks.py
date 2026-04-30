import re
import shutil
import subprocess


def resolve_wiki_root():
    expanded = expand_remote_path(payload.get("wiki_path"))
    if expanded is not None:
        return expanded
    return resolved_hermes_home() / "home" / "wiki"


try:
    basename = normalize_text(payload.get("page_basename"))
    if basename is None:
        print(json.dumps({"ok": True, "sources": []}))
        sys.exit(0)

    root = resolve_wiki_root()
    if not root.exists() or not root.is_dir():
        fail(f"Wiki path does not exist: {root}")

    root_str = str(root)
    root_resolved = root.resolve()

    # Wikilink regex matches [[basename]], [[basename|alias]], or [[dir/basename]] ending in basename.
    escaped = re.escape(basename)
    pattern = r"\[\[(?:[^\]\|\n]*?/)?" + escaped + r"(?:\|[^\]\n]+?)?\]\]"

    rg_path = shutil.which("rg")
    if rg_path is not None:
        args = [
            rg_path,
            "-l", "-i",
            "-g", "*.md",
            "--",
            pattern,
            root_str,
        ]
    else:
        args = [
            "grep", "-rli", "-E",
            "--include=*.md",
            "--", pattern, root_str,
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
        fail("Backlink scan timed out.")

    if proc.returncode not in (0, 1):
        fail((proc.stderr or "Backlinks scan failed.").strip())

    sources = []
    self_path = payload.get("self_relative_path")
    for line in (proc.stdout or "").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            file_path = pathlib.Path(line).resolve()
            file_path.relative_to(root_resolved)
            rel = file_path.relative_to(root_resolved).as_posix()
        except (ValueError, OSError):
            continue
        if self_path and rel == self_path:
            continue
        sources.append(rel)

    sources.sort(key=str.lower)
    print(json.dumps({"ok": True, "sources": sources}, ensure_ascii=False))
except Exception as exc:
    fail(f"Backlinks scan failed: {exc}")
