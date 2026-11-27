using System.Text.Json;
using AniKatha.Core;

namespace AniKatha.UI.Shared;

public sealed class CatalogSession
{
    internal Dictionary<string, MangaShelfSnapshot> MangaShelves { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> ExpandedMangaShelves { get; } = [];
    internal void ReorderMangaProviders(IEnumerable<string> providerKeys)
    {
        var keys = providerKeys.SelectMany(provider => MangaShelves.Keys.Where(key =>
            key.StartsWith(provider + "/", StringComparison.Ordinal))).ToArray();
        var order = keys.Select(key => MangaShelves[key].Order).Order().ToArray();
        for (var i = 0; i < keys.Length; i++)
            MangaShelves[keys[i]] = MangaShelves[keys[i]] with { Order = order[i] };
    }

    internal int MangaShelfOrder { get; set; }
    internal void InvalidateMangaProvider(string providerKey)
    {
        foreach (var key in MangaShelves.Keys.Where(key => key.StartsWith(providerKey + "/", StringComparison.Ordinal)).ToArray())
            MangaShelves.Remove(key);
    }

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
