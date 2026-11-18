using AniKatha.Core;
using System.Net;
using System.Text;
// Offline integration checks: real SQLite persistence and captured upstream responses.
var folder=Path.Combine(Path.GetTempPath(),"anikatha-check-"+Guid.NewGuid());Directory.CreateDirectory(folder);
var path=Path.Combine(folder,"checks.db3");
var store=new LocalStore(path);
await Task.WhenAll(Enumerable.Range(1,8).Select(i=>store.SaveAsync(new SavedTitle {Id=i,Title=$"Story {i}",Kind=i%2==0?"MANGA":"ANIME",Progress=i})));
var reopened=new LocalStore(path);
Assert((await reopened.GetLibraryAsync()).Count==8,"Library must persist after reopening");
var saved=(await reopened.GetLibraryAsync()).First(x=>x.Id==1);saved.Progress=7;await reopened.SaveAsync(saved);
Assert((await store.GetLibraryAsync()).First(x=>x.Id==1).Progress==7,"Progress must persist");
await store.RemoveAsync(1);Assert((await store.GetLibraryAsync()).Count==7,"Removal must persist");
await store.SetSettingAsync("accent","#ffa3c7");Assert(await reopened.GetSettingAsync("accent")=="#ffa3c7","Theme must persist");
Assert(await store.GetPlaybackPositionAsync(PlaybackSample.EpisodeKey)==0, "New episodes must start from the beginning");
await store.SavePlaybackPositionAsync(PlaybackSample.EpisodeKey, 12.75);
Assert(await reopened.GetPlaybackPositionAsync(PlaybackSample.EpisodeKey)==12.75, "Episode position must persist after reopening");
Assert(await reopened.GetPlaybackPositionAsync("other-episode")==0, "Episode positions must stay separate");
await reopened.SavePlaybackPositionAsync(PlaybackSample.EpisodeKey, 0);
Assert(await store.GetPlaybackPositionAsync(PlaybackSample.EpisodeKey)==0, "A completed episode must start over");
const string chapterKey = "sample-manga:chapter-1";
Assert(await store.GetReadingPageAsync(chapterKey)==1, "New chapters must open at page one");
await store.SaveReadingPageAsync(chapterKey, 3);
Assert(await reopened.GetReadingPageAsync(chapterKey)==3, "Reading position must persist after reopening");
Assert(await reopened.GetReadingPageAsync("sample-manga:chapter-2")==1, "Reading position must be isolated by chapter");
await reopened.SaveReadingPageAsync(chapterKey, 2);
Assert(await store.GetReadingPageAsync(chapterKey)==2, "Turning backwards must update the resume page");
await store.SetSettingAsync($"reading:{chapterKey}", "invalid");
Assert(await reopened.GetReadingPageAsync(chapterKey)==1, "Invalid saved pages must fall back to page one");
await store.SaveAddonAsync(new(){Url="https://example.com/manifest.json",Name="Example",Version="1.0.0",CatalogueProvider=JikanCatalogue.Provider});
Assert((await reopened.GetAddonsAsync()).Single().CatalogueProvider==JikanCatalogue.Provider,"Add-on must persist");await store.RemoveAddonAsync("https://example.com/manifest.json");Assert((await reopened.GetAddonsAsync()).Count==0,"Add-on removal must persist");
foreach(var invalid in new[]{"javascript:alert(1)","http://example.com/manifest.json","https://user:password@example.com/manifest.json","https://localhost/manifest.json","https://127.0.0.1/manifest.json","https://example.com/manifest.json?token=secret"}) {
 try {AddonClient.ValidateUrl(invalid);throw new Exception("Unsafe URL accepted: "+invalid);}catch(InvalidOperationException){}
}
const string youtubeUrl = "https://example.com/youtube/manifest.json";
const string episodeJson = """[{"aniListId":139274,"series":"The Witch from Mercury","episode":1,"title":"The Witch and the Bride","videoId":"2B_njMCRCvU"}]""";
await store.SaveAddonAsync(new() { Url = youtubeUrl, Name = "Gundam on YouTube", Version = "1.0.0", YouTubeEpisodesJson = episodeJson });
var episode = (await reopened.GetAddonsAsync()).Single().GetYouTubeEpisodes().Single();
Assert(episode.AniListId == 139274 && episode.Number == 1 && episode.WatchUrl == "https://www.youtube.com/watch?v=2B_njMCRCvU",
 "Installed YouTube episode must keep the correct series, episode number and video");
await store.SavePlaybackPositionAsync(episode.PlaybackKey, 50.5);
Assert(await reopened.GetPlaybackPositionAsync(episode.PlaybackKey) == 50.5 &&
 await reopened.GetPlaybackPositionAsync(PlaybackSample.EpisodeKey) == 0, "YouTube resume must be isolated by video");
