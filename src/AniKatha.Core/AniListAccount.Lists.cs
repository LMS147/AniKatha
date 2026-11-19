using System.Text.Json;

namespace AniKatha.Core;

public sealed record AniListListEntry(int MediaId, string Kind, string Title, string? Poster, string Status, int Progress, double? Score, string ScoreFormat = "POINT_100", int? Total = null);

public sealed partial class AniListAccount
{
    public Task<int> ImportAsync(LocalStore store, int viewerId, CancellationToken token = default) =>
        WithCredentialAsync(async (credential, ct) =>
        {
            if (credential.ViewerId != viewerId) throw new AniListAuthorizationException();
            var viewer = await ViewerAsync(credential, ct);
            if (viewer.Id != viewerId) throw new AniListAuthorizationException();
            await UpdateStoredViewerAsync(credential, viewer, ct);
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
                              media { id type episodes chapters title { userPreferred } coverImage { large } }
                            }
                          }
                        }
                        """;
                    var data = await SendAsync(credential, query, new
                    {
                        viewerId, kind = type, page, scoreFormat = viewer.ScoreFormat
                    }, ct);
                    var list = data.GetProperty("Page");
                    var entries = list.GetProperty("mediaList").EnumerateArray().Select(entry => ParseListEntry(entry, viewer.ScoreFormat))
                        .Where(entry => entry.Kind == type && observed.Add(entry.MediaId)).ToArray();
                    if (entries.Length == 0) break;
                    ct.ThrowIfCancellationRequested();
                    await store.ImportAniListPageAsync(viewerId, entries, ct);
                    imported += entries.Length;
                    if (!list.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()) break;
                }
            }
            await store.RecordAniListImportAsync(viewerId, imported);
            return imported;
        }, token);

    public static (double Maximum, double Step) ScoreScale(string format) => format switch
    {
        "POINT_3" => (3, 1), "POINT_5" => (5, 1), "POINT_10" => (10, 1),
        "POINT_10_DECIMAL" => (10, 0.1), "POINT_100" => (100, 1),
        _ => throw new InvalidOperationException("Unknown AniList score format.")
    };

    public Task UpdateFieldAsync(int viewerId, int mediaId, string field, string value, CancellationToken token = default) =>
        UpdateFieldAsync(viewerId, mediaId, field, value, null, token);

    private Task<int?> UpdateFieldAsync(int viewerId, int mediaId, string field, string value, int? expectedProgress,
        CancellationToken token, Func<Task<bool>>? stillCurrent = null) =>
        WithCredentialAsync<int?>(async (credential, ct) =>
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
            var match = await SendAsync(credential,
                "query ($id: Int!) { Media(id: $id) { id mediaListEntry { id progress } } }", new { id = mediaId }, ct);
            var media = match.GetProperty("Media");
            if (media.GetProperty("id").GetInt32() != mediaId)
                throw new InvalidOperationException("AniList returned an unexpected title.");
            var entry = media.GetProperty("mediaListEntry");
            var entryId = entry.ValueKind == JsonValueKind.Object ? (int?)entry.GetProperty("id").GetInt32() : null;
            if (expectedProgress is int floor)
            {
                if (entryId is null)
                    throw new InvalidOperationException("The tracked title is no longer in your AniList library.");
                var current = entry.GetProperty("progress").GetInt32();
                if (current < floor || current >= (int)typedValue) return current;
            }
            if (stillCurrent is not null && !await stillCurrent()) return null;
            var mutation = $"mutation ($entryId: Int, $id: Int!, $value: {graphType}!) {{ SaveMediaListEntry(id: $entryId, mediaId: $id, {field}: $value) {{ mediaId }} }}";
            var data = await SendAsync(credential, mutation, new { entryId, id = mediaId, value = typedValue }, ct);
            return data.GetProperty("SaveMediaListEntry").GetProperty("mediaId").GetInt32() == mediaId
                ? null : throw new InvalidOperationException("AniList did not acknowledge this edit.");
        }, token);

    private static AniListListEntry ParseListEntry(JsonElement entry, string scoreFormat)
    {
        var media = entry.GetProperty("media");
        var score = entry.GetProperty("score");
        var kind = media.GetProperty("type").GetString()!;
        var total = media.TryGetProperty(kind == "MANGA" ? "chapters" : "episodes", out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count > 0 ? (int?)count : null;
        return new(entry.GetProperty("mediaId").GetInt32(), kind,
            media.GetProperty("title").GetProperty("userPreferred").GetString()!,
            AddonResources.Field(media.GetProperty("coverImage"), "large"), entry.GetProperty("status").GetString()!,
            entry.GetProperty("progress").GetInt32(), score.ValueKind == JsonValueKind.Number && score.GetDouble() > 0 ? score.GetDouble() : null, scoreFormat, total);
    }
}
