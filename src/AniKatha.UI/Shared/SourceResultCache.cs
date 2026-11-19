using System.Text.Json;
using AniKatha.Core;

namespace AniKatha.UI.Shared;

public sealed class SourceResultCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private sealed record Entry(string? Manifest, JsonElement Data, DateTimeOffset Expires);

    internal bool TryGet(string key, string? manifest, out JsonElement result)
    {
        if (entries.TryGetValue(key, out var entry) && entry.Manifest == manifest && entry.Expires > DateTimeOffset.UtcNow)
        {
            result = entry.Data;
            return true;
        }
        entries.Remove(key);
        result = default;
        return false;
    }

    internal void Set(string key, string? manifest, JsonElement data)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(2);
        foreach (var item in AddonResources.Items(data, "streams").Prepend(data))
        {
            if (DateTimeOffset.TryParse(AddonResources.Field(item, "expiresAt"),
                    System.Globalization.CultureInfo.InvariantCulture, out var knownExpiry) && knownExpiry < expiry)
                expiry = knownExpiry;
        }
        if (expiry <= now) return;
        if (entries.Count >= 64)
            entries.Remove(entries.MinBy(item => item.Value.Expires).Key);
        entries[key] = new(manifest, data, expiry);
    }

    internal void Invalidate(string key) => entries.Remove(key);
}
