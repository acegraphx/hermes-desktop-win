using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HermesDesktop.Models;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.Services;

public class RemotePythonScriptExecutor : IRemoteScriptExecutor
{
    private readonly ISshTransport _ssh;
    private readonly SftpConnectionPool _sftpPool;
    private readonly ILogger<RemotePythonScriptExecutor> _logger;
    private readonly Dictionary<string, string> _scriptCache = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RemotePythonScriptExecutor(
        ISshTransport ssh,
        SftpConnectionPool sftpPool,
        ILogger<RemotePythonScriptExecutor> logger)
    {
        _ssh = ssh;
        _sftpPool = sftpPool;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(
        ConnectionProfile profile,
        string scriptName,
        Dictionary<string, object>? parameters,
        CancellationToken ct) where T : class
    {
        var json = await ExecuteRawAsync(profile, scriptName, parameters, ct);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions)
            ?? throw new InvalidOperationException($"Script '{scriptName}' returned null JSON");
    }

    public async Task<string> ExecuteRawAsync(
        ConnectionProfile profile,
        string scriptName,
        Dictionary<string, object>? parameters,
        CancellationToken ct)
    {
        var body = LoadScript(scriptName);

        var payload = parameters is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(parameters);

        payload.TryAdd("hermes_home", profile.RemoteHermesHomePath);
        payload.TryAdd("profile_name", profile.ResolvedHermesProfileName);
        var timeout = TimeSpan.FromSeconds(60);
        if (payload.TryGetValue("executor_timeout_seconds", out var timeoutValue) &&
            int.TryParse(Convert.ToString(timeoutValue), out var timeoutSeconds) &&
            timeoutSeconds > 0)
        {
            timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        var paramsJson = JsonSerializer.Serialize(payload);
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(paramsJson));

        var script = BuildScript(body, payloadBase64);

        var scriptBytes = Encoding.UTF8.GetBytes(script);
        var command = scriptBytes.Length > 48_000
            ? await UploadScriptAndBuildCommandAsync(profile, scriptBytes, ct)
            : BuildInlineCommand(scriptBytes);

        _logger.LogDebug("Executing remote script: {Script} (hermes_home={Home})",
            scriptName, profile.RemoteHermesHomePath);

        var result = await _ssh.ExecuteCommandAsync(
            profile, command, ct, timeout);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Python script {Script} failed (exit {Code}): {Error}",
                scriptName, result.ExitCode, result.StandardError);
            throw new RemoteScriptException(scriptName, result.ExitCode, result.StandardError);
        }

        return result.StandardOutput;
    }

    private static string BuildInlineCommand(byte[] scriptBytes)
    {
        var base64Script = Convert.ToBase64String(scriptBytes);
        return $"printf '%s' '{base64Script}' | base64 -d | python3 -";
    }

    private async Task<string> UploadScriptAndBuildCommandAsync(ConnectionProfile profile, byte[] scriptBytes, CancellationToken ct)
    {
        var remotePath = $"/tmp/hermes-desktop-{Guid.NewGuid():N}.py";
        var sftp = await _sftpPool.GetOrCreateAsync(profile, ct);
        await using var stream = new MemoryStream(scriptBytes);
        await Task.Run(() => sftp.UploadFile(stream, remotePath, canOverride: true), ct);
        var quoted = QuoteShell(remotePath);
        return $"python3 {quoted}; status=$?; rm -f {quoted}; exit $status";
    }

    private static string QuoteShell(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    private static string BuildScript(string body, string payloadBase64)
    {
        // Matches upstream RemotePythonScript.swift: wraps body with shared helpers
        // and decodes payload from base64.
        return $@"import base64
import json
import os
import pathlib
import sys

payload = json.loads(base64.b64decode(""{payloadBase64}"").decode(""utf-8""))

{SharedHelpers}

{body}
";
    }

    // Mirror of upstream RemotePythonScript.sharedHelpers. Keep in sync.
    private const string SharedHelpers = @"
def fail(message):
    print(json.dumps({
        ""ok"": False,
        ""error"": message,
    }, ensure_ascii=False))
    sys.exit(1)

def stringify(value):
    if value is None:
        return None
    if isinstance(value, bytes):
        return value.decode(""utf-8"", errors=""replace"")
    return str(value)

def normalize_text(value):
    text = stringify(value)
    if text is None:
        return None
    text = text.strip()
    return text or None

def choose_table(tables, needle):
    lowered = needle.lower()
    for name in tables:
        if name.lower() == lowered:
            return name
    for name in tables:
        if lowered in name.lower():
            return name
    return None

def choose_column(columns, choices):
    lowered = {column.lower(): column for column in columns}
    for choice in choices:
        if choice.lower() in lowered:
            return lowered[choice.lower()]
    for choice in choices:
        for column in columns:
            if choice.lower() in column.lower():
                return column
    return None

def quote_ident(value):
    return '""' + str(value).replace('""', '""""') + '""'

def quote_text(value):
    return ""'"" + str(value).replace(""'"", ""''"") + ""'""

