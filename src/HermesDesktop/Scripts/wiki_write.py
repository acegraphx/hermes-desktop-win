import hashlib
import tempfile


def resolve_wiki_root():
    expanded = expand_remote_path(payload.get("wiki_path"))
    if expanded is not None:
        return expanded
    return resolved_hermes_home() / "home" / "wiki"


temp_name = None
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

    expected_hash = payload.get("expected_content_hash")
    content_bytes = (payload.get("content") or "").encode("utf-8")

    target.parent.mkdir(parents=True, exist_ok=True)

    if expected_hash is not None:
        if not target.exists():
            fail(f"{rel_raw} was removed after it was loaded. Reload the page before saving.")
        if not target.is_file():
            fail(f"{rel_raw} is not a regular file anymore. Reload the page before saving.")

        current_bytes = target.read_bytes()
        current_hash = hashlib.sha256(current_bytes).hexdigest()
        if current_hash != expected_hash:
            fail(f"{rel_raw} changed on the active host after it was loaded. Reload before saving.")

    fd, temp_name = tempfile.mkstemp(
        dir=str(target.parent),
        prefix=f".{target.name}.",
        suffix=".tmp",
    )

    with os.fdopen(fd, "wb") as handle:
        handle.write(content_bytes)
        handle.flush()
        os.fsync(handle.fileno())

    if target.exists():
        try:
            os.chmod(temp_name, target.stat().st_mode)
        except Exception:
            pass

    os.replace(temp_name, target)
    temp_name = None

    try:
        dir_fd = os.open(str(target.parent), os.O_RDONLY)
        try:
            os.fsync(dir_fd)
        finally:
            os.close(dir_fd)
    except Exception:
        pass

    print(json.dumps({
        "ok": True,
        "relative_path": rel_raw,
        "content_hash": hashlib.sha256(content_bytes).hexdigest(),
    }, ensure_ascii=False))
except PermissionError:
    fail(f"Permission denied while writing {payload.get('relative_path')!r}.")
except Exception as exc:
    fail(f"Unable to save {payload.get('relative_path')!r}: {exc}")
finally:
    if temp_name is not None:
        try:
            os.unlink(temp_name)
        except Exception:
            pass
