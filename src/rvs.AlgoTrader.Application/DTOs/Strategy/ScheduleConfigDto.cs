using System.Text.Json;
using System.Text.Json.Serialization;

namespace rvs.AlgoTrader.Application.DTOs.Strategy;

/// <summary>
/// Typed representation of StrategyInstance.ScheduleJson.
/// Mirrors the frontend ScheduleConfig TypeScript interface in ScheduleEditor.tsx.
/// All times are IST (Asia/Kolkata). Serialized with snake_case property names.
/// </summary>
public record ScheduleConfigDto
{
    /// <summary>Days the strategy runs, e.g. ["MON","TUE","WED","THU","FRI"].</summary>
    [JsonPropertyName("days")]
    public IReadOnlyList<string> Days { get; init; } = ["MON", "TUE", "WED", "THU", "FRI"];

    /// <summary>Session start time in IST, "HH:MM" format, e.g. "09:20".</summary>
    [JsonPropertyName("session_start")]
    public string SessionStart { get; init; } = "09:20";

    /// <summary>Session stop time in IST, "HH:MM" format, e.g. "15:10".</summary>
    [JsonPropertyName("session_stop")]
    public string SessionStop { get; init; } = "15:10";

    /// <summary>Always "Asia/Kolkata".</summary>
    [JsonPropertyName("timezone")]
    public string Timezone { get; init; } = "Asia/Kolkata";

    /// <summary>Whether to restart automatically when the process restarts mid-session.</summary>
    [JsonPropertyName("auto_resume_on_restart")]
    public bool AutoResumeOnRestart { get; init; } = false;

    /// <summary>"SKIP" or "START_LATE" — behaviour when a session was missed at start time.</summary>
    [JsonPropertyName("missed_session_behavior")]
    public string MissedSessionBehavior { get; init; } = "SKIP";

    /// <summary>Whether to force-exit all open positions at session end.</summary>
    [JsonPropertyName("force_exit_on_session_end")]
    public bool ForceExitOnSessionEnd { get; init; } = true;

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses a ScheduleJson string. Returns null if the input is null or empty.
    /// Throws <see cref="JsonException"/> if the JSON is malformed.
    /// </summary>
    public static ScheduleConfigDto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<ScheduleConfigDto>(json, _opts);
    }

    /// <summary>Serializes to JSON for storage in StrategyInstance.ScheduleJson.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, _opts);

    /// <summary>
    /// Validates the configuration. Returns a list of error messages (empty if valid).
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Days == null || Days.Count == 0)
            errors.Add("days must contain at least one day.");

        var validDays = new HashSet<string>(["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"]);
        foreach (var d in Days ?? [])
            if (!validDays.Contains(d.ToUpperInvariant()))
                errors.Add($"'{d}' is not a valid day. Use MON/TUE/WED/THU/FRI/SAT/SUN.");

        if (!IsValidTime(SessionStart))
            errors.Add($"session_start '{SessionStart}' is not a valid HH:MM time.");

        if (!IsValidTime(SessionStop))
            errors.Add($"session_stop '{SessionStop}' is not a valid HH:MM time.");

        if (Timezone != "Asia/Kolkata")
            errors.Add("timezone must be 'Asia/Kolkata'.");

        if (MissedSessionBehavior is not ("SKIP" or "START_LATE"))
            errors.Add($"missed_session_behavior must be 'SKIP' or 'START_LATE', got '{MissedSessionBehavior}'.");

        return errors;
    }

    private static bool IsValidTime(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return false;
        var parts = t.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], out var h) && h is >= 0 and <= 23
            && int.TryParse(parts[1], out var m) && m is >= 0 and <= 59;
    }
}
