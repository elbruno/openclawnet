using System.Text.Json;

namespace OpenClawNet.Tools.Abstractions;

public sealed record ToolInput
{
    public required string ToolName { get; init; }
    public required string RawArguments { get; init; }
    
    public T? GetArgument<T>(string key)
    {
        using var doc = JsonDocument.Parse(RawArguments);
        if (doc.RootElement.TryGetProperty(key, out var value))
        {
            return JsonSerializer.Deserialize<T>(value.GetRawText());
        }
        return default;
    }
    
    public string? GetStringArgument(string key)
    {
        using var doc = JsonDocument.Parse(RawArguments);
        if (doc.RootElement.TryGetProperty(key, out var value))
        {
            return value.GetString();
        }
        return null;
    }

    /// <summary>
    /// Reads a boolean tool argument. Accepts JSON true/false, numeric 0/1,
    /// or string forms ("true"/"false"/"1"/"0"/"yes"/"no") so the tool stays
    /// tolerant of slightly noisy LLM JSON.
    /// </summary>
    public bool GetBoolArgument(string key, bool defaultValue = false)
    {
        using var doc = JsonDocument.Parse(RawArguments);
        if (!doc.RootElement.TryGetProperty(key, out var value))
            return defaultValue;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetInt64(out var n) && n != 0,
            JsonValueKind.String => value.GetString() switch
            {
                "true" or "True" or "TRUE" or "1" or "yes" or "Yes" or "YES" => true,
                "false" or "False" or "FALSE" or "0" or "no" or "No" or "NO" => false,
                _ => defaultValue
            },
            _ => defaultValue
        };
    }
}
