using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using AniKatha.UI.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;

var database = Path.Combine(Path.GetTempPath(), $"anikatha-ui-{Guid.NewGuid()}.db3");
var store = new LocalStore(database);
var handler=new FixtureHandler();
using var http = new HttpClient(handler);
var client = new AddonClient(http, (_,_) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.5") }));
var tracking = new TrackingFixture();
using var trackingHttp = new HttpClient(tracking);
await using var context = new BunitContext();
context.Services.AddSingleton(store);
context.Services.AddCascadingValue(_ => new SourceResultCache());
context.Services.AddSingleton(client);
context.Services.AddSingleton(new AniListAccount(trackingHttp, tracking));
context.JSInterop.Mode = JSRuntimeMode.Loose;

var profile = context.Render<Sources>();
void Click(Bunit.IRenderedComponent<Sources> view, string label) => view.FindAll("button").Single(x=>x.TextContent.Trim()==label).Click();
await profile.InvokeAsync(()=>profile.Find("input[inputmode=url]").Input("https://fiction.example/books?config=test"));
await profile.InvokeAsync(()=>profile.Find("form").SubmitAsync());
profile.WaitForAssertion(() => Check(profile.Find("[aria-label='Source preview']").TextContent.Contains("Chapter pages"), "Preview real capabilities"));
Check((await store.GetAddonsAsync()).Count == 0, "Preview does not install");
await ClickAsync(profile, "Add source");
profile.WaitForAssertion(()=>Check(profile.Find(".addon-row strong").TextContent=="Fixture","Install source"));
var sources=(await new LocalStore(database).GetAddonsAsync()).Select(x=>new AddonResources(x)).ToList();
var provider=sources.Single();
var manga=context.Render<ManifestCatalog>(p=>p.Add(x=>x.Source,provider).Add(x=>x.Catalog,provider.Catalogs("MANGA").Single()).Add(x=>x.Providers,sources));
Task Manga(string label) => ClickAsync(manga, label);
manga.WaitForAssertion(()=>Check(manga.Markup.Contains("Manga Story"),"Manga catalogue"));
await Manga("Load more");
manga.WaitForAssertion(()=>Check(manga.Markup.Contains("Second title"),"Append next page"));
await Manga("Load more");
manga.WaitForAssertion(()=>Check(!manga.Markup.Contains("Load more"),"Repeated page stops"));
await Manga("Manga Story");
manga.WaitForAssertion(()=>Check(manga.Markup.Contains("Under the sea") && manga.Markup.Contains("Release status"),"Optional manga detail metadata"));
Check(!manga.Markup.Contains("0 chapters"),"Unknown count stays unknown");
await manga.Find(".design-unit-tools input").InputAsync("7.5");
Check(manga.FindAll(".design-unit-row").Single().TextContent.Contains("Chapter 7.5"),
 "Chapter text search keeps the original release button");
await manga.Find(".design-unit-tools input").InputAsync("");
await Manga("Chapter 7"); await Manga("Read Chapter 7");
manga.WaitForAssertion(()=>Check(manga.Find(".reader-page").GetAttribute("src")=="https://images.example/1.webp","Resolved chapter pages"));
await Manga("Next page →");
manga.WaitForAssertion(()=>Check(manga.Find(".reader-page").GetAttribute("src")=="https://images.example/2.webp","Turn page"));
manga.WaitForAssertion(()=>Check(manga.Markup.Contains("Translation"),"Source language rendered"));
manga.Dispose();
Click(profile,"Disable");
profile.WaitForAssertion(()=>Check(profile.FindAll("button").Any(x=>x.TextContent=="Enable"),"Disable"));
Check((await store.GetAddonsAsync()).Count==0,"No active source");
Click(profile,"Enable");
profile.WaitForAssertion(()=>Check(profile.FindAll("button").Any(x=>x.TextContent=="Disable"),"Re-enable"));
var reopened=context.Render<ManifestCatalog>(p=>p.Add(x=>x.Source,provider).Add(x=>x.Catalog,provider.Catalogs("MANGA").Single()).Add(x=>x.Providers,sources));
foreach(var name in new[]{"Manga Story","Chapter 7","Read Chapter 7"})
 await ClickAsync(reopened, name);
reopened.WaitForAssertion(()=>Check(reopened.Find(".reader-page").GetAttribute("src")=="https://images.example/2.webp","Chapter/page resume"));
var other=new InstalledAddon{Url=FixtureHandler.OtherUrl, Name="Second source",
 ManifestJson="""{"types":["anime"],"resources":[{"name":"stream","idPrefixes":["alternate-"]}]}"""};
sources.Add(new AddonResources(other));
await store.SaveAddonAsync(other);
await store.SetSettingAsync(MediaPreferences.AutoplayKey, "true");
await store.SetSettingAsync(MediaPreferences.PreferDubKey, "true");
await store.SetSettingAsync(MediaPreferences.DimWatchedKey, "true");
var anime=context.Render<ManifestCatalog>(p=>p.Add(x=>x.Source,provider).Add(x=>x.Catalog,provider.Catalogs("ANIME").Single()).Add(x=>x.Providers,sources));
foreach(var name in new[]{"Anime Story","Episode 9","Stream 9"})
 await ClickAsync(anime, name);
anime.WaitForAssertion(()=>Check(anime.FindComponent<WatchPreview>().Instance.VideoUrl=="https://video.example/9.mp4","Selected episode reaches existing player"));
Check(!anime.Markup.Contains("Manga Story"),"No manga in anime catalogue");
Check(anime.Markup.IndexOf("English Dub", StringComparison.Ordinal) < anime.Markup.IndexOf("Stream 9", StringComparison.Ordinal),
 "Explicitly dubbed streams sort first while the original stream is selectable");
Check(anime.Markup.Contains("Loading sources"),"Successful provider rendered while second provider pending");
Check(handler.SlowRequest?.EndsWith("/alternate-09.json")==true,"Mapped provider got its own unit ID");
var preferred = new AddonResources(other).ProviderKey;
await anime.FindAll("select").Single(x => x.TextContent.Contains("No preference")).ChangeAsync(new ChangeEventArgs { Value = preferred });
handler.FailSlowSource.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
anime.WaitForAssertion(()=>Check(anime.Markup.Contains("Source request failed") && anime.FindComponent<WatchPreview>()!=null,"Failed provider isolated"));
await anime.InvokeAsync(()=>anime.FindAll("button").Single(x=>x.TextContent.Trim()=="Save to library").ClickAsync(new()));
var saved=(await store.GetLibraryAsync()).Single();
var successfulRequests = handler.SuccessfulSourceRequests;
var fromLibrary = context.Render<LibraryProviderTitle>(p => p.Add(x => x.Id, saved.Id));
fromLibrary.WaitForAssertion(()=>Check(fromLibrary.Markup.Contains("Episode 9"),"Open provider details from Library"));
await ClickAsync(fromLibrary, "Episode 9");
fromLibrary.WaitForAssertion(()=>Check(fromLibrary.Markup.Contains("Preferred") && fromLibrary.Markup.Contains("1080p"),"Preferred source and quality restored"));
Check(handler.SuccessfulSourceRequests==successfulRequests,"Recent source result reused");
await ClickAsync(fromLibrary, "Refresh sources");
fromLibrary.WaitForAssertion(()=>Check(handler.SuccessfulSourceRequests==successfulRequests+1,"Refresh bypassed cached source"));
Check(fromLibrary.Markup.Contains("Preferred") && fromLibrary.Markup.Contains("Source request failed"),"Preferred failure leaves alternative and saved choice intact");
Check(saved.ExternalId=="one" && saved.ResourceType=="anime","HTTP title saved with provider identity");
await store.SaveProviderTitleAsync(sources[1].ProviderKey,"anime","one","Other story");
Check((await store.GetLibraryAsync()).Count==2,"Identical local IDs across providers stay distinct");
var shelf=context.Render<Library>();
shelf.WaitForAssertion(()=>Check(shelf.FindAll(".library-item").Count==2,"Saved records reach Library"));
await shelf.InvokeAsync(()=>shelf.FindAll("button").First(x=>x.TextContent=="Add to favourites").ClickAsync(new()));
shelf.FindAll("button").Single(x=>x.TextContent=="Favourites").Click();
Check(shelf.FindAll(".library-item").Count==1,"Favourite filter");
await ClickAsync(shelf, "Stats");
Check(shelf.Find(".design-stat-card h2").TextContent == "Anime Stats", "Library switches to anime stats from saved data");
await ClickAsync(shelf, "Manga");
Check(shelf.Find(".design-stat-card h2").TextContent == "Manga Stats", "Library switches the statistics media type");
await ClickAsync(shelf, "Anime List");
Check(shelf.FindAll(".library-item").Count == 2, "Anime List displays saved anime");
await shelf.Find(".design-library-controls select").ChangeAsync("COMPLETED");
Check(shelf.FindAll(".library-item").Count == 0, "Library status filter changes the listed titles");
var record=(await store.HistoryAsync()).Single(x=>x.Type=="manga");
var resume=context.Render<Resume>(p=>p.Add(x=>x.Id,record.Id));
resume.WaitForAssertion(()=>Check(resume.FindAll("button").Any(x=>x.TextContent.Trim()=="Read Chapter 7"),"Re-resolve chapter on resume"));
await ClickAsync(resume, "Read Chapter 7");
resume.WaitForAssertion(()=>Check(resume.Find(".reader-page").GetAttribute("src")=="https://images.example/2.webp","History restores exact page"));

await store.ImportAniListPageAsync(7,
[
 new(42, "ANIME", "Tracked anime", null, "CURRENT", 8, null, "POINT_10"),
 new(44, "MANGA", "Tracked manga", null, "CURRENT", 6, null, "POINT_10")
]);
tracking.Linked = true;
Check(tracking.Anime == 8 && tracking.Manga == 6 && (await store.AniListEditsAsync(7)).Count == 0,
 "Opening content and saving a page did not queue a completion");
var importedLibrary = context.Render<Library>();
importedLibrary.WaitForAssertion(() => Check(importedLibrary.FindAll("a").Any(a =>
 a.TextContent.Trim() == "Find Sources" && a.GetAttribute("href") == "/library/title/44"), "Imported title exposes Find Sources"));
var importedManga = context.Render<LibraryProviderTitle>(p => p.Add(x => x.Id, 44));
foreach (var name in new[] {"Chapter 7", "Read Chapter 7"})
 await ClickAsync(importedManga, name);
importedManga.WaitForAssertion(() => Check(importedManga.Find(".reader-page").GetAttribute("src") == "https://images.example/2.webp",
 "Imported title opens add-on content at the exact page"));
await ClickAsync(importedManga, "Mark chapter read");
importedManga.WaitForAssertion(() => Check(tracking.Manga == 7 && importedManga.Markup.Contains("AniList progress confirmed"),
 "Imported Library chapter updated AniList"));
var completedReader = context.Render<Resume>(p=>p.Add(x=>x.Id,record.Id));
await ClickAsync(completedReader, "Read Chapter 7");
completedReader.WaitForAssertion(() => Check(completedReader.Markup.Contains("Chapter completed"), "Completed chapter remains marked on resume"));
var readingKey = ResumeEntry.Key(record.Origin, record.Type, record.TitleId, record.UnitId, record.Source);
Check(await store.IsUnitCompleteAsync(readingKey) && await store.GetReadingPageAsync(readingKey)==2,"Completion preserves precise page");
await ClickAsync(reopened, "Next chapter →");
reopened.WaitForAssertion(()=>Check(reopened.Find(".reader-page").GetAttribute("src")=="https://images.example/special.webp","Decimal chapter follows provider order"));
Check(reopened.FindAll("button").Single(x => x.TextContent.Trim()=="Next chapter →").HasAttribute("disabled"), "Last chapter boundary");
await ClickAsync(reopened, "Mark chapter read");
reopened.WaitForAssertion(() => Check(reopened.Markup.Contains("no verified AniList match") && tracking.Manga == 7,
 "Unmapped completion gives local-only feedback without changing remote count"));
await ClickAsync(reopened, "← Previous chapter");
reopened.WaitForAssertion(()=>Check(reopened.Find(".reader-page").GetAttribute("src")=="https://images.example/2.webp","Previous chapter restores page"));
handler.AlternateOnline = true;
var alternatePanel = anime.FindComponents<ManifestSources>().Single(m => m.Instance.Source.ProviderKey == preferred);
await ClickAsync(alternatePanel, "Retry");
var firstPlayer = anime.FindComponent<WatchPreview>();
var originalKey = firstPlayer.Instance.EpisodeKey;
await firstPlayer.InvokeAsync(() => firstPlayer.Instance.SavePosition(23.375));
await ClickAsync(alternatePanel, "Alternate 9");
var alternatePlayer = anime.FindComponent<WatchPreview>();
Check(anime.FindComponents<WatchPreview>().Count == 1 &&
 alternatePlayer.Instance.VideoUrl == "https://video.example/alternate.mp4", "One active player after source switch");
Check(await store.GetPlaybackPositionAsync(alternatePlayer.Instance.EpisodeKey) == 23.375, "Verified alternate inherits precise timestamp");
Check(!await store.IsUnitCompleteAsync(originalKey) && tracking.Anime == 8, "Switching a partial episode does not complete it");
await ClickAsync(anime, "Stream 9");
firstPlayer = anime.FindComponent<WatchPreview>();
Check(await store.GetPlaybackPositionAsync(firstPlayer.Instance.EpisodeKey) == 23.375, "Switch back retains timestamp");
await firstPlayer.InvokeAsync(() => firstPlayer.Instance.CompleteEpisode());
anime.WaitForAssertion(()=>Check(anime.FindComponent<WatchPreview>().Instance.VideoUrl=="https://video.example/10.mp4","Autoplay opens the next episode source"));
Check(!anime.FindComponent<WatchPreview>().Instance.OnNext.HasDelegate, "Last episode boundary");
anime.WaitForAssertion(() => Check(anime.FindAll(".watched-dimmed").Any(x => x.TextContent.Trim() == "Episode 9"),
 "Completed episode is dimmed using stored completion"));
anime.WaitForAssertion(() => Check(tracking.Anime == 9, "Discovery episode completion updated AniList"));
var importedAnime = context.Render<LibraryProviderTitle>(p => p.Add(x => x.Id, 42));
importedAnime.WaitForAssertion(() => Check(!importedAnime.Markup.Contains("Other season"), "A same-name candidate with another AniList ID stays hidden"));
foreach (var name in new[] {"Episode 10", "Stream 10"})
 await ClickAsync(importedAnime, name);
var importedPlayer = importedAnime.FindComponent<WatchPreview>();
await importedPlayer.InvokeAsync(() => importedPlayer.Instance.CompleteEpisode());
importedAnime.WaitForAssertion(() => Check(tracking.Anime == 10 && importedAnime.Markup.Contains("AniList progress confirmed"),
 "Imported Library episode updated AniList"));
await importedPlayer.InvokeAsync(() => importedPlayer.Instance.CompleteEpisode());
Check(tracking.Anime == 10, "Duplicate completion does not inflate the count");
await ReaderChecks.RunAsync(context, store);
Console.WriteLine("PASS: imported Library source matching, discovery playback, offline-ready mapping and automatic episode and chapter tracking.");


Console.WriteLine("PASS: phase 2 fixture covered metadata, explicit unit mapping, source fallback and preference, cache refresh and chapter completion.");
Console.WriteLine("PASS: install via Sources, type routing, pagination, repeat-page stop, chapter reading, page resume, enable/disable, episode source → player.");

await CatalogChecks.RunAsync(new LocalStore(Path.Combine(Path.GetTempPath(), $"anikatha-catalog-{Guid.NewGuid()}.db3")));
await DownloadChecks.RunAsync();
await PlayerChecks.RunAsync();
await HomeChecks.RunAsync();
await SpotlightChecks.RunAsync();
await ProfileUiChecks.RunAsync();
await TrackingScreenChecks.RunAsync();

static async Task ClickAsync<T>(IRenderedComponent<T> view, string label) where T : IComponent
{
 view.WaitForAssertion(() => Check(view.FindAll("button").Any(x => x.TextContent.Trim() == label), label));
 await view.InvokeAsync(() => view.FindAll("button").Single(x => x.TextContent.Trim() == label).ClickAsync(new()));
}

static void Check(bool valid,string name){if(!valid)throw new Exception(name);}
sealed class FixtureHandler : HttpMessageHandler
{
 internal const string OtherUrl = "https://fiction.example/slow/manifest.json?config=test";
 internal bool AlternateOnline { get; set; }
 internal string? SlowRequest { get; private set; }
 internal int SuccessfulSourceRequests { get; private set; }
 internal TaskCompletionSource<HttpResponseMessage> FailSlowSource {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  var uri=request.RequestUri!;
  if(uri.Query!="?config=test")throw new InvalidOperationException("Configuration lost");
  var path=uri.AbsolutePath;
  if (AlternateOnline && path.EndsWith("/alternate-09.json"))
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent("""{"streams":[{"name":"Alternate 9","url":"https://video.example/alternate.mp4"}]}""") });
  if(path.StartsWith("/slow/"))
  {
   SlowRequest = path;
   return FailSlowSource.Task.IsCompleted
    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
    : FailSlowSource.Task.WaitAsync(token);
  }
  object result;
  if(path.EndsWith("/manifest.json"))
   result=new{id="fixture",name="Fixture",version="1",types=new[]{"anime","manga"},resources=new[]{"catalog","meta","stream","chapters","pages"},catalogs=new[]{"anime","manga"}.Select(type=>new{id="stories",type,extra=new[]{new{name="skip"},new{name="search"}}})};
  else if(path.Contains("/catalog/")){
   var type=path.Contains("/anime/")?"anime":"manga";
   var next=path.Contains("skip=");
   result=new{metas=new[]{new{id=next?"two":"one",type,name=next?"Second title":type=="anime"?"Anime Story":"Manga Story"}}};
   if (path.Contains("search="))
    result = new { metas = new[] { new { id = "one", type, name = type == "anime" ? "Tracked anime" : "Tracked manga" },
                                    new { id = "other-season", type, name = type == "anime" ? "Tracked anime" : "Tracked manga" } } };
  }
  else if (path.Contains("/other-season.json")) result = new { meta = new { anilistId = 999, name = "Other season" } };
  else if(path.EndsWith("/chapters/manga/one.json"))result=new{meta=new{description="Under the sea",status="Ongoing",anilistId=44},chapters=new[]{new{id="chapter-7",title="Chapter 7",anilistProgress=(int?)7},new{id="chapter-7.5",title="Chapter 7.5",anilistProgress=(int?)null}}};
  else if(path.EndsWith("/pages/manga/chapter-7.json"))result=new{language="English",pages=new[]{"https://images.example/1.webp","https://images.example/2.webp"}};
  else if(path.EndsWith("/pages/manga/chapter-7.5.json"))result=new{pages=new[]{"https://images.example/special.webp"}};
  else if(path.EndsWith("/meta/anime/one.json"))result=new{meta=new{anilistId=42,videos=new[]{new{id="episode-9",title="Episode 9",anilistProgress=9,sourceMappings=new object[]{new{providerKey=new AddonResources(new InstalledAddon {Url=OtherUrl}).ProviderKey,unitId="alternate-09"}}},new{id="episode-10",title="Episode 10",anilistProgress=10,sourceMappings=Array.Empty<object>()}}}};
  else if(path.EndsWith("/stream/anime/episode-9.json"))
  {
   SuccessfulSourceRequests++;
   result=new{streams=new[]{new{name="Stream 9",quality="1080p",audioLanguage="Japanese",url="https://video.example/9.mp4"},
     new{name="English Dub",quality="720p",audioLanguage="English",url="https://video.example/9-dub.mp4"}}};
  }
  else if (path.EndsWith("/stream/anime/episode-10.json")) result = new { streams = new[] { new { name = "Stream 10", url = "https://video.example/10.mp4" } } };
  else throw new InvalidOperationException("Unexpected fixture request");
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(result))});
 }
}

