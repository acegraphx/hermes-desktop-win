import fcntl
import json
import os
import pathlib
import re
import secrets
import tempfile
from datetime import datetime, timezone

def normalize_list(value):
    if value is None:
        return []
    if isinstance(value, (list, tuple, set)):
        items = []
        for item in value:
            normalized = normalize_text(item)
            if normalized is not None:
                items.append(normalized)
        return items
    normalized = normalize_text(value)
    return [normalized] if normalized is not None else []

def load_container(path):
    if not path.exists():
        return [], "list", None, None

    raw = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(raw, list):
        return raw, "list", None, None

    if isinstance(raw, dict):
        for key in ("jobs", "items", "cron_jobs"):
            jobs = raw.get(key)
            if isinstance(jobs, list):
                return jobs, "dict", key, raw
        fail(f"Unsupported cron metadata wrapper in {path}.")

    fail(f"Unsupported cron metadata format in {path}.")

def save_container(path, jobs, container_kind, container_key, container_payload):
    if container_kind == "list":
        payload_to_write = jobs
    else:
        payload_to_write = dict(container_payload) if isinstance(container_payload, dict) else {}
        # Preserve any scheduler metadata Hermes keeps next to the jobs list.
        payload_to_write[container_key or "jobs"] = jobs

    path.parent.mkdir(parents=True, exist_ok=True)
    content_bytes = (
        json.dumps(payload_to_write, ensure_ascii=False, indent=2) + "\n"
    ).encode("utf-8")
    temp_name = None
    directory_fd = None

    try:
        fd, temp_name = tempfile.mkstemp(
            dir=str(path.parent),
            prefix=f".{path.name}.",
            suffix=".tmp",
        )
        with os.fdopen(fd, "wb") as handle:
            handle.write(content_bytes)
            handle.flush()
            os.fsync(handle.fileno())

        if path.exists():
            os.chmod(temp_name, path.stat().st_mode)

        os.replace(temp_name, path)
        directory_fd = os.open(path.parent, os.O_RDONLY)
        os.fsync(directory_fd)
    finally:
        if directory_fd is not None:
            os.close(directory_fd)
        if temp_name and os.path.exists(temp_name):
            os.unlink(temp_name)

def with_jobs_lock(path, callback):
    path.parent.mkdir(parents=True, exist_ok=True)
    lock_path = path.with_name(path.name + ".lock")
    lock_fd = os.open(str(lock_path), os.O_CREAT | os.O_RDWR, 0o600)
    try:
        os.chmod(str(lock_path), 0o600)
    except OSError:
        pass

    try:
        fcntl.flock(lock_fd, fcntl.LOCK_EX)
        return callback()
    finally:
        try:
            fcntl.flock(lock_fd, fcntl.LOCK_UN)
        finally:
            os.close(lock_fd)

def iso_now():
    return datetime.now(timezone.utc).isoformat()

def normalize_origin_payload(value):
    if not isinstance(value, dict):
        return None

    normalized = {
        "kind": normalize_text(value.get("kind")) or normalize_text(value.get("type")) or normalize_text(value.get("platform")),
        "source": normalize_text(value.get("source")) or normalize_text(value.get("path")),
        "label": normalize_text(value.get("label")) or normalize_text(value.get("name")) or normalize_text(value.get("chat_name")),
    }

    normalized = {
        key: item
        for key, item in normalized.items()
        if item is not None
    }
    return normalized or None

def detect_schedule(value):
    if value is None:
        return None, None

    text = value.strip()
    lowered = text.lower()

    if re.fullmatch(r"\d+[mhd]", lowered):
        return "delay", 1

    if re.fullmatch(r"every\s+\d+[mhd]", lowered):
        return "every", None

    try:
        datetime.fromisoformat(text.replace("Z", "+00:00"))
        return "at", 1
    except Exception:
        pass

    if len(text.split()) == 5:
        return "cron", None

    return None, None

action = normalize_text(payload.get("action"))
if action not in {"create", "update"}:
    fail("Unsupported cron mutation action.")

draft = payload.get("draft")
if not isinstance(draft, dict):
    fail("A cron draft payload is required.")

