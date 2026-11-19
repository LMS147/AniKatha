using System.Text.Json;

namespace AniKatha.Core;

public sealed record AniListListEntry(int MediaId, string Kind, string Title, string? Poster, string Status, int Progress, double? Score);

public sealed partial class AniListAccount
{
    public Task<int> ImportAsync(LocalStore store, int viewerId, CancellationToken token = default) =>
        WithCredentialAsync(async (credential, ct) =>
        {
            if (credential.ViewerId != viewerId) throw new AniListAuthorizationException();
            var viewer = await ViewerAsync(credential, ct);
            if (viewer.Id != viewerId) throw new AniListAuthorizationException();
            var imported = 0;
            foreach (var type in new[] { "ANIME", "MANGA" })
            {
                var observed = new HashSet<int>();
                for (var page = 1; ; page++)
                {
                    const string query = """
                        query ($viewerId: Int!, $kind: MediaType!, $page: Int!, $scoreFormat: ScoreFormat!) {
                          Page(page: $page, perPage: 50) {
                            pageInfo { hasNextPage }
                            mediaList(userId: $viewerId, type: $kind, sort: [MEDIA_ID]) {
                              mediaId status progress score(format: $scoreFormat)
                              media { id type title { userPreferred } coverImage { large } }
                            }
                          }
                        }
                        """;
                    var data = await SendAsync(credential, query, new
                    {
                        viewerId, kind = type, page, scoreFormat = viewer.ScoreFormat
                    }, ct);
                    var list = data.GetProperty("Page");
                    var entries = list.GetProperty("mediaList").EnumerateArray().Select(ParseListEntry)
                        .Where(entry => entry.Kind == type && observed.Add(entry.MediaId)).ToArray();
                    if (entries.Length == 0) break;
                    ct.ThrowIfCancellationRequested();
                    await store.ImportAniListPageAsync(viewerId, entries, ct);
                    imported += entries.Length;
                    if (!list.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()) break;
                }
            }
            return imported;
        }, token);

    public static (double Maximum, double Step) ScoreScale(string format) => format switch
    {
        "POINT_3" => (3, 1), "POINT_5" => (5, 1), "POINT_10" => (10, 1),
        "POINT_10_DECIMAL" => (10, 0.1), "POINT_100" => (100, 1),
        _ => throw new InvalidOperationException("Unknown AniList score format.")
    };

    public Task UpdateFieldAsync(int viewerId, int mediaId, string field, string value, CancellationToken token = default) =>
        WithCredentialAsync(async (credential, ct) =>
        {
            if (credential.ViewerId != viewerId || mediaId <= 0) throw new AniListAuthorizationException();
            var graphType = field switch
            {
                "status" => "MediaListStatus", "score" => "Float", "progress" => "Int",
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            object typedValue = field switch
            {
                "score" => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                "progress" => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                _ => value
            };
            var mutation = $"mutation ($id: Int!, $value: {graphType}!) {{ SaveMediaListEntry(mediaId: $id, {field}: $value) {{ mediaId }} }}";
            var data = await SendAsync(credential, mutation, new { id = mediaId, value = typedValue }, ct);
            return data.GetProperty("SaveMediaListEntry").GetProperty("mediaId").GetInt32() == mediaId
                ? true : throw new InvalidOperationException("AniList did not acknowledge this edit.");
        }, token);

    private static AniListListEntry ParseListEntry(JsonElement entry)
    {
        var media = entry.GetProperty("media");
        var score = entry.GetProperty("score");
        return new(entry.GetProperty("mediaId").GetInt32(), media.GetProperty("type").GetString()!,
            media.GetProperty("title").GetProperty("userPreferred").GetString()!,
            AddonResources.Field(media.GetProperty("coverImage"), "large"), entry.GetProperty("status").GetString()!,
            entry.GetProperty("progress").GetInt32(), score.ValueKind == JsonValueKind.Number && score.GetDouble() > 0 ? score.GetDouble() : null);
    }
}
