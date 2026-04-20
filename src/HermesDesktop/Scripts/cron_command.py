import shutil
import subprocess

def find_hermes_binary():
    candidate = shutil.which("hermes")
    if candidate:
        return candidate

    fallback = pathlib.Path.home() / ".local" / "bin" / "hermes"
    if fallback.exists() and os.access(fallback, os.X_OK):
        return str(fallback)

    return None

try:
    job_id = str(payload.get("job_id") or "").strip()
    command = str(payload.get("command") or "").strip()

    if not job_id:
        fail("The cron job ID is required.")
    if not command:
        fail("The cron command is required.")

    hermes_binary = find_hermes_binary()
    if hermes_binary is None:
        fail("Hermes CLI was not found on the active host.")

    profile_name = str(payload.get("profile_name") or "").strip()
    command_args = [hermes_binary]
    if profile_name and profile_name.lower() != "default":
        command_args.extend(["-p", profile_name])
    command_args.extend(["cron", command, job_id])

    try:
        completed = subprocess.run(
            command_args,
            capture_output=True,
            text=True,
        )
    except Exception as exc:
        fail(f"Unable to launch Hermes CLI: {exc}")

    if completed.returncode != 0:
        message = (completed.stderr or completed.stdout or f"Hermes cron {command} failed.").strip()
        fail(message)

    print(json.dumps({
        "ok": True,
        "message": (completed.stdout or "").strip() or None,
    }, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to run cron command: {exc}")
