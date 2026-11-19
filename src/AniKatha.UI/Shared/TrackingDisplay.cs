using AniKatha.Core;

namespace AniKatha.UI.Shared;

internal static class TrackingDisplay
{
    internal static string Mode(SavedTitle title) => title.Kind == "MANGA" ? "Manga" : "Anime";
    internal static string Units(SavedTitle title) => title.Kind == "MANGA" ? "chapters" : "episodes";
    internal static bool CanSync(SavedTitle title) => title.Id > 0 && title.ProviderKey is null;

    internal static string Status(string status, bool manga) => status switch
    {
        "CURRENT" => manga ? "Reading" : "Watching",
        "COMPLETED" => "Completed", "PAUSED" => "Paused",
        "REPEATING" => manga ? "Rereading" : "Rewatching",
        "DROPPED" => "Dropped", _ => "Planned"
    };

    internal static string Progress(SavedTitle title, int? total)
    {
        var count = total is int value ? $"{title.Progress} of {value}" : title.Progress.ToString();
        var units = title.Progress == 1 && total is null ? Units(title)[..^1] : Units(title);
        return $"{count} {units} {(title.Kind == "MANGA" ? "read" : "watched")}";
    }
}
