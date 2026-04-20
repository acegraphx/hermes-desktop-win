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

try:
    root = resolved_hermes_home() / "skills"
    if not root.exists():
        print(json.dumps({"ok": True, "items": []}, ensure_ascii=False))
        sys.exit(0)

    items = []
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

            items.append({
                "id": rel_path,
                "slug": skill_file.parent.name,
                "name": name,
                "description": description,
                "version": version,
                "category": category,
                "relative_path": rel_path,
                "tags": tags,
            })
        except Exception:
            continue

    items.sort(key=lambda x: (
        (x.get("category") or "").lower(),
        (x.get("name") or x.get("slug") or "").lower(),
    ))

    print(json.dumps({"ok": True, "items": items}, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to read the remote Hermes skill library: {exc}")
