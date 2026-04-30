import hashlib
import re


def resolve_wiki_root():
    expanded = expand_remote_path(payload.get("wiki_path"))
    if expanded is not None:
        return expanded
    return resolved_hermes_home() / "home" / "wiki"


def parse_frontmatter(content):
    """Returns (data, body). data is dict or None; body excludes frontmatter."""
    if not content.startswith("---"):
        return None, content
    rest = content[3:]
    if rest.startswith("\r\n"):
        rest = rest[2:]
    elif rest.startswith("\n"):
        rest = rest[1:]
    else:
        return None, content
    match = re.search(r"^---\s*$", rest, re.MULTILINE)
    if not match:
        return None, content
    raw = rest[: match.start()]
    body = rest[match.end():]
    if body.startswith("\r\n"):
        body = body[2:]
    elif body.startswith("\n"):
        body = body[1:]

    data = None
    try:
        import yaml  # type: ignore
        loaded = yaml.safe_load(raw)
        if isinstance(loaded, dict):
            data = loaded
    except Exception:
        data = simple_yaml_parse(raw)
    if data is None:
        data = simple_yaml_parse(raw)
    return data, body


def simple_yaml_parse(text):
    """Lightweight key: value / list parser — handles flat shapes only."""
    result = {}
    current_key = None
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        # List item under current key.
        if line.lstrip().startswith("-") and current_key:
            value = line.split("-", 1)[1].strip().strip('"\'')
            if value:
                if not isinstance(result.get(current_key), list):
                    result[current_key] = []
                result[current_key].append(value)
            continue
        if ":" in line:
            k, _, v = line.partition(":")
            k = k.strip()
            v = v.strip()
            if not v:
                current_key = k
                result[k] = []
            elif v.startswith("[") and v.endswith("]"):
                items = [item.strip().strip('"\'') for item in v[1:-1].split(",") if item.strip()]
                result[k] = items
                current_key = None
            else:
                result[k] = v.strip('"\'')
                current_key = None
    return result


def extract_tags(frontmatter):
    if not isinstance(frontmatter, dict):
        return []
    raw = frontmatter.get("tags")
    if raw is None:
        return []
    if isinstance(raw, str):
        return [t.strip().lstrip("#") for t in raw.replace(",", " ").split() if t.strip()]
    if isinstance(raw, list):
        return [str(t).strip().lstrip("#") for t in raw if t is not None and str(t).strip()]
    return []


def stringify_value(v):
    if v is None:
        return None
    if isinstance(v, (str, int, float, bool)):
        return v
    if isinstance(v, list):
        return [stringify_value(x) for x in v]
    return str(v)


def normalize_frontmatter(data):
    if not isinstance(data, dict):
        return None
    return {str(k): stringify_value(v) for k, v in data.items()}


def extract_outgoing_links(body):
    pattern = re.compile(r"\[\[([^\]\|\n]+?)(?:\|[^\]\n]+?)?\]\]")
    seen = []
    for m in pattern.finditer(body):
        target = m.group(1).strip()
        if target and target not in seen:
            seen.append(target)
    return seen


try:
    rel_raw = normalize_text(payload.get("relative_path"))
    if rel_raw is None:
        fail("relative_path is required")
    if rel_raw.startswith("/") or rel_raw.startswith("~"):
        fail("relative_path must be relative to the wiki root")

    root = resolve_wiki_root()
    if not root.exists() or not root.is_dir():
        fail(f"Wiki path does not exist on the active host: {root}")

    root_resolved = root.resolve()
    target = (root / rel_raw).resolve()

    try:
        target.relative_to(root_resolved)
    except ValueError:
        fail(f"Path escapes the wiki root: {rel_raw}")

    if not target.exists():
        fail(f"{rel_raw} does not exist in the wiki.")
    if not target.is_file():
        fail(f"{rel_raw} is not a regular file.")

    raw_content = target.read_bytes()
    content_hash = hashlib.sha256(raw_content).hexdigest()
    content = raw_content.decode("utf-8")

    fm, body = parse_frontmatter(content)
    fm_normalized = normalize_frontmatter(fm)
    tags = extract_tags(fm)
    outgoing = extract_outgoing_links(body)

    print(json.dumps({
        "ok": True,
        "relative_path": rel_raw,
        "content": content,
        "body": body,
        "content_hash": content_hash,
        "frontmatter": fm_normalized,
        "tags": tags,
        "outgoing_links": outgoing,
    }, ensure_ascii=False))
except UnicodeDecodeError:
    fail(f"{payload.get('relative_path')!r} is not valid UTF-8.")
except PermissionError:
    fail(f"Permission denied while reading {payload.get('relative_path')!r}.")
except Exception as exc:
    fail(f"Unable to read {payload.get('relative_path')!r}: {exc}")
