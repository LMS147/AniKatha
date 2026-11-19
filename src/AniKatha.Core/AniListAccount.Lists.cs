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
