using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class CronJobListResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("jobs")] public List<CronJob> Jobs { get; set; } = new();
}

public class CronJob
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
    [JsonPropertyName("skills")] public List<string> Skills { get; set; } = new();
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("base_url")] public string? BaseUrl { get; set; }
    [JsonPropertyName("schedule")] public CronSchedule? Schedule { get; set; }
    [JsonPropertyName("schedule_display")] public string ScheduleDisplay { get; set; } = string.Empty;
    [JsonPropertyName("recurrence")] public CronRecurrence? Recurrence { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = "scheduled";
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("next_run_at")] public string? NextRunAt { get; set; }
    [JsonPropertyName("last_run_at")] public string? LastRunAt { get; set; }
    [JsonPropertyName("last_status")] public string? LastStatus { get; set; }
    [JsonPropertyName("last_error")] public string? LastError { get; set; }
    [JsonPropertyName("delivery_target")] public string? DeliveryTarget { get; set; }
    [JsonPropertyName("origin")] public CronJobOrigin? Origin { get; set; }
    [JsonPropertyName("last_delivery_error")] public string? LastDeliveryError { get; set; }
    [JsonPropertyName("script")] public string? Script { get; set; }
    [JsonPropertyName("workdir")] public string? Workdir { get; set; }
    [JsonPropertyName("no_agent")] public bool NoAgent { get; set; }

    [JsonIgnore]
    public string ResolvedName => string.IsNullOrWhiteSpace(Name) ? Id : Name.Trim();

    [JsonIgnore]
    public string? TrimmedPrompt
    {
        get
        {
            var t = Prompt?.Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }
    }

    [JsonIgnore]
    public string PreviewPrompt
    {
        get
        {
            if (NoAgent) return $"Script-only watchdog: {TrimmedScript ?? "No script configured"}";
            if (TrimmedPrompt is null) return "No saved prompt payload";
            var compact = TrimmedPrompt.Replace("\n", " ").Replace("\r", " ");
            return compact.Length > 140 ? compact[..140] + "…" : compact;
        }
    }

    [JsonIgnore]
    public string? RawScheduleText
    {
        get
        {
            var expr = Schedule?.Expr?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(expr)) return expr;
            var display = ScheduleDisplay?.Trim() ?? string.Empty;
            return string.IsNullOrEmpty(display) ? null : display;
        }
    }

    [JsonIgnore]
    public string ResolvedScheduleDisplay =>
        RawScheduleText is { } raw
            ? (CronScheduleFormatter.HumanReadableDescription(raw) ?? raw)
            : "No schedule metadata";

    [JsonIgnore]
    public bool IsPaused => string.Equals(State, "paused", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsActive =>
        string.Equals(State, "scheduled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayState
    {
        get
        {
            var norm = (State ?? string.Empty).Trim().ToLowerInvariant();
            return norm switch
            {
                "scheduled" => "Active",
                "paused" => "Paused",
                "running" => "Running",
                "failed" => "Failed",
                "error" => "Error",
                _ => Enabled ? "Active" : "Paused"
            };
        }
    }

    [JsonIgnore]
    public string? TrimmedScript
    {
        get
        {
            var t = Script?.Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }
    }

    [JsonIgnore]
    public string? TrimmedWorkdir
    {
        get
        {
            var t = Workdir?.Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }
    }

    [JsonIgnore]
    public string ExecutionModeTitle => NoAgent ? "Script Only" : "Agent";

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        var haystacks = new[]
        {
            Id, ResolvedName, Prompt ?? string.Empty,
            ResolvedScheduleDisplay, RawScheduleText ?? string.Empty,
            Model ?? string.Empty, Provider ?? string.Empty,
            BaseUrl ?? string.Empty, DeliveryTarget ?? string.Empty,
            Script ?? string.Empty, Workdir ?? string.Empty, ExecutionModeTitle
        }.Concat(Skills);

        return haystacks.Any(s => s?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
    }
}

public class CronSchedule
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("expr")] public string? Expr { get; set; }
    [JsonPropertyName("timezone")] public string? Timezone { get; set; }
}

public class CronRecurrence
{
    [JsonPropertyName("times")] public int? Times { get; set; }
    [JsonPropertyName("remaining")] public int? Remaining { get; set; }
}

public class CronJobOrigin
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
}

public enum CronSchedulePreset
{
    AfterDelay,
    AtDateTime,
    EveryInterval,
    Hourly,
    Daily,
    Weekdays,
    Weekly,
    Monthly,
    Custom
}

public enum CronIntervalUnit
{
    Minutes,
    Hours,
    Days
}

public static class CronIntervalUnitExtensions
{
    public static string ShortLabel(this CronIntervalUnit u) => u switch
    {
        CronIntervalUnit.Minutes => "m",
        CronIntervalUnit.Hours => "h",
        CronIntervalUnit.Days => "d",
        _ => "h"
    };

