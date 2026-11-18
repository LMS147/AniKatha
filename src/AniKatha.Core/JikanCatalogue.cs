using System.Net;
using System.Text.Json;

namespace AniKatha.Core;

public sealed record AddonTitle(int Id, string Name, string? Poster, string? Format, string? Description);
public sealed record AddonPage(List<AddonTitle> Items, bool HasNextPage, bool IsStarterCatalogue = false);

public sealed class JikanCatalogue(HttpClient http)
{
    public const string Provider = "jikan-v4";

    public async Task<AddonPage> SearchAsync(string kind, string? search, int page, CancellationToken token)
    {
        try
        {
            return await FetchAsync(kind, search, page, token);
        }
        catch (HttpRequestException ex) when (page == 1 && (ex.StatusCode is null || (int)ex.StatusCode >= 500))
        {
            return StarterCatalogue(kind, search);
        }
        catch (OperationCanceledException) when (page == 1 && !token.IsCancellationRequested)
        {
            return StarterCatalogue(kind, search);
        }
    }

    private async Task<AddonPage> FetchAsync(string kind, string? search, int page, CancellationToken token)
    {
        var endpoint = kind switch
        {
            "ANIME" => "anime",
            "MANGA" => "manga",
            _ => throw new ArgumentException("Unknown catalogue type.", nameof(kind))
        };
        var query = $"?sfw=true&limit=18&page={page}";
        if (string.IsNullOrWhiteSpace(search))
            query += "&order_by=popularity&sort=asc";
        else
            query += "&q=" + Uri.EscapeDataString(search.Trim());

        using var response = await http.GetAsync($"https://api.jikan.moe/v4/{endpoint}{query}", token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Jikan is receiving too many requests. Wait a moment and retry.");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;
        var items = new List<AddonTitle>();
        var seen = new HashSet<int>();
        foreach (var item in root.GetProperty("data").EnumerateArray())
        {
            if (!item.TryGetProperty("mal_id", out var id) || !id.TryGetInt32(out var number) || number <= 0 || !seen.Add(number))
                continue;
            var name = ReadText(item, "title_english") ?? ReadText(item, "title");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var poster = item.TryGetProperty("images", out var images) && images.TryGetProperty("jpg", out var jpg)
                ? ReadText(jpg, "image_url") : null;
            if (!Uri.TryCreate(poster, UriKind.Absolute, out var image) ||
                image.Scheme != Uri.UriSchemeHttps || image.Host != "cdn.myanimelist.net")
                poster = null;
            items.Add(new AddonTitle(number, name, poster, ReadText(item, "type"), ReadText(item, "synopsis")));
        }

        return new AddonPage(items, root.GetProperty("pagination").GetProperty("has_next_page").GetBoolean());
    }

    private static AddonPage StarterCatalogue(string kind, string? search)
    {
        // These are bundled titles, shown explicitly as starter results when live browsing fails.
        AddonTitle[] titles = kind == "ANIME"
            ? [
                new(1, "Cowboy Bebop", null, "TV", null),
                new(5114, "Fullmetal Alchemist: Brotherhood", null, "TV", null),
                new(52991, "Frieren: Beyond Journey’s End", null, "TV", null),
                new(21, "One Piece", null, "TV", null)
            ]
            : [
                new(104, "Yotsuba&!", null, "Manga", null),
                new(13, "One Piece", null, "Manga", null),
                new(25, "Fullmetal Alchemist", null, "Manga", null),
                new(126287, "Frieren: Beyond Journey’s End", null, "Manga", null)
            ];
        return new AddonPage(titles.Where(title => string.IsNullOrWhiteSpace(search) ||
            title.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList(), false, true);
    }

    private static string? ReadText(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
