using System.Text.Json;

namespace AniKatha.Core;

public sealed record AddonAniListProgress(int MediaId, string Kind, int Progress)
{
    public static AddonAniListProgress? Read(string type, JsonElement title, JsonElement unit)
    {
        var kind = type switch { "anime" or "series" => "ANIME", "manga" => "MANGA", _ => null };
        if (kind is null || !ReadPositiveInt(title, "anilistId", out var id) ||
            !ReadPositiveInt(unit, "anilistProgress", out var progress)) return null;
        return new(id, kind, progress);
    }

    public static int? TitleId(JsonElement title) =>
        ReadPositiveInt(title, "anilistId", out var id) ? id : null;

    private static bool ReadPositiveInt(JsonElement item, string key, out int value)
    {
        value = 0;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var property) &&
            property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value) && value > 0;
    }
}
