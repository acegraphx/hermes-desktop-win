def extract_frontmatter(content):
    lines = content.splitlines()
    if not lines or lines[0].strip() != "---":
        return None
    for i in range(1, len(lines)):
        if lines[i].strip() == "---":
            return "\n".join(lines[1:i])
    return None

def parse_key_value(fm_lines, key):
    for line in fm_lines:
        stripped = line.strip()
        if stripped.startswith(f"{key}:"):
            val = stripped[len(key) + 1:].strip().strip("'\"")
            return val if val else None
    return None

def parse_frontmatter(content, rel_path):
    name = rel_path.parent.name or rel_path.stem
    description = None
    version = None
    category = None
    tags = []

    fm_text = extract_frontmatter(content)
    if fm_text:
        fm_lines = fm_text.splitlines()

        try:
            import yaml
            data = yaml.safe_load(fm_text)
            if isinstance(data, dict):
                name = normalize_text(data.get("name")) or name
                description = normalize_text(data.get("description"))
                version = normalize_text(data.get("version"))
                metadata = data.get("metadata", {})
                if isinstance(metadata, dict):
                    tags = metadata.get("tags", [])
                    if not isinstance(tags, list):
                        tags = []
                return name, description, version, category, tags
        except Exception:
            pass

        name = parse_key_value(fm_lines, "name") or name
        description = parse_key_value(fm_lines, "description")
        version = parse_key_value(fm_lines, "version")

    parts = list(rel_path.parent.parts)
    if len(parts) > 1:
        category = parts[0]

    return name, description, version, category, tags


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
    """Tiny YAML fallback: only handles `skills:` block with `external_dirs:` list."""
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


def resolve_skill_sources(hermes_home):
    home = pathlib.Path.home()
    sources = []
    seen_roots = set()

    def add(source_id, kind, root):
        try:
            resolved = root.resolve()
        except Exception:
            return
        key = str(resolved)
        if key in seen_roots:
            return
        seen_roots.add(key)
        sources.append({
            "id": source_id,
            "kind": kind,
            "root": resolved,
            "root_path": str(resolved),
            "is_read_only": kind != "local",
        })

    add("local", "local", hermes_home / "skills")

    for index, raw in enumerate(load_external_dirs(hermes_home)):
        candidate = expand_remote_path(raw, home)
        if candidate is None:
            continue
        add(f"external_{index}", "external", candidate)

    return sources


def collect_items(source):
    root = source["root"]
    if not root.exists() or not root.is_dir():
        return []

    out = []
    for skill_file in sorted(root.rglob("SKILL.md")):
        if not skill_file.is_file():
            continue
        rel = skill_file.relative_to(root)
        rel_str = str(rel)
        if ".." in rel_str or os.path.isabs(rel_str):
            continue

        try:
            content = skill_file.read_text(encoding="utf-8", errors="replace")
            name, description, version, category, tags = parse_frontmatter(content, rel)
            rel_path = skill_file.parent.relative_to(root).as_posix()

            if not category and "/" in rel_path:
                category = rel_path.rsplit("/", 1)[0]

            out.append({
                "id": f"{source['id']}::{rel_path}",
                "slug": skill_file.parent.name,
                "name": name,
                "description": description,
                "version": version,
                "category": category,
                "relative_path": rel_path,
                "tags": tags,
                "source_id": source["id"],
                "source_kind": source["kind"],
                "source_label": "Local" if source["kind"] == "local" else "External",
                "is_read_only": source["is_read_only"],
                "root_path": source["root_path"],
            })
        except Exception:
            continue

    return out


try:
    hermes_home = resolved_hermes_home()
    sources = resolve_skill_sources(hermes_home)

    items = []
    seen_relative = set()  # (relative_path) — local entries shadow externals at the same path

    for source in sources:
        for item in collect_items(source):
            rel = item["relative_path"]
            if rel in seen_relative:
                continue  # local already provided this skill
            seen_relative.add(rel)
            items.append(item)

    items.sort(key=lambda x: (
        (x.get("category") or "").lower(),
        (x.get("name") or x.get("slug") or "").lower(),
    ))

    print(json.dumps({"ok": True, "items": items}, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to read the remote Hermes skill library: {exc}")