sealed class TrackingFixture : HttpMessageHandler, IAniListAuthorization
{
 private int anime = 8, manga = 6;
 public int Anime => Volatile.Read(ref anime);
 public int Manga => Volatile.Read(ref manga);
 public bool Linked { get; set; }
 public bool Offline { get; set; }
 public string? ClientId => null;
 public Uri Callback => new("co.zyma.anikatha://anilist/callback");
 public Task<Uri> AuthorizeAsync(Uri request, CancellationToken token) => throw new NotSupportedException();
 public Task<AniListCredential?> ReadAsync() => Task.FromResult<AniListCredential?>(Linked
  ? new("fixture", DateTimeOffset.UtcNow.AddHours(1), 7, "Reader", "POINT_10") : null);
 public Task WriteAsync(AniListCredential credential) => throw new NotSupportedException();
 public void Clear() => Linked = false;

 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
 {
  if (Offline) throw new HttpRequestException("Offline fixture");
  using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
  var query = document.RootElement.GetProperty("query").GetString()!;
  var variables = document.RootElement.GetProperty("variables");
  var id = variables.GetProperty("id").GetInt32();
  if (id is not (42 or 44)) throw new InvalidOperationException("Incorrect AniList title ID");
  object data;
  if (query.Contains("SaveMediaListEntry") && query.Contains("progress:"))
  {
   var value = variables.GetProperty("value").GetInt32();
   if (id == 42) Volatile.Write(ref anime, value);
   else Volatile.Write(ref manga, value);
   data = new { SaveMediaListEntry = new { mediaId = id } };
  }
  else
   data = new { Media = new { id, mediaListEntry = new { id = id + 100, progress = id == 42 ? Anime : Manga } } };
  return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data })) };
 }
}