def expand_remote_path(value, home=None):
    if home is None:
        home = pathlib.Path.home()
    normalized = normalize_text(value)
    if normalized is None:
        return None
    if normalized == ""~"":
        return home
    if normalized.startswith(""~/""):
        return home / normalized[2:]
    return pathlib.Path(normalized)

def resolved_hermes_home(request=None):
    request_data = payload if request is None else request
    home = pathlib.Path.home()
    expanded = expand_remote_path(request_data.get(""hermes_home"") if isinstance(request_data, dict) else None, home)
    if expanded is not None:
        return expanded
    return home / "".hermes""

def tilde(path, home=None):
    if home is None:
        home = pathlib.Path.home()
    try:
        relative = path.relative_to(home)
        return ""~/"" + relative.as_posix() if relative.as_posix() != ""."" else ""~""
    except ValueError:
        return path.as_posix()

def iter_session_store_candidates(hermes_home, home=None, hinted_path=None):
    if home is None:
        home = pathlib.Path.home()

    seen = set()

    def emit(candidate):
        if candidate is None:
            return None
        resolved = str(candidate)
        if resolved in seen or not candidate.is_file():
            return None
        seen.add(resolved)
        return candidate

    hinted_candidate = emit(expand_remote_path(hinted_path, home))
    if hinted_candidate is not None:
        yield hinted_candidate

    preferred = [
        hermes_home / ""state.db"",
        hermes_home / ""state.sqlite"",
        hermes_home / ""state.sqlite3"",
        hermes_home / ""store.db"",
        hermes_home / ""store.sqlite"",
        hermes_home / ""store.sqlite3"",
    ]

    for candidate in preferred:
        candidate = emit(candidate)
        if candidate is not None:
            yield candidate

    try:
        extras = sorted(
            [
                item
                for pattern in (""*.db"", ""*.sqlite"", ""*.sqlite3"")
                for item in hermes_home.glob(pattern)
                if item.is_file()
            ],
            key=lambda item: item.stat().st_mtime,
            reverse=True,
        )
    except Exception:
        extras = []
    for candidate in extras:
        candidate = emit(candidate)
        if candidate is not None:
            yield candidate

    sessions_dir = hermes_home / ""sessions""
    if sessions_dir.exists():
        try:
            nested = sorted(
                [
                    item
                    for pattern in (""*.db"", ""*.sqlite"", ""*.sqlite3"")
                    for item in sessions_dir.rglob(pattern)
                    if item.is_file()
                ],
                key=lambda item: item.stat().st_mtime,
                reverse=True,
            )
        except Exception:
            nested = []
        for candidate in nested:
            candidate = emit(candidate)
            if candidate is not None:
                yield candidate
";

    private string LoadScript(string scriptName)
    {
        if (_scriptCache.TryGetValue(scriptName, out var cached))
            return cached;

        var assembly = typeof(RemotePythonScriptExecutor).Assembly;
        var resourceName = $"HermesDesktop.Scripts.{scriptName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded script not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        _scriptCache[scriptName] = content;
        return content;
    }
}
