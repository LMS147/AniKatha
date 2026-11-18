using System.Text.Json;

namespace AniKatha.Core;

public sealed record YouTubeEpisode(int AniListId, string Series, int Number, string Title, string VideoId)
{
 public string PlaybackKey => $"youtube:{VideoId}";
 public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";

 public static bool IsVideoId(string? value) => value is { Length: 11 }
  && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

 internal static IReadOnlyList<YouTubeEpisode> Read(JsonElement entries)
 {
  if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() is < 1 or > 200)
   throw new InvalidOperationException("A YouTube add-on must list 1 to 200 episodes.");

  List<YouTubeEpisode> result = [];
  HashSet<(int, int, string)> seen = [];
  foreach (var entry in entries.EnumerateArray())
  {
   if (entry.ValueKind != JsonValueKind.Object)
    throw new InvalidOperationException("Each YouTube episode must be an object.");

   var mediaId = PositiveInt(entry, "aniListId");
   var episode = PositiveInt(entry, "episode");
   var video = RequiredText(entry, "videoId");
   if (!IsVideoId(video))
    throw new InvalidOperationException("Each episode must use an 11-character YouTube video ID.");
   if (!seen.Add((mediaId, episode, video)))
    throw new InvalidOperationException("A YouTube episode is listed more than once.");

   result.Add(new(mediaId, RequiredText(entry, "series"), episode, RequiredText(entry, "title"), video));
  }
  return result;
 }

 static string RequiredText(JsonElement entry, string key)
 {
  var text = entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
   ? value.GetString()?.Trim() : null;
  if (string.IsNullOrEmpty(text) || text.Length > 180)
   throw new InvalidOperationException($"Every YouTube episode needs a valid {key}.");
  return text;
 }

 static int PositiveInt(JsonElement entry, string key)
 {
  if (entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
   && value.TryGetInt32(out var number) && number > 0)
   return number;
  throw new InvalidOperationException($"Every YouTube episode needs a positive integer {key}.");
 }
}

public interface IYouTubePlayer
{
 Task OpenAsync(YouTubeEpisode episode);
}
