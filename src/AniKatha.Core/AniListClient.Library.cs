namespace AniKatha.Core;

public sealed record LibraryTitleMetadata(int Id, string Kind, string? Format, int? Year, int? Units,
    string[] Genres, string[] Studios, string? Source);

public sealed partial class AniListClient
{
    public async Task<IReadOnlyList<LibraryTitleMetadata>> LibraryMetadataAsync(int[] ids, string kind,
        CancellationToken token)
    {
        const string query = """
            query LibraryMetadata($ids: [Int], $kind: MediaType) {
              Page(perPage: 50) {
                media(id_in: $ids, type: $kind) {
                  id type format startDate { year } episodes chapters genres source
                  studios(isMain: true) { nodes { name } }
                }
              }
            }
            """;
        using var response = await HomeQuery(query, new { ids, kind }, token);
        var media = response.RootElement.GetProperty("data").GetProperty("Page");
        var result = new List<LibraryTitleMetadata>();
        foreach (var item in AddonResources.Items(media, "media"))
        {
            if (Number(item, "id") is not int id || !ids.Contains(id) || Text(item, "type") != kind) continue;
            var year = item.TryGetProperty("startDate", out var date) &&
                date.ValueKind == System.Text.Json.JsonValueKind.Object ? Number(date, "year") : null;
            var genres = AddonResources.Items(item, "genres")
                .Where(genre => genre.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(genre => genre.GetString()).OfType<string>().ToArray();
            var studios = item.TryGetProperty("studios", out var studio)
                ? AddonResources.Items(studio, "nodes").Select(node => Text(node, "name")).OfType<string>().ToArray()
                : [];
            result.Add(new(id, kind, Text(item, "format"), year, Number(item, kind == "MANGA" ? "chapters" : "episodes"),
                genres, studios, Text(item, "source")));
        }
        return result;
    }
}
