using System.Globalization;
using System.Text.Json;
using AniKatha.Core;
using static AniKatha.Core.AddonResources;

namespace AniKatha.UI.Shared;

internal sealed record MangaShelf(string Title, string? Format)
{
    internal static readonly MangaShelf[] All =
    [
        new("Popular Manga", "manga"),
        new("Popular Manhwa", "manhwa"),
        new("Popular Light Novels", "lightnovel"),
        new("Top Rated", null)
    ];
}

internal sealed record MangaShelfCard(string Id, string Title, string? Poster, string Format, int? Year,
    int? Score, JsonElement Listing);

internal sealed record MangaShelfPage(MangaShelfCard[] Cards, int RawCount, bool More);

internal sealed class MangaShelfFeed(AddonResources provider, AddonCatalog catalog)
{
    public string Key { get; } = $"{provider.ProviderKey}/{catalog.Type}/{Uri.EscapeDataString(catalog.Id)}";
    public string Label { get; } = $"{provider.Addon.Name} · {catalog.Name}";
    public AddonResources Provider { get; } = provider;
    public AddonCatalog Catalog { get; } = catalog;
    public bool Busy { get; set; }
    public bool Failed { get; set; }
    public bool FailedNextPage { get; set; }
    public MangaShelfSnapshot? Snapshot { get; set; }

    static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    string DeclaredFormat => Normalize(Catalog.Format);
    bool ByScore => string.Equals(Catalog.Ranking, "score", StringComparison.OrdinalIgnoreCase);

    public static bool CanLoad(AddonCatalog catalog) =>
        (string.Equals(catalog.Ranking, "popularity", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(catalog.Ranking, "score", StringComparison.OrdinalIgnoreCase)) &&
         catalog.Extras.All(extra => !extra.Required || extra.Name == "skip");

    public bool CouldFeed(MangaShelf target) => target.Format is null ? ByScore : !ByScore &&
        (DeclaredFormat is not ("manga" or "manhwa" or "lightnovel") || DeclaredFormat == target.Format ||
         Snapshot?.Cards.Any(card => Normalize(card.Format) == target.Format) == true);

    public bool CanPage(MangaShelf target) => Snapshot is { More: true } &&
        (target.Format is null || DeclaredFormat == target.Format || Cards(target).Any());

    public IEnumerable<MangaShelfCard> Cards(MangaShelf target)
    {
        if (!CouldFeed(target) || Snapshot is null) return [];
        return target.Format is { } format
            ? Snapshot.Cards.Where(card => Normalize(card.Format) == format)
            : Snapshot.Cards.Where(card => !card.Listing.TryGetProperty("communityScore", out _) || card.Score > 0);
    }

    public async Task<MangaShelfPage> Fetch(AddonClient client, bool next, CancellationToken token)
    {
        var offset = next ? Snapshot!.Offset : 0;
        var skip = offset > 0 || Catalog.Extras.Any(extra => extra.Name == "skip" && extra.Required)
            ? $"skip={offset.ToString(CultureInfo.InvariantCulture)}" : null;
        var root = await client.FetchJsonAsync(Provider.Resource("catalog", Catalog.Type, Catalog.Id, skip), token);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("metas", out var metas) || metas.ValueKind != JsonValueKind.Array)
            throw new FormatException("Expected a catalog with a metas array.");

        var cards = metas.EnumerateArray().Where(item =>
                Field(item, "type") is "" or "manga" && Field(item, "id").Length is > 0 and <= 512 &&
                Field(item, "name").Length > 0)
            .Select(item => new MangaShelfCard(Field(item, "id"), Field(item, "name"),
                MediaUrl(Field(item, "poster")) ? Field(item, "poster") : null,
                FormatLabel(Field(item, "format") is { Length: > 0 } format ? format : Catalog.Format),
                Number(item, "year", 1), Number(item, "communityScore", 1, 100), item)).ToArray();
        return new(cards, metas.GetArrayLength(), Catalog.Skip);
    }

    static string FormatLabel(string format) => Normalize(format) switch
    {
        "manga" => "Manga",
        "manhwa" => "Manhwa",
        "lightnovel" => "Light Novel",
        _ => format.Replace('_', ' ')
    };

    static int? Number(JsonElement item, string name, int min, int max = int.MaxValue) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number >= min && number <= max ? number : null;
}

internal sealed record MangaShelfSnapshot(MangaShelfCard[] Cards, int Offset, bool More, DateTimeOffset Loaded, int Order);
