using System.Text.Json;
using AniKatha.Core;

namespace AniKatha.UI.Shared;

public sealed class CatalogSession
{
    internal Dictionary<string, CatalogTab> Tabs { get; } = new(StringComparer.Ordinal);
}

internal sealed record CatalogTab(
    SearchOptions Draft, SearchOptions Applied, string Source, string? JikanKey, int Page,
    bool HasMore, bool FailedNextPage, bool StarterCatalogue, bool AniListFallback, string? Error,
    List<Media> Items, List<AddonTitle> JikanItems,
    Dictionary<(string Provider, string Type, string Id), CatalogPage> Pages);

public sealed record CatalogPage(
    Dictionary<string, string> Extras, List<JsonElement> Titles, HashSet<string> Seen,
    int Offset, bool More, string? Error);
