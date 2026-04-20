import hashlib
import tempfile

temp_name = None
directory_fd = None
content_bytes = payload["content"].encode("utf-8")
expected_hash = payload.get("expected_content_hash")

try:
    target = pathlib.Path(os.path.expanduser(payload["path"]))
    target.parent.mkdir(parents=True, exist_ok=True)

    if expected_hash is not None:
        if not target.exists():
            fail(f"{payload['path']} was removed after it was loaded. Reload from Remote before saving.")
        if not target.is_file():
            fail(f"{payload['path']} is not a regular file anymore. Reload from Remote before saving.")

        current_bytes = target.read_bytes()
        current_hash = hashlib.sha256(current_bytes).hexdigest()
        if current_hash != expected_hash:
            fail(f"{payload['path']} changed on the active host after it was loaded. Reload from Remote before saving.")

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
        os.chmod(temp_name, target.stat().st_mode)

    os.replace(temp_name, target)

    try:
        directory_fd = os.open(str(target.parent), os.O_RDONLY)
        os.fsync(directory_fd)
    except (OSError, AttributeError):
        pass

    print(json.dumps({
        "ok": True,
        "path": payload["path"],
        "content_hash": hashlib.sha256(content_bytes).hexdigest(),
    }, ensure_ascii=False))
except PermissionError:
    fail(f"Permission denied while writing {payload['path']}.")
except Exception as exc:
    fail(f"Unable to write {payload['path']}: {exc}")
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
