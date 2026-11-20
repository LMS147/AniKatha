using System.Globalization;

namespace AniKatha.Core;

public static class MediaPreferences
{
    public static IReadOnlyList<double> PlaybackSpeeds { get; } = Array.AsReadOnly(new[] { 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2 });
    public static IReadOnlyList<int> SeekIntervals { get; } = Array.AsReadOnly(new[] { 5, 10, 15, 30, 60 });
    public const string SeekIntervalKey = "player:seek-seconds";

    public static int ReadSeekInterval(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var interval) && SeekIntervals.Contains(interval)
            ? interval : 10;

    public const string SubtitleSizeKey = "player:subtitle-size";
    public const string SubtitleBackgroundKey = "player:subtitle-background";
    public const string SubtitlePlacementKey = "player:subtitle-placement";
    public const string AudioLanguageKey = "player:audio-language";
    public const string SubtitleLanguageKey = "player:subtitle-language";
    public const string PlayOnOpenKey = "player:play-on-open";
    public const string PreferDubKey = "player:prefer-dub";
    public const string DimWatchedKey = "player:dim-watched";
    public const string AutoplayKey = "player:autoplay";
    public const string SpeedKey = "player:default-speed";

    public static double ReadSpeed(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) &&
        PlaybackSpeeds.Contains(speed) ? speed : 1;
    public static int PreferredTrack(IReadOnlyList<EpisodeSubtitle> tracks, string? language)
    {
        var best = -1;
        var bestScore = 0;
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var match = LanguageMatch(track.Language, language);
            if (match == 0) continue;
            var score = match * 2 + (track.Role.Trim().ToLowerInvariant() is "forced" or "signs" or "signs-and-songs" ? 0 : 1);
            if (score <= bestScore) continue;
            best = index;
            bestScore = score;
        }
        return best;
    }

    public static bool IsDubbed(string value) => value
        .Split([' ', '-', '_', '(', ')', '[', ']', ',', '/'], StringSplitOptions.RemoveEmptyEntries)
        .Any(word => word.Equals("dub", StringComparison.OrdinalIgnoreCase) ||
                     word.Equals("dubbed", StringComparison.OrdinalIgnoreCase));

    public static int LanguageMatch(string? available, string? preferred)
    {
        if (string.IsNullOrWhiteSpace(available) || string.IsNullOrWhiteSpace(preferred)) return 0;
        available = available.Trim();
        preferred = preferred.Trim();
        if (string.Equals(available, preferred, StringComparison.OrdinalIgnoreCase)) return 2;
        return string.Equals(available.Split('-')[0], preferred.Split('-')[0], StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

}

public sealed record ReaderPreferences(string Mode = "ltr", string Fit = "width", int Zoom = 100)
{
    public const string GlobalKey = "reader:defaults";
    public static IReadOnlyList<int> ZoomLevels { get; } = Array.AsReadOnly(new[] { 100, 125, 150, 200 });
    public static string SafeMode(string? mode) => mode is "vertical" or "rtl" ? mode : "ltr";
    public static string SafeFit(string? fit) => fit == "page" ? fit : "width";
    public static int SafeZoom(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var zoom) && ZoomLevels.Contains(zoom) ? zoom : 100;

    public static async Task<ReaderPreferences> LoadAsync(LocalStore store, string key)
    {
        var json = await store.GetSettingAsync(key);
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var value = System.Text.Json.JsonSerializer.Deserialize<ReaderPreferences>(json);
            return value is null ? new() : new(SafeMode(value.Mode), SafeFit(value.Fit),
                ZoomLevels.Contains(value.Zoom) ? value.Zoom : 100);
        }
        catch (System.Text.Json.JsonException) { return new(); }
    }

    public Task SaveAsync(LocalStore store, string key) => store.SetSettingAsync(key, System.Text.Json.JsonSerializer.Serialize(this));
}
