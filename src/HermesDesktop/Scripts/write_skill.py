import hashlib
import re
import tempfile

# Skill writes always land in the local skills root (hermes_home/skills).
# External sources are read-only — the upstream macOS app uses the same rule.

_SLUG_PART = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")

temp_name = None
directory_fd = None
content_str = payload.get("markdown_content", "")
if not isinstance(content_str, str) or not content_str.strip():
    fail("SKILL.md content is required.")
content_bytes = content_str.encode("utf-8")
expected_hash = normalize_text(payload.get("expected_content_hash"))
relative_path = normalize_text(payload.get("relative_path"))
requested_source = normalize_text(payload.get("source_id")) or "local"
create_references = bool(payload.get("create_references_folder", False))

if requested_source != "local":
    fail("External skill directories are read-only. Create a local skill at the same relative path to override.")

if not relative_path:
    fail("Relative path is required.")

normalized = pathlib.PurePosixPath(relative_path)
if normalized.is_absolute() or ".." in normalized.parts or not normalized.parts:
    fail("The requested skill path is invalid.")

for part in normalized.parts:
    if not _SLUG_PART.match(part):
        fail(f"Skill path segment '{part}' is not allowed. Use letters, numbers, underscores, dots, or hyphens.")

try:
    hermes_home = resolved_hermes_home()
    root = (hermes_home / "skills").resolve()
    root.mkdir(parents=True, exist_ok=True)

    skill_dir = (root / pathlib.Path(*normalized.parts)).resolve()
    try:
        skill_dir.relative_to(root)
    except ValueError:
        fail("The requested skill path escapes the local skills directory.")

    target = skill_dir / "SKILL.md"

    if expected_hash is not None:
        if not target.exists():
            fail(f"{relative_path}/SKILL.md was removed after it was loaded. Reload from Remote before saving.")
        if not target.is_file():
            fail(f"{relative_path}/SKILL.md is not a regular file anymore. Reload from Remote before saving.")
        current_bytes = target.read_bytes()
        current_hash = hashlib.sha256(current_bytes).hexdigest()
        if current_hash != expected_hash:
            fail(f"{relative_path}/SKILL.md changed on the active host after it was loaded. Reload from Remote before saving.")
    else:
        # New-skill mode: refuse to clobber an existing SKILL.md.
        if target.exists():
            fail(f"A skill already exists at {relative_path}. Pick a different path or open the existing one to edit.")

    skill_dir.mkdir(parents=True, exist_ok=True)
    if create_references:
        try:
            (skill_dir / "references").mkdir(parents=True, exist_ok=True)
        except Exception:
            pass

    fd, temp_name = tempfile.mkstemp(
        dir=str(skill_dir),
        prefix=".SKILL.md.",
        suffix=".tmp",
    )

    with os.fdopen(fd, "wb") as handle:
        handle.write(content_bytes)
        handle.flush()
        os.fsync(handle.fileno())

    if target.exists():
        os.chmod(temp_name, target.stat().st_mode)

    os.replace(temp_name, target)

    try:
        directory_fd = os.open(str(skill_dir), os.O_RDONLY)
        os.fsync(directory_fd)
    except (OSError, AttributeError):
        pass

    new_hash = hashlib.sha256(content_bytes).hexdigest()
    print(json.dumps({
        "ok": True,
        "relative_path": relative_path,
        "content_hash": new_hash,
        "source_id": "local",
    }, ensure_ascii=False))
except SystemExit:
    raise
except PermissionError:
    fail(f"Permission denied while writing {relative_path}/SKILL.md.")
except Exception as exc:
    fail(f"Unable to write {relative_path}/SKILL.md: {exc}")
finally:
    if directory_fd is not None:
        try:
            os.close(directory_fd)
        except Exception:
            pass
    if temp_name and os.path.exists(temp_name):
        try:
            os.unlink(temp_name)
        except Exception:
            pass