var rejected = new InstalledAddon { YouTubeEpisodesJson = episodeJson.Replace("2B_njMCRCvU", "https://example.com/video") };
try { _ = rejected.GetYouTubeEpisodes(); throw new Exception("A URL was accepted as a YouTube video ID"); }
catch (InvalidOperationException) { }
await store.RemoveAddonAsync(youtubeUrl);
Assert((await reopened.GetAddonsAsync()).Count == 0, "Removing an add-on must remove the episode mapping");
var rawManifest = "https://raw.githubusercontent.com/LMS147/AniKatha/main/examples/addons/anime/manifest.json";
Assert(AddonClient.ValidateUrl(rawManifest).AbsoluteUri == rawManifest, "Raw manifest URLs must continue to work");
Assert(AddonClient.ValidateUrl(" https://github.com/LMS147/AniKatha/blob/main/examples/addons/anime/manifest.json ").AbsoluteUri == rawManifest,
 "GitHub file-page links must resolve to the raw manifest");
Assert(AddonClient.ValidateUrl("stremio://example.com/manifest.json").AbsoluteUri == "https://example.com/manifest.json",
 "Add-on install links must resolve to HTTPS");
foreach (var invalid in new[] {"stremio://localhost/manifest.json", "stremio://user@example.com/manifest.json", "stremio://example.com/manifest.json?token=secret"})
{
 try { AddonClient.ValidateUrl(invalid); throw new Exception("Unsafe install link accepted"); }
 catch (InvalidOperationException) {}
}
var handler=new StubHandler();var api=new AniListClient(new HttpClient(handler));
var result=await api.SearchAsync(new("MANGA","Story",Year:2020));
Assert(result.Items.Count==1&&result.Items[0].Title=="Story"&&result.Items[0].Description==" A & B "&&result.Items[0].Year is null,"Nullable metadata and descriptions must parse safely");
Assert(handler.Body!.Contains("MANGA")&&handler.Body.Contains("Story"),"Search variables must reach upstream");
handler.Response="{\"errors\":[{\"message\":\"temporarily unavailable\"}]}";
try{await api.SearchAsync(new());throw new Exception("GraphQL errors must not look like empty results");}catch(InvalidOperationException){}
handler.Status=HttpStatusCode.TooManyRequests;
try{await api.SearchAsync(new());throw new Exception("Rate limit must surface");}catch(InvalidOperationException){}
var jikanHandler = new JikanStub();
using var jikanHttp = new HttpClient(jikanHandler);
var jikan = new JikanCatalogue(jikanHttp);
var animePage = await jikan.SearchAsync("ANIME", null, 1, default);
Assert(animePage.HasNextPage && animePage.Items.Single().Poster is not null &&
 jikanHandler.LastUri!.AbsolutePath == "/v4/anime" && jikanHandler.LastUri.Query.Contains("sfw=true"), "Live add-on must map anime title and cover data");
var mangaPage = await jikan.SearchAsync("MANGA", "Yotsuba&!", 2, default);
Assert(mangaPage.Items.Single().Name == "Sample title" &&
 jikanHandler.LastUri!.AbsolutePath == "/v4/manga" && jikanHandler.LastUri.Query.Contains("page=2") &&
 jikanHandler.LastUri.Query.Contains("Yotsuba%26"), "Manga search must use its own endpoint and escape search terms");
jikanHandler.Status = HttpStatusCode.ServiceUnavailable;
var starter = await jikan.SearchAsync("ANIME", "bebop", 1, default);
Assert(starter.IsStarterCatalogue && !starter.HasNextPage && starter.Items.Single().Name == "Cowboy Bebop",
 "An unavailable live catalogue must return a labeled, searchable starter list");
var noMatch = await jikan.SearchAsync("MANGA", "Cowboy Bebop", 1, default);
Assert(noMatch.IsStarterCatalogue && noMatch.Items.Count == 0, "Starter search must respect the active media type");
jikanHandler.Status = HttpStatusCode.TooManyRequests;
try { await jikan.SearchAsync("ANIME", null, 1, default); throw new Exception("Missing Jikan rate limit error"); }
catch (InvalidOperationException ex) { Assert(ex.Message.Contains("too many requests"), "Jikan rate limits must be retryable"); }
Console.WriteLine("PASS: SQLite persistence, progress, settings, add-ons, URL validation, metadata parsing, GraphQL errors, and rate limiting.");
static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
sealed class StubHandler:HttpMessageHandler {
 public string? Body;public HttpStatusCode Status=HttpStatusCode.OK;
 public string Response="""{"data":{"Page":{"pageInfo":{"hasNextPage":false},"media":[{"id":42,"type":"MANGA","title":{"userPreferred":"Story"},"coverImage":{"large":null},"bannerImage":null,"description":"<b>A &amp; B</b>","startDate":{"year":null},"format":"MANGA","averageScore":null,"episodes":null,"chapters":null,"genres":["Drama"]}]}}}""";
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){Body=await request.Content!.ReadAsStringAsync(cancellationToken);return new(Status){Content=new StringContent(Response,Encoding.UTF8,"application/json")};}
}


sealed class JikanStub : HttpMessageHandler
{
 public HttpStatusCode Status = HttpStatusCode.OK;
 public Uri? LastUri;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
 {
  LastUri = request.RequestUri;
  const string response = """
   {"pagination":{"has_next_page":true},"data":[{"mal_id":14,"title":"Sample title","title_english":null,"type":"TV",
    "images":{"jpg":{"image_url":"https://cdn.myanimelist.net/images/anime/example.jpg"}},"synopsis":"Sample synopsis"}]}
   """;
  return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(response) });
 }
}