name = normalize_text(draft.get("name"))
prompt_text = normalize_text(draft.get("prompt"))
script_path = normalize_text(draft.get("script"))
workdir = normalize_text(draft.get("workdir"))
no_agent = bool(draft.get("no_agent"))
schedule_expr = normalize_text(draft.get("schedule"))
skills = normalize_list(draft.get("skills"))
model = normalize_text(draft.get("model"))
provider = normalize_text(draft.get("provider"))
base_url = normalize_text(draft.get("base_url"))
delivery = normalize_text(draft.get("deliver"))
timezone_name = normalize_text(draft.get("timezone"))
schedule_kind, repeat_times = detect_schedule(schedule_expr)

if name is None:
    fail("The cron job title is required.")
if no_agent and script_path is None:
    fail("The cron job script is required for script-only jobs.")
if not no_agent and prompt_text is None:
    fail("The cron job prompt is required.")
if schedule_expr is None:
    fail("The cron job schedule is required.")
if delivery is None:
    fail("A delivery target is required.")

def mutate_jobs():
    jobs, container_kind, container_key, container_payload = load_container(jobs_path)

    if action == "create":
        existing_ids = {
            normalize_text(item.get("id"))
            for item in jobs
            if isinstance(item, dict)
        }
        job_id = secrets.token_hex(6)
        while job_id in existing_ids:
            job_id = secrets.token_hex(6)

        job = {
            "id": job_id,
            "name": name,
            "prompt": prompt_text or "",
            "script": script_path,
            "workdir": workdir,
            "no_agent": no_agent,
            "skills": skills,
            "model": model,
            "provider": provider,
            "base_url": base_url,
            "schedule": {
                "kind": schedule_kind,
                "expr": schedule_expr,
                "timezone": timezone_name,
                "display": schedule_expr,
            },
            "schedule_display": schedule_expr,
            "repeat": {
                "times": repeat_times,
                "completed": 0,
            },
            "enabled": True,
            "state": "scheduled",
            "paused_at": None,
            "paused_reason": None,
            "created_at": iso_now(),
            "next_run_at": None,
            "last_run_at": None,
            "last_status": None,
            "last_error": None,
            "deliver": delivery,
            "origin": {
                "kind": "desktop",
                "label": "Hermes Desktop",
            },
        }
        jobs.append(job)
        save_container(jobs_path, jobs, container_kind, container_key, container_payload)
        return job_id

    job_id = normalize_text(payload.get("job_id"))
    if job_id is None:
        fail("The cron job ID is required.")

    target = None
    for item in jobs:
        if not isinstance(item, dict):
            continue
        if normalize_text(item.get("id")) == job_id:
            target = item
            break

    if target is None:
        fail(f"Cron job {job_id} was not found.")

    old_expr = normalize_text(
        ((target.get("schedule") or {}).get("expr")) if isinstance(target.get("schedule"), dict) else None
    )
    schedule_changed = old_expr != schedule_expr

    target["name"] = name
    target["prompt"] = prompt_text or ""
    target["script"] = script_path
    target["workdir"] = workdir
    target["no_agent"] = no_agent
    target["skills"] = skills
    target.pop("skill", None)
    target["model"] = model
    target["provider"] = provider
    target["base_url"] = base_url
    target["deliver"] = delivery

    normalized_origin = normalize_origin_payload(target.get("origin"))
    if normalized_origin is not None:
        target["origin"] = normalized_origin
    else:
        target.pop("origin", None)

    schedule_data = target.get("schedule")
    if not isinstance(schedule_data, dict):
        schedule_data = {}
    schedule_data["kind"] = schedule_kind
    schedule_data["expr"] = schedule_expr
    schedule_data["timezone"] = timezone_name
    schedule_data["display"] = schedule_expr
    target["schedule"] = schedule_data
    target["schedule_display"] = schedule_expr

    repeat_data = target.get("repeat")
    if not isinstance(repeat_data, dict):
        repeat_data = {}
    repeat_data["times"] = repeat_times
    if schedule_changed:
        repeat_data["completed"] = 0
    elif "completed" not in repeat_data:
        repeat_data["completed"] = 0
    target["repeat"] = repeat_data

    if schedule_changed:
        target["next_run_at"] = None
        if normalize_text(target.get("state")) != "paused":
            target["state"] = "scheduled"
        if target.get("enabled") is not False:
            target["enabled"] = True

    save_container(jobs_path, jobs, container_kind, container_key, container_payload)
    return job_id

jobs_path = resolved_hermes_home() / "cron" / "jobs.json"
job_id = with_jobs_lock(jobs_path, mutate_jobs)
print(json.dumps({
    "ok": True,
    "job_id": job_id,
}, ensure_ascii=False))
