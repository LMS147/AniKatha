using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using AniKatha.UI.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

internal static class SpotlightChecks
{
    internal static async Task RunAsync()
    {
        using var fixture = new SpotlightFixture();
        using var http = new HttpClient(fixture);
        var catalog = new AniListClient(http);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var ids = (await catalog.HomeCatalogueAsync(HomeCatalogue.TrendingAnime, today, default)).Select(title => title.Media.Id);
        Check(ids.SequenceEqual(Enumerable.Range(1, 10)), "Spotlight caps distinct airing titles at ten in ranked order across pages.");
        fixture.ShortFeed = true;
        Check((await catalog.HomeCatalogueAsync(HomeCatalogue.TrendingAnime, today, default)).Count == 6,
            "A shorter ranked result is shown without padding.");
        fixture.ShortFeed = false;

        var store = new LocalStore(Path.Combine(Path.GetTempPath(), $"anikatha-spotlight-{Guid.NewGuid():N}.db3"));
        await store.SaveAddonAsync(new InstalledAddon
        {
            Url = "https://spotlight-fixture.example/manifest.json", Name = "Spotlight provider",
            ManifestJson = """{"types":["anime"],"resources":[{"name":"meta","idPrefixes":["anilist:"]},"stream"]}"""
        });
        await store.SaveAddonAsync(new InstalledAddon
        {
            Url = "https://spotlight-youtube.example/manifest.json", Name = "Embedded episodes",
            YouTubeEpisodesJson = """
                [{"aniListId":2,"episode":1,"series":"Anime 2","title":"First episode","videoId":"dQw4w9WgXcQ"},
                 {"aniListId":2,"episode":2,"series":"Anime 2","title":"Second episode","videoId":"aqz-KE-bpKQ"}]
                """
        });
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(catalog);
        context.Services.AddSingleton(new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.5") })));
        var session = new HomeSpotlightSession();
        context.Services.AddCascadingValue(_ => session);

        var home = context.Render<Home>();
        home.WaitForAssertion(() => Check(home.Find(".home-spotlight-action").TextContent.Contains("Watch now"), "A verified stream enables playback."));
        var order = home.Find(".home-landing").Children.Select(element => element.ClassList.First()).ToArray();
        Check(order.SequenceEqual(new[] { "home-header", "home-spotlight", "home-segment", "home-viewports" }), "Spotlight is above the tabs.");
        Check(home.Find(".home-spotlight-meta").TextContent.Contains("12 episodes"), "The spotlight uses actual episode metadata.");

        await home.InvokeAsync(() => home.Find(".home-spotlight-action").ClickAsync(new()));
        home.WaitForAssertion(() => Check(home.FindAll(".home-source-sheet button").Any(button => button.TextContent.Trim() == "Episode 2"),
            "Watch now opens the existing episode picker rather than auto-playing the first episode."));
        await home.InvokeAsync(() => home.FindAll(".home-source-sheet button").Single(button => button.TextContent.Trim() == "Episode 1").ClickAsync(new()));
        home.WaitForAssertion(() => Check(home.FindAll(".home-source-sheet button").Any(button => button.TextContent.Trim() == "Play"),
            "Choosing the episode resolves its actual provider stream."));
        await home.InvokeAsync(() => home.FindAll(".home-source-sheet button").Single(button => button.TextContent.Trim() == "Play").ClickAsync(new()));
        home.WaitForAssertion(() => Check(home.FindComponent<WatchPreview>().Instance.VideoUrl == "https://media.example/episode.mp4",
            "The resolved stream reaches the existing player."));
        await home.InvokeAsync(() => home.Find("[aria-label='Close details']").ClickAsync(new()));

        await home.InvokeAsync(() => home.Find(".home-spotlight").TriggerEventAsync("ontouchstart",
            new TouchEventArgs { Touches = [new TouchPoint { ClientX = 220, ClientY = 120 }] }));
        await home.InvokeAsync(() => home.Find(".home-spotlight").TriggerEventAsync("ontouchend",
            new TouchEventArgs { ChangedTouches = [new TouchPoint { ClientX = 110, ClientY = 130 }] }));
        Check(home.Find(".home-spotlight h2").TextContent == "Anime 2", "Horizontal swipe advances the shared spotlight.");
        var requests = fixture.TrendingRequests;
        await home.InvokeAsync(() => home.Find("#home-tab-manga").ClickAsync(new()));
        Check(home.Find(".home-spotlight h2").TextContent == "Anime 2" && home.FindAll(".home-spotlight").Count == 1,
            "Manga uses the same slide and only one spotlight.");
        await home.InvokeAsync(() => home.Find(".home-spotlight-action").ClickAsync(new()));
        home.WaitForAssertion(() => Check(home.FindAll(".detail-sheet a[href*='?from=home']").Count == 2,
            "An embedded series opens the existing list of episode choices with a Home return."));
        await home.InvokeAsync(() => home.Find("[aria-label='Close details']").ClickAsync(new()));
        await home.InvokeAsync(() => home.Find("#home-tab-anime").ClickAsync(new()));
        Check(fixture.TrendingRequests == requests && session.MediaId == 2, "Tab switches neither reload ranking nor reset the slide.");

        await home.InvokeAsync(() => home.Find("[aria-label='Next spotlight']").ClickAsync(new()));
        home.WaitForAssertion(() => Check(home.Find(".home-spotlight-action").TextContent.Trim() == "View details",
            "An airing title without playback keeps the honest action."));
        await home.InvokeAsync(() => home.Find(".home-spotlight-action").ClickAsync(new()));
        Check(home.Find("#detail-title").TextContent == "Anime 3", "View details opens the existing real title panel.");
        await home.InvokeAsync(() => home.Find("[aria-label='Close details']").ClickAsync(new()));
        await home.InvokeAsync(() => home.Find("#home-tab-manga").ClickAsync(new()));
        home.Dispose();

        var returned = context.Render<Home>();
        returned.WaitForAssertion(() => Check(returned.Find(".home-spotlight h2").TextContent == "Anime 3" &&
            returned.Find("#home-tab-manga").GetAttribute("aria-selected") == "true",
            "Recreating Home in the same layout restores the selected slide and tab."));
        returned.Dispose();

        fixture.FailTrending = true;
        var offline = context.Render<Home>();
        offline.WaitForAssertion(() => Check(offline.Find(".home-spotlight").TextContent.Contains("Spotlight unavailable"),
            "Spotlight shows an error while the Home tabs are still usable."));
        fixture.FailTrending = false;
        await offline.InvokeAsync(() => offline.Find(".home-spotlight-state button").ClickAsync(new()));
        offline.WaitForElement(".home-spotlight-slide");
        Console.WriteLine("PASS: spotlight rank, deduplication, swipe, shared state, real episode and details actions, and retry.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SpotlightFixture : HttpMessageHandler
    {
        internal bool ShortFeed { get; set; }
        internal bool FailTrending { get; set; }
        internal int TrendingRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host == "spotlight-fixture.example")
            {
                var path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath);
                return path switch
                {
                    "/meta/anime/anilist:1.json" => Json(new { meta = new { anilistId = 1, videos = new[]
                    {
                        new { id = "episode-1", title = "Episode 1" }, new { id = "episode-2", title = "Episode 2" }
                    } } }),
                    "/stream/anime/episode-1.json" => Json(new { streams = new[] { new { url = "https://media.example/episode.mp4" } } }),
                    _ => new(HttpStatusCode.NotFound)
                };
            }

            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = input.RootElement.GetProperty("query").GetString()!;
            var variables = input.RootElement.GetProperty("variables");
            if (query.Contains("airingSchedules", StringComparison.Ordinal))
                return Json(new { data = new { Page = new { airingSchedules = Array.Empty<object>(), pageInfo = new { hasNextPage = false } } } });
            if (!variables.TryGetProperty("status", out var status) || status.GetString() != "RELEASING")
                return Page([], false);

            TrendingRequests++;
            Check(variables.GetProperty("type").GetString() == "ANIME" && variables.GetProperty("sort")[0].GetString() == "TRENDING_DESC",
                "The upstream query filters for airing anime in trending order.");
            if (FailTrending) return new(HttpStatusCode.ServiceUnavailable);
            var page = variables.GetProperty("page").GetInt32();
            if (page == 1)
            {
                object[] contents = [
                    Entry(99, "MANGA"), Entry(100, status: "FINISHED"), Entry(101, status: "NOT_YET_RELEASED"),
                    Entry(102, future: true), Entry(1), Entry(1), Entry(2), Entry(3), Entry(4), Entry(5), Entry(6)
                ];
                return Page(contents, !ShortFeed);
            }
            return Page([Entry(6), .. Enumerable.Range(7, 6).Select(id => Entry(id))], false);
        }

        private static object Entry(int id, string type = "ANIME", string status = "RELEASING", bool future = false)
        {
            var date = DateTime.Today.AddDays(future ? 1 : -1);
            return new
            {
                id, type, status, title = new { userPreferred = $"Anime {id}" },
                startDate = new { year = date.Year, month = date.Month, day = date.Day },
                coverImage = new { large = "https://assets.example/cover.webp" }, averageScore = 81, episodes = 12,
                format = "TV", genres = Array.Empty<string>()
            };
        }

        private static HttpResponseMessage Page(object[] entries, bool more) =>
            Json(new { data = new { Page = new { media = entries, pageInfo = new { hasNextPage = more } } } });

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: JsonSerializerOptions.Default) };
    }
}