    public static string Title(this CronIntervalUnit u) => u switch
    {
        CronIntervalUnit.Minutes => "Minutes",
        CronIntervalUnit.Hours => "Hours",
        CronIntervalUnit.Days => "Days",
        _ => "Hours"
    };
}

public enum CronDeliveryPreset
{
    Local,
    Origin,
    All,
    Telegram,
    Discord,
    Slack,
    Whatsapp,
    Email,
    Custom
}

public static class CronDeliveryPresetExtensions
{
    public static string Title(this CronDeliveryPreset p) => p switch
    {
        CronDeliveryPreset.Local => "Local Only",
        CronDeliveryPreset.Origin => "Origin Chat",
        CronDeliveryPreset.All => "All Connected Channels",
        CronDeliveryPreset.Telegram => "Telegram Home",
        CronDeliveryPreset.Discord => "Discord Home",
        CronDeliveryPreset.Slack => "Slack Home",
        CronDeliveryPreset.Whatsapp => "WhatsApp Home",
        CronDeliveryPreset.Email => "Email",
        CronDeliveryPreset.Custom => "Custom Target",
        _ => "Local Only"
    };

    public static string? ResolvedValue(this CronDeliveryPreset p) => p switch
    {
        CronDeliveryPreset.Local => "local",
        CronDeliveryPreset.Origin => "origin",
        CronDeliveryPreset.All => "all",
        CronDeliveryPreset.Telegram => "telegram",
        CronDeliveryPreset.Discord => "discord",
        CronDeliveryPreset.Slack => "slack",
        CronDeliveryPreset.Whatsapp => "whatsapp",
        CronDeliveryPreset.Email => "email",
        _ => null
    };

    public static (CronDeliveryPreset preset, string custom) FromDeliveryTarget(string? target)
    {
        var trimmed = (target ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return (CronDeliveryPreset.Custom, string.Empty);
        return trimmed switch
        {
            "local" => (CronDeliveryPreset.Local, string.Empty),
            "origin" => (CronDeliveryPreset.Origin, string.Empty),
            "all" => (CronDeliveryPreset.All, string.Empty),
            "telegram" => (CronDeliveryPreset.Telegram, string.Empty),
            "discord" => (CronDeliveryPreset.Discord, string.Empty),
            "slack" => (CronDeliveryPreset.Slack, string.Empty),
            "whatsapp" => (CronDeliveryPreset.Whatsapp, string.Empty),
            "email" => (CronDeliveryPreset.Email, string.Empty),
            _ => (CronDeliveryPreset.Custom, trimmed)
        };
    }
}

public class CronScheduleDraft
{
    public CronSchedulePreset Preset { get; set; } = CronSchedulePreset.Daily;
    public int Hour { get; set; } = 9;
    public int Minute { get; set; } = 0;
    public int Weekday { get; set; } = 1;
    public int DayOfMonth { get; set; } = 1;
    public int IntervalValue { get; set; } = 1;
    public CronIntervalUnit IntervalUnit { get; set; } = CronIntervalUnit.Hours;
    public DateTime OneTimeDate { get; set; } = DateTime.Now.AddHours(1);
    public string CustomExpression { get; set; } = string.Empty;

    public static CronScheduleDraft FromJob(CronJob job) =>
        FromExpression(job.RawScheduleText);

    public static CronScheduleDraft FromExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return new CronScheduleDraft();

        var trimmed = expression.Trim();

        if (CronScheduleFormatter.TryParseDelay(trimmed, out var delayValue, out var delayUnit))
        {
            return new CronScheduleDraft
            {
                Preset = CronSchedulePreset.AfterDelay,
                IntervalValue = delayValue,
                IntervalUnit = delayUnit,
                CustomExpression = trimmed
            };
        }

        if (CronScheduleFormatter.TryParseEvery(trimmed, out var everyValue, out var everyUnit))
        {
            return new CronScheduleDraft
            {
                Preset = CronSchedulePreset.EveryInterval,
                IntervalValue = everyValue,
                IntervalUnit = everyUnit,
                CustomExpression = trimmed
            };
        }

        if (DateTime.TryParse(trimmed, out var parsedDate))
        {
            return new CronScheduleDraft
            {
                Preset = CronSchedulePreset.AtDateTime,
                OneTimeDate = parsedDate,
                CustomExpression = trimmed
            };
        }

        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            return new CronScheduleDraft { Preset = CronSchedulePreset.Custom, CustomExpression = trimmed };
        }

        var minuteOk = int.TryParse(parts[0], out var minute);
        var hourOk = int.TryParse(parts[1], out var hour);
        var dayOk = int.TryParse(parts[2], out var dayOfMonth);
        var month = parts[3];
        var dow = parts[4];

