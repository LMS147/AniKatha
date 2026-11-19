using System.Globalization;

namespace AniKatha.Core;

public static class MediaPreferences
{
    public static IReadOnlyList<double> PlaybackSpeeds { get; } = Array.AsReadOnly(new[] { 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2 });
    public const string SpeedKey = "player:default-speed";

    public static double ReadSpeed(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) &&
        PlaybackSpeeds.Contains(speed) ? speed : 1;
}
