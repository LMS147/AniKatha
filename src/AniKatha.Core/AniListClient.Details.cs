using System.Text.Json;

namespace AniKatha.Core;

public sealed partial class AniListClient
{
    public async Task<Media?> FindTitleAsync(int id, string kind, CancellationToken token = default)
    {
        const string query = """
            query ($id: Int, $type: MediaType) {
              Media(id: $id, type: $type, isAdult: false) {
                id type title { userPreferred native romaji } coverImage { large } bannerImage description(asHtml: false)
                startDate { year } format averageScore episodes chapters genres status countryOfOrigin popularity tags { name isMediaSpoiler isGeneralSpoiler }
              }
            }
            """;
        using var result = await HomeQuery(query, new { id, type = kind }, token);
        return result.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("Media", out var media) && media.ValueKind == JsonValueKind.Object
            ? ReadMedia(media) with
            {
                Status = Text(media, "status"),
                Country = Text(media, "countryOfOrigin"),
                Popularity = Number(media, "popularity"),
                AlternateTitle = Text(media.GetProperty("title"), "native") ?? Text(media.GetProperty("title"), "romaji"),
                Tags = AddonResources.Items(media, "tags")
                    .Where(tag => tag.TryGetProperty("isMediaSpoiler", out var spoiler) && spoiler.ValueKind == JsonValueKind.False &&
                        tag.TryGetProperty("isGeneralSpoiler", out var general) && general.ValueKind == JsonValueKind.False)
                    .Select(tag => Text(tag, "name")).OfType<string>().Take(12).ToArray()
            } : null;
    }
}
