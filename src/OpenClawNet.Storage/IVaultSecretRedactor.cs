using System.Collections.Concurrent;

namespace OpenClawNet.Storage;

public interface IVaultSecretRedactor
{
    void TrackResolvedValue(string value);
    string Redact(string? content);
}

public sealed class VaultSecretRedactor : IVaultSecretRedactor
{
    private const int MaxTrackedValues = 1024;
    private const string Redacted = "[vault-secret-redacted]";
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _values = new(StringComparer.Ordinal);

    public void TrackResolvedValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var now = DateTimeOffset.UtcNow;
        _values[value] = now.Add(Retention);
        Prune(now);
    }

    public string Redact(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        Prune(DateTimeOffset.UtcNow);
        var redacted = content;
        foreach (var value in _values.Keys.OrderByDescending(v => v.Length))
        {
            if (value.Length == 0) continue;
            redacted = redacted.Replace(value, Redacted, StringComparison.Ordinal);
        }

        return redacted;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _values)
        {
            if (pair.Value <= now)
                _values.TryRemove(pair.Key, out _);
        }

        if (_values.Count <= MaxTrackedValues)
            return;

        foreach (var pair in _values.OrderBy(v => v.Value).Take(_values.Count - MaxTrackedValues))
            _values.TryRemove(pair.Key, out _);
    }
}
