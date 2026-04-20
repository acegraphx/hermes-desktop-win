import hashlib

try:
    target = pathlib.Path(os.path.expanduser(payload["path"]))
    if not target.exists():
        fail(f"{payload['path']} does not exist on the active host.")
    if not target.is_file():
        fail(f"{payload['path']} is not a regular file.")

    raw_content = target.read_bytes()
    content_hash = hashlib.sha256(raw_content).hexdigest()
    content = raw_content.decode("utf-8")
    print(json.dumps({
        "ok": True,
        "content": content,
        "content_hash": content_hash,
    }, ensure_ascii=False))
except UnicodeDecodeError:
    fail(f"{payload['path']} is not valid UTF-8.")
except PermissionError:
    fail(f"Permission denied while reading {payload['path']}.")
except Exception as exc:
    fail(f"Unable to read {payload['path']}: {exc}")
