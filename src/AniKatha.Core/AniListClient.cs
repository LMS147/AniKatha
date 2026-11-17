using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AniKatha.Core;
public sealed class AniListClient(HttpClient http)
{
 public const string Endpoint = "https://graphql.anilist.co";
 public async Task<MediaPage> SearchAsync(SearchOptions options, CancellationToken token = default)
 {
  const string query = """
  query ($type: MediaType, $search: String, $genre: String, $start: FuzzyDateInt, $end: FuzzyDateInt, $season: MediaSeason, $format: MediaFormat, $sort: [MediaSort], $page: Int) {
   Page(page: $page, perPage: 24) {
    pageInfo { hasNextPage }
    media(type: $type, search: $search, genre: $genre, startDate_greater: $start, startDate_lesser: $end, season: $season, format: $format, sort: $sort, isAdult: false) {
     id type title { userPreferred } coverImage { large } bannerImage description(asHtml: false)
     startDate { year } format averageScore episodes chapters genres
    }
   }
  }
  """;
  using var response = await http.PostAsJsonAsync(Endpoint, new { query, variables = new {
   type = options.Kind, search = Empty(options.Search), genre = Empty(options.Genre), start = options.Year is int y ? (int?)(y * 10000) : null, end = options.Year is int z ? (int?)((z + 1) * 10000) : null,
   season = options.Kind == "ANIME" ? Empty(options.Season) : null, format = Empty(options.Format),
   sort = new[] { options.Sort }, page = options.Page
  } }, token);
  if (response.StatusCode == HttpStatusCode.TooManyRequests)
   throw new InvalidOperationException("AniList is busy. Wait a minute before trying again.");
  response.EnsureSuccessStatusCode();
  using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
  if (doc.RootElement.TryGetProperty("errors", out _))
   throw new InvalidOperationException("AniList could not complete this request. Try again shortly.");
  var page = doc.RootElement.GetProperty("data").GetProperty("Page");
  var items = page.GetProperty("media").EnumerateArray().Select(m => new Media(
   m.GetProperty("id").GetInt32(), Text(m,"type")!, Text(m.GetProperty("title"),"userPreferred")!,
   Text(m.GetProperty("coverImage"),"large"), Text(m,"bannerImage"), Clean(Text(m,"description")),
   Number(m.GetProperty("startDate"),"year"), Text(m,"format"), Number(m,"averageScore"),
   Number(m,"episodes"), Number(m,"chapters"), m.GetProperty("genres").EnumerateArray().Select(x=>x.GetString()!).ToArray()
  )).ToList();
  return new(items, page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
 }
 static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
 static string? Text(JsonElement value, string key) => value.TryGetProperty(key,out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
 static int? Number(JsonElement value, string key) => value.TryGetProperty(key,out var p) && p.TryGetInt32Safe(out var n) ? n : null;
 static string? Clean(string? value) => value is null ? null : WebUtility.HtmlDecode(Regex.Replace(value,"<[^>]+>"," "));
}
internal static class JsonNumbers
{
 public static bool TryGetInt32Safe(this JsonElement value, out int number) { number=0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number); }
}
