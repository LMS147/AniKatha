using System.Globalization;
using System.Text.Json;
using AniKatha.Core;
using static AniKatha.Core.AddonResources;

namespace AniKatha.UI.Shared;

internal sealed record MangaShelf(string Title, string? Format, SearchOptions Request)
{
    internal static readonly MangaShelf[] All =
    [
        new("Popular Manga", "manga", new("MANGA", Format: "MANGA", Sort: "POPULARITY_DESC", Country: "JP")),
        new("Popular Manhwa", "manhwa", new("MANGA", Format: "MANGA", Sort: "POPULARITY_DESC", Country: "KR")),
        // AniList documents MANGA + NOVEL as its light-novel classification.
        new("Popular Light Novels", "lightnovel", new("MANGA", Format: "NOVEL", Sort: "POPULARITY_DESC", Country: "JP")),
        new("Top Rated", null, new("MANGA", Sort: "SCORE_DESC"))
    ];
}

internal sealed record MangaShelfCard(string Id, string Title, string? Poster, string Format, int? Year,
    int? Score, int? Popularity = null, JsonElement Listing = default);

internal sealed record MangaShelfPage(MangaShelfCard[] Cards, int RawCount, bool More);

internal sealed class MangaShelfFeed(string key, string label, MangaShelf? shelf = null,
    AddonResources? provider = null, AddonCatalog? catalog = null)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public AddonResources? Provider { get; } = provider;
    public AddonCatalog? Catalog { get; } = catalog;
    public bool Busy { get; set; }
    public bool Failed { get; set; }
    public bool FailedNextPage { get; set; }
    public MangaShelfSnapshot? Snapshot { get; set; }

    static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    string DeclaredFormat => Normalize(Catalog?.Format ?? "");
    bool ByPopularity => string.Equals(Catalog?.Ranking, "popularity", StringComparison.OrdinalIgnoreCase);
    bool ByScore => string.Equals(Catalog?.Ranking, "score", StringComparison.OrdinalIgnoreCase);

    public bool CouldFeed(MangaShelf target) => shelf is not null
        ? shelf == target
        : target.Format is null || DeclaredFormat.Length == 0 || DeclaredFormat == target.Format;

    public bool IsRankedFor(MangaShelf target) => target.Format is null
        ? ByScore : DeclaredFormat == target.Format && ByPopularity;

    public IEnumerable<MangaShelfCard> Cards(MangaShelf target)
    {
        if (!CouldFeed(target) || Snapshot is null) return [];
        var cards = Snapshot.Cards.AsEnumerable();
        if (shelf is not null)
            return target.Format is null ? cards.Where(card => card.Score > 0) : cards;
        if (target.Format is null)
        {
            return ByScore ? cards.Where(card => !card.Listing.TryGetProperty("communityScore", out _) || card.Score > 0) : cards.Where(card => card.Score > 0).OrderByDescending(card => card.Score).ThenBy(card => card.Id, StringComparer.Ordinal);
        }
        cards = cards.Where(card => Normalize(card.Format) == target.Format);
        return ByPopularity ? cards : cards.Where(card => card.Popularity.HasValue)
            .OrderByDescending(card => card.Popularity).ThenBy(card => card.Id, StringComparer.Ordinal);
    }

    public async Task<MangaShelfPage> Fetch(AniListClient anilist, AddonClient client, bool next, CancellationToken token)
    {
        var offset = next ? Snapshot!.Offset : 0;
        if (shelf is not null)
        {
            var page = await anilist.SearchAsync(shelf.Request with { Page = offset + 1, PageSize = 6 }, token);
            return new(page.Items.Where(item => item.Kind == "MANGA" &&
                    (shelf.Request.Format is null || item.Format == shelf.Request.Format))
                .Select(item => new MangaShelfCard(item.Id.ToString(CultureInfo.InvariantCulture),
                    item.Title, item.Poster, shelf.Format is null ? item.Format ?? "" : FormatLabel(shelf.Format),
                    item.Year, item.Score)).ToArray(), 1, page.HasNextPage);
        }

        var source = Provider!;
        var catalogue = Catalog!;
        var skip = offset > 0 || catalogue.Extras.Any(extra => extra.Name == "skip" && extra.Required)
            ? $"skip={offset.ToString(CultureInfo.InvariantCulture)}" : null;
        var root = await client.FetchJsonAsync(source.Resource("catalog", catalogue.Type, catalogue.Id, skip), token);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("metas", out var metas) || metas.ValueKind != JsonValueKind.Array)
            throw new FormatException("Expected a catalog with a metas array.");

        var cards = metas.EnumerateArray().Where(item =>
                Field(item, "type") is "" or "manga" && Field(item, "id").Length is > 0 and <= 512 &&
                Field(item, "name").Length > 0)
            .Select(item => new MangaShelfCard(Field(item, "id"), Field(item, "name"),
                MediaUrl(Field(item, "poster")) ? Field(item, "poster") : null,
                FormatLabel(Field(item, "format") is { Length: > 0 } format ? format : catalogue.Format),
                Number(item, "year", 1), Number(item, "communityScore", 1, 100),
                Number(item, "popularity", 0), item)).ToArray();
        return new(cards, metas.GetArrayLength(), catalogue.Skip);
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
