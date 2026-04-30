import re

PEEK_BYTES = 4096


def resolve_wiki_root():
    expanded = expand_remote_path(payload.get("wiki_path"))
    if expanded is not None:
        return expanded
    return resolved_hermes_home() / "home" / "wiki"


def peek_frontmatter(path):
    """Read the head of the file and try to extract title + tags. Returns dict (may be empty)."""
    try:
        with open(path, "rb") as fh:
            head = fh.read(PEEK_BYTES)
        text = head.decode("utf-8", errors="replace")
    except Exception:
        return {}
    if not text.startswith("---"):
        return {}
    rest = text[3:]
    if rest.startswith("\r\n"):
        rest = rest[2:]
    elif rest.startswith("\n"):
        rest = rest[1:]
    else:
        return {}
    m = re.search(r"^---\s*$", rest, re.MULTILINE)
    raw = rest[: m.start()] if m else rest

    title = None
    tags = []
    current_key = None
    for line in raw.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if line.lstrip().startswith("-") and current_key == "tags":
            v = line.split("-", 1)[1].strip().strip('"\'').lstrip("#")
            if v:
                tags.append(v)
            continue
        if ":" in line:
            k, _, v = line.partition(":")
            k = k.strip().lower()
            v = v.strip()
            if k == "title":
                title = v.strip('"\'') or None
                current_key = None
            elif k == "tags":
                if not v:
                    current_key = "tags"
                elif v.startswith("[") and v.endswith("]"):
                    tags = [item.strip().strip('"\'').lstrip("#") for item in v[1:-1].split(",") if item.strip()]
                    current_key = None
                else:
                    tags = [t.strip().lstrip("#") for t in v.replace(",", " ").split() if t.strip()]
                    current_key = None
            else:
                current_key = None
    return {"title": title, "tags": tags}


try:
    root = resolve_wiki_root()
    if not root.exists():
        fail(f"Wiki path does not exist on the active host: {root}")
    if not root.is_dir():
        fail(f"Wiki path is not a directory: {root}")

    root_resolved = root.resolve()

    entries = []
    for path in root.rglob("*.md"):
        if not path.is_file():
            continue
        try:
            rel = path.relative_to(root)
        except ValueError:
            continue
        if any(part.startswith(".") for part in rel.parts):
            continue
        try:
            resolved = path.resolve()
            resolved.relative_to(root_resolved)
        except (ValueError, OSError):
            continue

        try:
            mtime = path.stat().st_mtime
        except OSError:
            mtime = 0.0

        dir_str = rel.parent.as_posix()
        if dir_str == ".":
            dir_str = ""

        peek = peek_frontmatter(path)

        entries.append({
            "relative_path": rel.as_posix(),
            "name": path.name,
            "dir": dir_str,
            "mtime": mtime,
            "title": peek.get("title"),
            "tags": peek.get("tags") or [],
        })

    entries.sort(key=lambda e: e["relative_path"].lower())

    print(json.dumps({
        "ok": True,
        "root": str(root),
        "entries": entries,
    }, ensure_ascii=False))
except PermissionError as exc:
    fail(f"Permission denied while reading wiki: {exc}")
except Exception as exc:
    fail(f"Unable to list wiki files: {exc}")
