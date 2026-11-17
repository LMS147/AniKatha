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
 public string Status { get; set; } = "PLANNING";
 public int Progress { get; set; }
 public DateTime UpdatedAt { get; set; }
}
public sealed class InstalledAddon
{
 [PrimaryKey] public string Url { get; set; } = "";
 public string Name { get; set; } = "";
 public string Version { get; set; } = "";
 public string Description { get; set; } = "";
}
public sealed class AppSetting
{
 [PrimaryKey] public string Key { get; set; } = "";
 public string Value { get; set; } = "";
}
