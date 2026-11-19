using System.Text.Json;
using SQLite;
namespace AniKatha.Core;
public sealed record Media(int Id, string Kind, string Title, string? Poster, string? Banner, string? Description, int? Year, string? Format, int? Score, int? Episodes, int? Chapters, string[] Genres);
public sealed record SearchOptions(string Kind = "ANIME", string? Search = null, string? Genre = null, int? Year = null, string? Season = null, string? Format = null, string Sort = "TRENDING_DESC", int Page = 1);
public sealed record MediaPage(List<Media> Items, bool HasNextPage);
public sealed class SavedTitle
{
 [PrimaryKey] public int Id { get; set; }
 public string Kind { get; set; } = "ANIME";
 public string Title { get; set; } = "";
 public string? Poster { get; set; }
 public bool IsFavourite { get; set; }
 public string? ProviderKey { get; set; }
 public string? ExternalId { get; set; }
 public string? ResourceType { get; set; }
 public string Status { get; set; } = "PLANNING";
 public int Progress { get; set; }
 public DateTime UpdatedAt { get; set; }
}
public sealed class InstalledAddon
{
 [PrimaryKey] public string Url { get; set; } = "";
 public string Name { get; set; } = "";
 public string Version { get; set; } = "";
 public string? ManifestJson { get; set; }
 public string CapabilitySummary()
 {
  if (ManifestJson == null) return "Refresh to load capabilities";
  using var document = JsonDocument.Parse(ManifestJson);
  var root = document.RootElement;
  var types = root.GetProperty("types").EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString());
  var resources = root.GetProperty("resources").EnumerateArray().Select(x=>
   x.ValueKind==JsonValueKind.String ? x.GetString() :
   x.ValueKind==JsonValueKind.Object && x.TryGetProperty("name",out var name) && name.ValueKind==JsonValueKind.String ? name.GetString() : null);
  return string.Join(" · ", types.Concat(resources).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct());
 }
 public bool Disabled { get; set; }
 public int SortOrder { get; set; }
 public string? CatalogueProvider { get; set; }
 public string? YouTubeEpisodesJson { get; set; }
 public IReadOnlyList<YouTubeEpisode> GetYouTubeEpisodes()
 {
  if (YouTubeEpisodesJson is null)
   return [];
  using var document = JsonDocument.Parse(YouTubeEpisodesJson);
  return YouTubeEpisode.Read(document.RootElement);
 }
 public string Description { get; set; } = "";
}
public sealed class AppSetting
{
 [PrimaryKey] public string Key { get; set; } = "";
 public string Value { get; set; } = "";
}

public sealed class ResumeEntry
{
 [PrimaryKey] public string Id { get; set; } = "";
 public string Origin { get; set; } = "";
 public string Source { get; set; } = "";
 public string Type { get; set; } = "";
 public string TitleId { get; set; } = "";
 public string UnitId { get; set; } = "";
 public string Title { get; set; } = "";
 public string Label { get; set; } = "";
 public string? Poster { get; set; }
 public DateTime Opened { get; set; }
 public static string Key(string origin,string type,string titleId,string unitId,string source) =>
  string.Join("/",origin,type,Uri.EscapeDataString(titleId),Uri.EscapeDataString(unitId),source);
}

public sealed record EpisodeSubtitle(string Language, string Url, string Label = "", string Role = "");
