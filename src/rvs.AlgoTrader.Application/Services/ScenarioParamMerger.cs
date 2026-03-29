using System.Text.Json;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Merges a base parameters JSON object with a scenario override JSON object.
/// Override keys replace base keys; keys absent from the override are kept from the base.
/// Both inputs must be JSON objects ({}). Either may be null/empty — treated as {}.
/// Returns a compact JSON string suitable for passing to a strategy config's FromJson().
/// </summary>
public static class ScenarioParamMerger
{
    public static string Merge(string? baseJson, string? overrideJson)
    {
        if (string.IsNullOrWhiteSpace(overrideJson) || overrideJson == "{}")
            return string.IsNullOrWhiteSpace(baseJson) ? "{}" : baseJson;

        if (string.IsNullOrWhiteSpace(baseJson) || baseJson == "{}")
            return overrideJson;

        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        using var baseDoc     = JsonDocument.Parse(baseJson);
        using var overrideDoc = JsonDocument.Parse(overrideJson);

        foreach (var prop in baseDoc.RootElement.EnumerateObject())
            merged[prop.Name] = prop.Value.Clone();

        foreach (var prop in overrideDoc.RootElement.EnumerateObject())
            merged[prop.Name] = prop.Value.Clone();

        using var ms     = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        writer.WriteStartObject();
        foreach (var (key, val) in merged)
        {
            writer.WritePropertyName(key);
            val.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
