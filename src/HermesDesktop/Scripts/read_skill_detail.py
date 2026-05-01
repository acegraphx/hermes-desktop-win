import hashlib


def normalize_text_list(value):
    if value is None:
        return []
    if isinstance(value, str):
        text = normalize_text(value)
        return [text] if text else []
    if isinstance(value, list):
        out = []
        for item in value:
            text = normalize_text(item)
            if text:
                out.append(text)
        return out
    return []


def fallback_external_dirs(config_text):
    lines = config_text.splitlines()
    in_skills = False
    in_external = False
    base_indent = 0
    out = []

    for raw in lines:
        stripped = raw.strip()
        if not stripped or stripped.startswith("#"):
            continue
        indent = len(raw) - len(raw.lstrip(" "))

        if indent == 0:
            in_skills = stripped.startswith("skills:")
            in_external = False
            continue

        if not in_skills:
            continue

        if in_external:
            if indent <= base_indent:
                in_external = False
            elif stripped.startswith("- "):
                value = stripped[2:].strip().strip("'\"")
                if value:
                    out.append(value)
                continue

        if stripped.startswith("external_dirs:"):
            rest = stripped[len("external_dirs:"):].strip()
            if rest.startswith("[") and rest.endswith("]"):
                inner = rest[1:-1]
                for piece in inner.split(","):
                    value = piece.strip().strip("'\"")
                    if value:
                        out.append(value)
            else:
                in_external = True
                base_indent = indent

    return out


def load_external_dirs(hermes_home):
    config_path = hermes_home / "config.yaml"
    if not config_path.is_file():
        return []
    try:
        text = config_path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return []

    try:
        import yaml
        data = yaml.safe_load(text)
        if isinstance(data, dict):
            skills = data.get("skills")
            if isinstance(skills, dict):
                return normalize_text_list(skills.get("external_dirs"))
    except Exception:
        pass

    return normalize_text_list(fallback_external_dirs(text))


def resolve_source_root(source_id, hermes_home):
    if not source_id or source_id == "local":
        return hermes_home / "skills", False

    home = pathlib.Path.home()
    if source_id.startswith("external_"):
        try:
            index = int(source_id[len("external_"):])
        except ValueError:
            fail(f"Invalid skill source identifier: {source_id}")
            return None, False
        externals = load_external_dirs(hermes_home)
        if index < 0 or index >= len(externals):
            fail(f"Skill source no longer exists: {source_id}")
            return None, False
        candidate = expand_remote_path(externals[index], home)
        if candidate is None:
            fail(f"Skill source path is invalid: {externals[index]}")
            return None, False
        return candidate, True

    fail(f"Unknown skill source: {source_id}")
    return None, False


try:
    relative_path = payload.get("relative_path", "")
    source_id = normalize_text(payload.get("source_id")) or "local"

    normalized = pathlib.PurePosixPath(relative_path)
    if normalized.is_absolute() or ".." in normalized.parts or not normalized.parts:
        fail("The requested skill path is invalid.")

    hermes_home = resolved_hermes_home()
    root_unresolved, is_read_only = resolve_source_root(source_id, hermes_home)
    root = root_unresolved.resolve()
    target = (root / pathlib.Path(*normalized.parts) / "SKILL.md").resolve()

    try:
        target.relative_to(root)
    except ValueError:
        fail("The requested skill path escapes the source directory.")

    if not target.exists():
        fail(f"No skill exists at {relative_path}.")
    if not target.is_file():
        fail(f"{relative_path} does not resolve to a readable SKILL.md file.")

    content_bytes = target.read_bytes()
    content = content_bytes.decode("utf-8", errors="replace")
    content_hash = hashlib.sha256(content_bytes).hexdigest()

    print(json.dumps({
        "ok": True,
        "markdown_content": content,
        "content_hash": content_hash,
        "source_id": source_id,
        "is_read_only": is_read_only,
    }, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to read the remote Hermes skill detail: {exc}")
