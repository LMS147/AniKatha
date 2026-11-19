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
    public const string SubtitleLanguageKey = "player:subtitle-language";
    public const string AutoplayKey = "player:autoplay";
    public const string SpeedKey = "player:default-speed";

    public static double ReadSpeed(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) &&
        PlaybackSpeeds.Contains(speed) ? speed : 1;
    public static int PreferredTrack(IReadOnlyList<EpisodeSubtitle> tracks, string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return -1;
        var preferred = language.Trim();
        var fallback = -1;
        var primary = preferred.Split('-')[0];
        for (var index = 0; index < tracks.Count; index++)
        {
            var available = tracks[index].Language;
            if (string.Equals(available, preferred, StringComparison.OrdinalIgnoreCase)) return index;
            if (fallback < 0 && string.Equals(available.Split('-')[0], primary, StringComparison.OrdinalIgnoreCase))
                fallback = index;
        }
        return fallback;
    }
}
