import os
import selectors
import shutil
import subprocess
import time

def compact_output(stdout, stderr, exit_code):
    merged = "\n".join([
        stringify(stderr).strip() if stringify(stderr) else "",
        stringify(stdout).strip() if stringify(stdout) else "",
    ]).strip()
    if not merged:
        return f"Hermes chat exited with code {exit_code}."
    if len(merged) <= 4000:
        return merged
    return merged[-4000:]

def compact_text(value, limit=12000):
    text = stringify(value)
    if text is None or len(text) <= limit:
        return text
    return text[-limit:]

def looks_like_approval_request(text):
    lowered = (text or "").lower()
    approval_markers = [
        "approval required",
        "requires confirmation",
        "requires approval",
        "command approval",
        "approve command",
        "approve this command",
        "confirm command",
        "do you want to proceed",
        "allow command",
        "authorization required",
    ]
    if any(marker in lowered for marker in approval_markers):
        return True
    if "dangerous command" in lowered and "choice [o/s/a/d]" in lowered:
        return True
    if all(marker in lowered for marker in ["[o]nce", "[s]ession", "[a]lways", "[d]eny"]):
        return True
    return (
        "approve" in lowered and
        "deny" in lowered and
        any(marker in lowered for marker in ["command", "choice", "permission", "approval", "request"])
    )

def looks_like_approval_denial_stop(text):
    lowered = (text or "").lower()
    denial_markers = [
        "command denied. stopping",
        "command denied. stopped",
        "permission denied. stopping",
        "request denied. stopping",
    ]
    if any(marker in lowered for marker in denial_markers):
        return True
    return "✗ denied" in lowered and "stopping" in lowered

def looks_like_approval_denial(text):
    lowered = (text or "").lower()
    return "✗ denied" in lowered or "command denied" in lowered

def approval_error(message):
    return (
        "Hermes requested command approval, but this chat turn cannot collect manual approvals. "
        "Retry this turn with Auto-approve enabled, or resume the session in Terminal to review the command yourself."
        + ("\n\n" + message if message else "")
    )

def stop_process(process):
    if process.poll() is not None:
        return
    try:
        process.terminate()
        process.wait(timeout=5)
    except Exception:
        try:
            process.kill()
            process.wait(timeout=2)
        except Exception:
            pass

def run_hermes_chat(command, cwd, env, timeout_seconds, auto_approve_commands):
    process = subprocess.Popen(
        command,
        cwd=cwd,
        env=env,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    selector = selectors.DefaultSelector()
    stdout_chunks = []
    stderr_chunks = []
    started_at = time.monotonic()
    approval_seen_at = None
    last_output_at = started_at
    approval_grace_seconds = 8.0
    denial_seen_at = None
    denial_grace_seconds = 30.0

    if process.stdout is not None:
        selector.register(process.stdout, selectors.EVENT_READ, "stdout")
    if process.stderr is not None:
        selector.register(process.stderr, selectors.EVENT_READ, "stderr")

    def append_chunk(stream_name, data):
        text = stringify(data)
        if stream_name == "stderr":
            stderr_chunks.append(text)
        else:
            stdout_chunks.append(text)

    try:
        while True:
            now = time.monotonic()
            if now - started_at > timeout_seconds:
                stdout = "".join(stdout_chunks)
                stderr = "".join(stderr_chunks)
                partial = compact_output(stdout, stderr, 124)
                stop_process(process)
                if looks_like_approval_request(partial):
                    fail(approval_error(partial))
                fail(
                    "Hermes did not finish within the allotted time. The turn was stopped so the app would not remain blocked indefinitely."
                    + ("\n\n" + partial if partial else "")
                )

            events = selector.select(timeout=0.2)
            for key, _ in events:
                try:
                    data = key.fileobj.read1(4096)
                except AttributeError:
                    data = key.fileobj.read(4096)
                if data:
                    append_chunk(key.data, data)
                    last_output_at = time.monotonic()
                else:
                    try:
                        selector.unregister(key.fileobj)
                    except Exception:
                        pass

            exit_code = process.poll()
            if exit_code is not None:
                for pipe, stream_name in ((process.stdout, "stdout"), (process.stderr, "stderr")):
                    if pipe is None:
                        continue
                    try:
                        remaining = pipe.read()
                    except Exception:
                        remaining = b""
                    if remaining:
                        append_chunk(stream_name, remaining)
                return "".join(stdout_chunks), "".join(stderr_chunks), exit_code

            if not auto_approve_commands:
                partial = compact_output("".join(stdout_chunks), "".join(stderr_chunks), None)
                if looks_like_approval_denial_stop(partial):
                    approval_seen_at = None
                    denial_seen_at = None
                elif looks_like_approval_denial(partial):
                    approval_seen_at = None
                    if denial_seen_at is None:
                        denial_seen_at = now
                    elif (
                        now - denial_seen_at >= denial_grace_seconds and
                        now - last_output_at >= denial_grace_seconds
                    ):
                        stop_process(process)
                        fail(approval_error(partial))
                elif looks_like_approval_request(partial):
                    denial_seen_at = None
                    if approval_seen_at is None:
                        approval_seen_at = now
                    elif (
                        now - approval_seen_at >= approval_grace_seconds and
                        now - last_output_at >= approval_grace_seconds
                    ):
                        stop_process(process)
                        fail(approval_error(partial))
                else:
                    approval_seen_at = None
                    denial_seen_at = None
    finally:
        selector.close()

try:
    hermes_home = resolved_hermes_home()
    home = pathlib.Path.home()
    env = os.environ.copy()
    env["HERMES_HOME"] = str(hermes_home)
    env.setdefault("NO_COLOR", "1")
    env.setdefault("TERM", "dumb")

    path_entries = [
        str(home / ".local" / "bin"),
        str(home / ".hermes" / "hermes-agent" / "venv" / "bin"),
        str(home / ".cargo" / "bin"),
        "/opt/homebrew/bin",
        "/usr/local/bin",
        env.get("PATH", ""),
    ]
    env["PATH"] = os.pathsep.join([entry for entry in path_entries if entry])

    hermes_path = shutil.which("hermes", path=env["PATH"])
    if hermes_path is None:
        fail("Hermes CLI was not found in the remote SSH environment. Verify that `hermes` is installed and available on PATH for non-interactive SSH commands.")

    arguments = payload.get("arguments") or []
    if not isinstance(arguments, list) or not all(isinstance(item, str) for item in arguments):
        fail("Invalid Hermes chat invocation.")

    timeout_seconds = int(payload.get("timeout_seconds") or 1800)
    auto_approve_commands = bool(payload.get("auto_approve_commands"))

    stdout, stderr, exit_code = run_hermes_chat(
        [hermes_path] + arguments,
        cwd=str(home),
        env=env,
        timeout_seconds=timeout_seconds,
        auto_approve_commands=auto_approve_commands,
    )

    message = compact_output(stdout, stderr, exit_code)
    if not auto_approve_commands and looks_like_approval_denial_stop(message):
        fail(approval_error(message))

    if exit_code != 0:
        if not auto_approve_commands and looks_like_approval_request(message):
            fail(approval_error(message))
        fail(message)

    print(json.dumps({
        "ok": True,
        "session_id": payload.get("session_id"),
        "stdout": compact_text(stdout),
        "stderr": compact_text(stderr),
    }, ensure_ascii=False))
except Exception as exc:
    fail(f"Unable to run Hermes chat over SSH: {exc}")
