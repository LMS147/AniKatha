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
await using var context = new BunitContext();
context.Services.AddSingleton(store);
context.Services.AddCascadingValue(_ => new SourceResultCache());
context.Services.AddSingleton(client);
context.JSInterop.Mode = JSRuntimeMode.Loose;

var profile = context.Render<Profile>();
void Click(Bunit.IRenderedComponent<Profile> view, string label) => view.FindAll("button").Single(x=>x.TextContent.Trim()==label).Click();
await profile.InvokeAsync(()=>profile.Find("input[inputmode=url]").Input("https://fiction.example/books?config=test"));
await profile.InvokeAsync(()=>profile.Find("form").SubmitAsync());
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
var anime=context.Render<ManifestCatalog>(p=>p.Add(x=>x.Source,provider).Add(x=>x.Catalog,provider.Catalogs("ANIME").Single()).Add(x=>x.Providers,sources));
foreach(var name in new[]{"Anime Story","Episode 9","Stream 9"})
 await ClickAsync(anime, name);
anime.WaitForAssertion(()=>Check(anime.FindComponent<WatchPreview>().Instance.VideoUrl=="https://video.example/9.mp4","Selected episode reaches existing player"));
Check(!anime.Markup.Contains("Manga Story"),"No manga in anime catalogue");
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
var record=(await store.HistoryAsync()).Single(x=>x.Type=="manga");
var resume=context.Render<Resume>(p=>p.Add(x=>x.Id,record.Id));
resume.WaitForAssertion(()=>Check(resume.FindAll("button").Any(x=>x.TextContent.Trim()=="Read Chapter 7"),"Re-resolve chapter on resume"));
await ClickAsync(resume, "Read Chapter 7");
resume.WaitForAssertion(()=>Check(resume.Find(".reader-page").GetAttribute("src")=="https://images.example/2.webp","History restores exact page"));

await ClickAsync(reopened, "Mark chapter read");
reopened.WaitForAssertion(()=>Check(reopened.Markup.Contains("Chapter completed"),"Complete chapter"));
var readingKey = ResumeEntry.Key(record.Origin, record.Type, record.TitleId, record.UnitId, record.Source);
Check(await store.IsUnitCompleteAsync(readingKey) && await store.GetReadingPageAsync(readingKey)==2,"Completion preserves precise page");
await ClickAsync(reopened, "Next chapter →");
reopened.WaitForAssertion(()=>Check(reopened.Find(".reader-page").GetAttribute("src")=="https://images.example/special.webp","Decimal chapter follows provider order"));
Check(reopened.FindAll("button").Single(x => x.TextContent.Trim()=="Next chapter →").HasAttribute("disabled"), "Last chapter boundary");
await ClickAsync(reopened, "← Previous chapter");
reopened.WaitForAssertion(()=>Check(reopened.Find(".reader-page").GetAttribute("src")=="https://images.example/2.webp","Previous chapter restores page"));
var firstPlayer = anime.FindComponent<WatchPreview>();
await firstPlayer.InvokeAsync(() => firstPlayer.Instance.CompleteEpisode());
anime.WaitForAssertion(()=>Check(anime.FindComponent<WatchPreview>().Instance.VideoUrl=="https://video.example/10.mp4","Autoplay opens the next episode source"));
Check(!anime.FindComponent<WatchPreview>().Instance.OnNext.HasDelegate, "Last episode boundary");
await ReaderChecks.RunAsync(context, store);


Console.WriteLine("PASS: phase 2 fixture covered metadata, explicit unit mapping, source fallback and preference, cache refresh and chapter completion.");
Console.WriteLine("PASS: install via Profile, type routing, pagination, repeat-page stop, chapter reading, page resume, enable/disable, episode source → player.");

await CatalogChecks.RunAsync(new LocalStore(Path.Combine(Path.GetTempPath(), $"anikatha-catalog-{Guid.NewGuid()}.db3")));

static async Task ClickAsync<T>(IRenderedComponent<T> view, string label) where T : IComponent
{
 view.WaitForAssertion(() => Check(view.FindAll("button").Any(x => x.TextContent.Trim() == label), label));
 await view.InvokeAsync(() => view.FindAll("button").Single(x => x.TextContent.Trim() == label).ClickAsync(new()));
}

static void Check(bool valid,string name){if(!valid)throw new Exception(name);}
sealed class FixtureHandler : HttpMessageHandler
{
 internal const string OtherUrl = "https://fiction.example/slow/manifest.json?config=test";
 internal string? SlowRequest { get; private set; }
 internal int SuccessfulSourceRequests { get; private set; }
 internal TaskCompletionSource<HttpResponseMessage> FailSlowSource {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  var uri=request.RequestUri!;
  if(uri.Query!="?config=test")throw new InvalidOperationException("Configuration lost");
  var path=uri.AbsolutePath;
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
  }
  else if(path.EndsWith("/chapters/manga/one.json"))result=new{meta=new{description="Under the sea",status="Ongoing"},chapters=new[]{new{id="chapter-7",title="Chapter 7"},new{id="chapter-7.5",title="Chapter 7.5"}}};
  else if(path.EndsWith("/pages/manga/chapter-7.json"))result=new{language="English",pages=new[]{"https://images.example/1.webp","https://images.example/2.webp"}};
  else if(path.EndsWith("/pages/manga/chapter-7.5.json"))result=new{pages=new[]{"https://images.example/special.webp"}};
  else if(path.EndsWith("/meta/anime/one.json"))result=new{meta=new{videos=new[]{new{id="episode-9",title="Episode 9",sourceMappings=new object[]{new{providerKey=new AddonResources(new InstalledAddon {Url=OtherUrl}).ProviderKey,unitId="alternate-09"}}},new{id="episode-10",title="Episode 10",sourceMappings=Array.Empty<object>()}}}};
  else if(path.EndsWith("/stream/anime/episode-9.json"))
  {
   SuccessfulSourceRequests++;
   result=new{streams=new[]{new{name="Stream 9",quality="1080p",audioLanguage="Japanese",url="https://video.example/9.mp4"}}};
  }
  else if (path.EndsWith("/stream/anime/episode-10.json")) result = new { streams = new[] { new { name = "Stream 10", url = "https://video.example/10.mp4" } } };
  else throw new InvalidOperationException("Unexpected fixture request");
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(result))});
 }
}