        if (parts[2] == "*" && month == "*" && dow == "*" && minuteOk && parts[1] == "*")
        {
            return new CronScheduleDraft { Preset = CronSchedulePreset.Hourly, Minute = minute, CustomExpression = trimmed };
        }

        if (!minuteOk || !hourOk || month != "*")
        {
            return new CronScheduleDraft { Preset = CronSchedulePreset.Custom, CustomExpression = trimmed };
        }

        if (parts[2] == "*" && dow == "*")
            return new CronScheduleDraft { Preset = CronSchedulePreset.Daily, Hour = hour, Minute = minute, CustomExpression = trimmed };

        if (parts[2] == "*" && dow == "1-5")
            return new CronScheduleDraft { Preset = CronSchedulePreset.Weekdays, Hour = hour, Minute = minute, CustomExpression = trimmed };

        if (parts[2] == "*" && CronScheduleFormatter.WeekdayIndex(dow) is int wd)
            return new CronScheduleDraft { Preset = CronSchedulePreset.Weekly, Hour = hour, Minute = minute, Weekday = wd, CustomExpression = trimmed };

        if (dow == "*" && dayOk)
            return new CronScheduleDraft { Preset = CronSchedulePreset.Monthly, Hour = hour, Minute = minute, DayOfMonth = dayOfMonth, CustomExpression = trimmed };

        return new CronScheduleDraft { Preset = CronSchedulePreset.Custom, CustomExpression = trimmed };
    }

    public string? Expression => Preset switch
    {
        CronSchedulePreset.AfterDelay => $"{IntervalValue}{IntervalUnit.ShortLabel()}",
        CronSchedulePreset.AtDateTime => OneTimeDate.ToString("yyyy-MM-ddTHH:mm:ss"),
        CronSchedulePreset.EveryInterval => $"every {IntervalValue}{IntervalUnit.ShortLabel()}",
        CronSchedulePreset.Hourly => $"{Minute} * * * *",
        CronSchedulePreset.Daily => $"{Minute} {Hour} * * *",
        CronSchedulePreset.Weekdays => $"{Minute} {Hour} * * 1-5",
        CronSchedulePreset.Weekly => $"{Minute} {Hour} * * {Weekday}",
        CronSchedulePreset.Monthly => $"{Minute} {Hour} {DayOfMonth} * *",
        CronSchedulePreset.Custom => string.IsNullOrWhiteSpace(CustomExpression) ? null : CustomExpression.Trim(),
        _ => null
    };

    public string Summary =>
        Expression is { } expr
            ? (CronScheduleFormatter.HumanReadableDescription(expr) ?? expr)
            : "No schedule";
}

