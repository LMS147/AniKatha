using System.Text.Json;

namespace AniKatha.Core;

public sealed partial class AniListClient
{
    public async Task<Media?> FindMangaAsync(int id, CancellationToken token = default)
    {
        const string query = """
            query ($id: Int) {
              Media(id: $id, type: MANGA, isAdult: false) {
                id type title { userPreferred } coverImage { large } bannerImage description(asHtml: false)
                startDate { year } format averageScore episodes chapters genres
              }
            }
            """;
        using var result = await HomeQuery(query, new { id }, token);
        return result.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("Media", out var media) && media.ValueKind == JsonValueKind.Object
            ? ReadMedia(media) : null;
    }
}
