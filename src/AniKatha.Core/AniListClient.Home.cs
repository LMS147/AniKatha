using System.Net.Http.Json;
using System.Text.Json;

namespace AniKatha.Core;

public enum HomeCatalogue { TrendingAnime, UpcomingAnime, NewRelease, JustCompleted, RecentPopularManga, NewManga }

public sealed record HomeTitle(Media Media, DateOnly? Date, string? Season, int? SeasonYear = null);
public sealed record AiringEpisode(Media Media, int Episode, DateTimeOffset AirsAt);

public sealed partial class AniListClient
{
    private const string HomeMediaFields = """
        id type isAdult title { userPreferred } coverImage { large } bannerImage description(asHtml: false)
        startDate { year month day } endDate { year month day } season seasonYear format averageScore episodes chapters genres status
        """;

    public async Task<IReadOnlyList<HomeTitle>> HomeCatalogueAsync(HomeCatalogue feed, DateOnly today, CancellationToken token)
    {
        var (type, status, sort, start, end) = feed switch
        {
            HomeCatalogue.TrendingAnime => ("ANIME", "RELEASING", "TRENDING_DESC", (int?)null, (int?)null),
            HomeCatalogue.UpcomingAnime => ("ANIME", "NOT_YET_RELEASED", "START_DATE", null, null),
            HomeCatalogue.NewRelease => ("ANIME", (string?)null, "START_DATE_DESC", Fuzzy(today.AddDays(-30)), null),
            HomeCatalogue.JustCompleted => ("ANIME", "FINISHED", "END_DATE_DESC", null, Fuzzy(today.AddDays(-30))),
            HomeCatalogue.RecentPopularManga => ("MANGA", null, "POPULARITY_DESC", Fuzzy(today.AddYears(-1)), null),
            HomeCatalogue.NewManga => ("MANGA", null, "START_DATE_DESC", Fuzzy(today.AddDays(-90)), null),
            _ => throw new ArgumentOutOfRangeException(nameof(feed))
        };
        var query = $$"""
            query ($type: MediaType, $status: MediaStatus, $sort: [MediaSort], $start: FuzzyDateInt, $end: FuzzyDateInt, $before: FuzzyDateInt, $page: Int) {
              Page(page: $page, perPage: 24) {
                pageInfo { hasNextPage }
                media(type: $type, status: $status, sort: $sort, isAdult: false,
                      startDate_greater: $start, endDate_greater: $end, startDate_lesser: $before) {
                  {{HomeMediaFields}}
                }
              }
            }
            """;
        var airing = feed == HomeCatalogue.TrendingAnime;
        var ranked = new List<HomeTitle>();
        var seen = new HashSet<int>();
        for (var page = 1; ; page++)
        {
            var variables = new { type, status, sort = new[] { sort }, start, end, page,
                before = start.HasValue ? (int?)Fuzzy(today.AddDays(1)) : null };
            using var doc = await HomeQuery(query, variables, token);
            var data = doc.RootElement.GetProperty("data").GetProperty("Page");
            var seenBefore = seen.Count;
            foreach (var item in HomeItems(data, "media"))
            {
                if (Number(item, "id") is not int id || id <= 0) continue;
                if (!seen.Add(id) || Text(item, "type") != type ||
                    !item.TryGetProperty("title", out var title) || string.IsNullOrWhiteSpace(Text(title, "userPreferred"))) continue;
                var date = ReadDate(item, end.HasValue ? "endDate" : "startDate");
                if (airing && (Text(item, "status") != "RELEASING" || date > today)) continue;
                if (feed == HomeCatalogue.JustCompleted && (date is null || date > today)) continue;
                ranked.Add(new HomeTitle(ReadMedia(item), date, Text(item, "season"), Number(item, "seasonYear")));
                if (airing && ranked.Count == 10) return ranked;
            }
            if (!airing || seen.Count == seenBefore || !data.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()) return ranked;
        }
    }

    public async Task<IReadOnlyList<AiringEpisode>> HomeAiringsAsync(DateTimeOffset from, DateTimeOffset until,
        bool newestFirst, CancellationToken token)
    {
        var query = $$"""
            query ($from: Int, $until: Int, $sort: [AiringSort], $page: Int) {
              Page(page: $page, perPage: 50) {
                pageInfo { hasNextPage }
                airingSchedules(airingAt_greater: $from, airingAt_lesser: $until, sort: $sort) {
                  episode airingAt media { {{HomeMediaFields}} }
                }
              }
            }
            """;
        var result = new List<AiringEpisode>();
        for (var page = 1; page <= 4; page++)
        {
            var variables = new { from = from.ToUnixTimeSeconds() - 1, until = until.ToUnixTimeSeconds(),
                sort = new[] { newestFirst ? "TIME_DESC" : "TIME" }, page };
            using var doc = await HomeQuery(query, variables, token);
            var data = doc.RootElement.GetProperty("data").GetProperty("Page");
            foreach (var item in HomeItems(data, "airingSchedules"))
            {
                if (!item.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object ||
                    media.GetProperty("isAdult").ValueKind != JsonValueKind.False || Text(media, "type") != "ANIME" ||
                    Number(item, "episode") is not int episode || episode < 1 ||
                    Number(item, "airingAt") is not int timestamp) continue;
                var time = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                if (time >= from && time < until) result.Add(new(ReadMedia(media), episode, time));
            }
            if (!data.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean() ||
                newestFirst && result.Select(item => item.Media.Id).Distinct().Count() >= 16) break;
        }
        return result.DistinctBy(item => (item.Media.Id, item.Episode, item.AirsAt)).ToArray();
    }

    private async Task<JsonDocument> HomeQuery(string query, object variables, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(Endpoint, new { query, variables }, token);
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        if (doc.RootElement.TryGetProperty("errors", out _))
        {
            doc.Dispose();
            throw new InvalidOperationException("AniList could not complete the Home request.");
        }
        return doc;
    }

    private static int Fuzzy(DateOnly date) => date.Year * 10000 + date.Month * 100 + date.Day;
    private static JsonElement.ArrayEnumerator HomeItems(JsonElement node, string key) =>
        node.GetProperty(key).EnumerateArray();
    private static DateOnly? ReadDate(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object ||
            Number(value, "year") is not int year || Number(value, "month") is not int month ||
            Number(value, "day") is not int day || year is < 1 or > 9999 || month is < 1 or > 12 ||
            day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new(year, month, day);
    }
}