public class CronJobDraft
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public string Workdir { get; set; } = string.Empty;
    public bool NoAgent { get; set; }
    public string SkillsText { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public CronDeliveryPreset DeliveryPreset { get; set; } = CronDeliveryPreset.Local;
    public string CustomDeliveryTarget { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public CronScheduleDraft Schedule { get; set; } = new();

    public static CronJobDraft FromJob(CronJob job)
    {
        var (preset, custom) = CronDeliveryPresetExtensions.FromDeliveryTarget(job.DeliveryTarget);
        return new CronJobDraft
        {
            Name = job.ResolvedName,
            Prompt = job.TrimmedPrompt ?? job.Prompt ?? string.Empty,
            Script = job.TrimmedScript ?? string.Empty,
            Workdir = job.TrimmedWorkdir ?? string.Empty,
            NoAgent = job.NoAgent,
            SkillsText = string.Join(", ", job.Skills),
            Model = job.Model ?? string.Empty,
            Provider = job.Provider ?? string.Empty,
            BaseUrl = job.BaseUrl ?? string.Empty,
            DeliveryPreset = preset,
            CustomDeliveryTarget = custom,
            Timezone = job.Schedule?.Timezone ?? string.Empty,
            Schedule = CronScheduleDraft.FromJob(job)
        };
    }

    public string NormalizedName => (Name ?? string.Empty).Trim();
    public string NormalizedPrompt => (Prompt ?? string.Empty).Trim();
    public string? NormalizedScript => NormalizeOpt(Script);
    public string? NormalizedWorkdir => NormalizeOpt(Workdir);

    public List<string> NormalizedSkills =>
        (SkillsText ?? string.Empty)
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

    public string? NormalizedModel => NormalizeOpt(Model);
    public string? NormalizedProvider => NormalizeOpt(Provider);
    public string? NormalizedBaseUrl => NormalizeOpt(BaseUrl);

    public string? NormalizedDeliveryTarget =>
        DeliveryPreset.ResolvedValue() ?? NormalizeOpt(CustomDeliveryTarget);

    public string? NormalizedTimezone => Schedule.Preset switch
    {
        CronSchedulePreset.AfterDelay or CronSchedulePreset.AtDateTime or CronSchedulePreset.EveryInterval => null,
        _ => NormalizeOpt(Timezone)
    };

    public string? ValidationError
    {
        get
        {
            if (string.IsNullOrEmpty(NormalizedName)) return "A cron job title is required.";
            if (NoAgent)
            {
                if (NormalizedScript is null) return "A script path is required for script-only jobs.";
            }
            else if (string.IsNullOrEmpty(NormalizedPrompt)) return "A prompt is required.";
            if (string.IsNullOrWhiteSpace(Schedule.Expression)) return "A valid schedule is required.";
            if (NormalizedDeliveryTarget is null) return "A delivery target is required.";
            return null;
        }
    }

    private static string? NormalizeOpt(string? v)
    {
        var t = (v ?? string.Empty).Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}

public static class CronScheduleFormatter
{
    public static string[] WeekdayPickerLabels => new[]
    {
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
    };

    public static string? HumanReadableDescription(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var trimmed = expression.Trim();

        if (TryParseDelay(trimmed, out var delayVal, out var delayUnit))
            return $"Once in {delayVal} {LabelFor(delayUnit, delayVal)}";

        if (TryParseEvery(trimmed, out var everyVal, out var everyUnit))
            return $"Every {everyVal} {LabelFor(everyUnit, everyVal)}";

        if (DateTime.TryParse(trimmed, out var dt))
            return $"Once on {dt:MMM d, yyyy h:mm tt}";

        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return null;

        var minute = parts[0];
        var hour = parts[1];
        var dayOfMonth = parts[2];
        var month = parts[3];
        var dow = parts[4];

        if (hour == "*" && month == "*" && dayOfMonth == "*" && dow == "*" && int.TryParse(minute, out var min))
            return $"Every hour at :{min:D2}";

        if (!int.TryParse(hour, out var h) || !int.TryParse(minute, out var m)) return null;
        if (m < 0 || m > 59 || h < 0 || h > 23) return null;

        var time = $"{h:D2}:{m:D2}";

        if (dayOfMonth == "*" && month == "*" && dow == "*")
            return $"Every day at {time}";
        if (dayOfMonth == "*" && month == "*" && dow == "1-5")
            return $"Every weekday at {time}";
        if (dayOfMonth == "*" && month == "*" && WeekdayLabel(dow) is { } wd)
            return $"Every {wd} at {time}";
        if (month == "*" && dow == "*" && int.TryParse(dayOfMonth, out var day))
            return $"On day {day} of every month at {time}";

        return null;
    }

    public static bool TryParseDelay(string value, out int quantity, out CronIntervalUnit unit) =>
        TryParseDuration(value.Trim().ToLowerInvariant(), out quantity, out unit);

    public static bool TryParseEvery(string value, out int quantity, out CronIntervalUnit unit)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        quantity = 0;
        unit = CronIntervalUnit.Hours;
        if (!trimmed.StartsWith("every ")) return false;
        return TryParseDuration(trimmed[6..], out quantity, out unit);
    }

    private static bool TryParseDuration(string value, out int quantity, out CronIntervalUnit unit)
    {
        quantity = 0;
        unit = CronIntervalUnit.Hours;
        var t = value.Trim();
        if (t.Length < 2) return false;
        if (!int.TryParse(t[..^1], out quantity)) return false;
        unit = t[^1] switch
        {
            'm' => CronIntervalUnit.Minutes,
            'h' => CronIntervalUnit.Hours,
            'd' => CronIntervalUnit.Days,
            _ => CronIntervalUnit.Hours
        };
        return t[^1] is 'm' or 'h' or 'd';
    }

    public static int? WeekdayIndex(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "0" or "7" or "sun" => 0,
            "1" or "mon" => 1,
            "2" or "tue" => 2,
            "3" or "wed" => 3,
            "4" or "thu" => 4,
            "5" or "fri" => 5,
            "6" or "sat" => 6,
            _ => null
        };

    private static string? WeekdayLabel(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "0" or "7" or "sun" => "Sun",
            "1" or "mon" => "Mon",
            "2" or "tue" => "Tue",
            "3" or "wed" => "Wed",
            "4" or "thu" => "Thu",
            "5" or "fri" => "Fri",
            "6" or "sat" => "Sat",
            _ => null
        };

    private static string LabelFor(CronIntervalUnit u, int v) => u switch
    {
        CronIntervalUnit.Minutes => v == 1 ? "minute" : "minutes",
        CronIntervalUnit.Hours => v == 1 ? "hour" : "hours",
        CronIntervalUnit.Days => v == 1 ? "day" : "days",
        _ => "hours"
    };
}
