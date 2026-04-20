try:
    relative_path = payload.get("relative_path", "")
    normalized = pathlib.PurePosixPath(relative_path)
    if normalized.is_absolute() or ".." in normalized.parts or not normalized.parts:
        fail("The requested skill path is invalid.")

    root = (resolved_hermes_home() / "skills").resolve()
    target = (root / pathlib.Path(*normalized.parts) / "SKILL.md").resolve()

    try:
        target.relative_to(root)
    except ValueError:
        fail("The requested skill path escapes the Hermes skills directory.")

    if not target.exists():
        fail(f"No skill exists at {relative_path}.")
    if not target.is_file():
        fail(f"{relative_path} does not resolve to a readable SKILL.md file.")

    content = target.read_text(encoding="utf-8", errors="replace")
    print(json.dumps({
        "ok": True,
        "markdown_content": content,
    }, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to read the remote Hermes skill detail: {exc}")
