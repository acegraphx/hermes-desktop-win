import re
import secrets
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
        return [], "list", None

    raw = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(raw, list):
        return raw, "list", None

    if isinstance(raw, dict):
        for key in ("jobs", "items", "cron_jobs"):
            jobs = raw.get(key)
            if isinstance(jobs, list):
                return jobs, "dict", key
        fail(f"Unsupported cron metadata wrapper in {path}.")

    fail(f"Unsupported cron metadata format in {path}.")

def save_container(path, jobs, container_kind, container_key):
    if container_kind == "list":
        payload_to_write = jobs
    else:
        payload_to_write = {container_key or "jobs": jobs}

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload_to_write, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8"
    )

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

    normalized = {k: v for k, v in normalized.items() if v is not None}
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

try:
    action = normalize_text(payload.get("action"))
    if action not in {"create", "update"}:
        fail("Unsupported cron mutation action.")

    draft = payload.get("draft")
    if not isinstance(draft, dict):
        fail("A cron draft payload is required.")

    name = normalize_text(draft.get("name"))
    prompt_text = normalize_text(draft.get("prompt"))
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
    if prompt_text is None:
        fail("The cron job prompt is required.")
    if schedule_expr is None:
        fail("The cron job schedule is required.")
    if delivery is None:
        fail("A delivery target is required.")

    jobs_path = resolved_hermes_home() / "cron" / "jobs.json"
    jobs, container_kind, container_key = load_container(jobs_path)

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
            "prompt": prompt_text,
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
            "repeat": {"times": repeat_times, "completed": 0},
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
            "origin": {"kind": "desktop", "label": "Hermes Desktop"},
        }
        jobs.append(job)
        save_container(jobs_path, jobs, container_kind, container_key)
        print(json.dumps({"ok": True, "job_id": job_id}, ensure_ascii=False))
        sys.exit(0)

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
    target["prompt"] = prompt_text
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

    save_container(jobs_path, jobs, container_kind, container_key)
    print(json.dumps({"ok": True, "job_id": job_id}, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to mutate cron job: {exc}")
