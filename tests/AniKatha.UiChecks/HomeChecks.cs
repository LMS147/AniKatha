using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

internal static class HomeChecks
{
    internal static async Task RunAsync()
    {
        var db = Path.Combine(Path.GetTempPath(), $"anikatha-home-{Guid.NewGuid():N}.db3");
        var store = new LocalStore(db);
        const string manifest = """
            {"types":["manga"],"resources":["catalog","chapters","pages"],"catalogs":[{"type":"manga","id":"books","name":"Books"}]}
            """;
        var enabled = new InstalledAddon { Url = "https://home-books.example/manifest.json", Name = "Books", ManifestJson = manifest };
        var broken = new InstalledAddon { Url = "https://home-offline.example/manifest.json", Name = "Offline", ManifestJson = manifest };
        await store.SaveAddonAsync(enabled);
        await store.SaveAddonAsync(broken);
        var origin = new AddonResources(enabled).ProviderKey;
        await store.SaveProviderTitleAsync(origin, "manga", "lantern", "The Lantern");
        using var fixture = new HomeFixture();
        using var http = new HttpClient(fixture);
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(new AniListClient(http));
        context.Services.AddSingleton(new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.5") })));

        var fresh = context.Render<Home>();
        fresh.WaitForAssertion(() => Check(fresh.Find("#home-anime").TextContent.Contains("Episodes you start will appear here"),
            "Fresh launch shows compact empty history without an AniList login."));
        fresh.Dispose();

        var chapter = ResumeEntry.Key(origin, "manga", "lantern", "first", origin);
        var episode = ResumeEntry.Key("video-provider", "anime", "series", "episode", "video-provider");
        await store.RememberAsync(new ResumeEntry { Id = "read", Origin = origin, Source = origin, Type = "manga",
            TitleId = "lantern", UnitId = "first", Label = "Chapter 1", Title = "The Lantern" });
        await store.SaveReadingPageAsync(chapter, 4);
        await store.SaveHomeUnitLengthAsync(chapter, 8);
        await store.RememberAsync(new ResumeEntry { Id = "watch", Origin = "video-provider", Source = "video-provider", Type = "anime",
            TitleId = "series", UnitId = "episode", Label = "Episode 2", Title = "Anime with no art" });
        await store.SavePlaybackPositionAsync(episode, 84.275);
        await store.SaveHomeUnitLengthAsync(episode, 120);

        var home = context.Render<Home>();
        home.WaitForAssertion(() =>
        {
            var watch = home.Find("a[href='/resume/watch']");
            Check(watch.TextContent.Contains("1:24 / 2:00"), "History shows the elapsed time and real known duration.");
            Check(watch.QuerySelector("progress") is not null && watch.QuerySelector("img") is null &&
                watch.QuerySelector("svg") is not null, "Progress and stable missing-art placeholder render.");
            Check(home.Find("#home-tab-anime").GetAttribute("aria-selected") == "true", "Anime is active first.");
        });
        Titles(home, "anime", ["Continue Watching", "Latest Episodes", "Upcoming Anime", "Release lists", "Estimated Schedule"]);
        await Click(home, "#home-anime button", "Newly Added");
        Check(home.Find("[aria-labelledby='home-releases-title']").TextContent.Contains("addition dates"), "Unknown addition dates are explicit.");
        await Click(home, "#home-anime button", "Just Completed");
        home.WaitForAssertion(() => Check(home.Find("[aria-labelledby='home-releases-title']").TextContent.Contains("Recently finished"), "Releases change data."));
        var schedule = home.Find("[aria-labelledby='home-schedule-title']");
        home.WaitForAssertion(() => Check(schedule.TextContent.Contains("Scheduled anime") && schedule.TextContent.Contains("Episode 7"),
            "Schedule shows a real supplied title, time and episode."));
        Check(schedule.QuerySelector("time")?.GetAttribute("datetime") is not null, "Schedule rows carry an exact airing timestamp.");
        await home.InvokeAsync(() => home.Find("[aria-label='Next day']").ClickAsync(new()));
        home.WaitForAssertion(() => Check(schedule.TextContent.Contains("No releases scheduled for this day"), "Changing to an empty day is distinct from an outage."));
        var date = home.Find(".home-day[aria-pressed='true']").TextContent;

        var latest = home.InvokeAsync(() => home.FindAll("#home-anime button").Single(x => x.TextContent == "Trending").ClickAsync(new()));
        await fixture.TrendingStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await home.InvokeAsync(() => home.Find("#home-tab-manga").ClickAsync(new()));
        Titles(home, "manga", ["New Chapters from Followed Comics", "Reading History", "Discover Manga", "Most Recent Popular", "New Comics", "Latest Updates"]);
        home.WaitForAssertion(() =>
        {
            Check(home.Find("a[href='/resume/read']").TextContent.Contains("Page 4 / 8"), "Reading card uses saved chapter and page.");
            Check(home.Find("[aria-labelledby='home-followed-title']").TextContent.Contains("Chapter 2"), "Latest followed chapter comes from the provider.");
            var discovery = home.Find("[aria-labelledby='home-discover-title']").TextContent;
            Check(discovery.Contains("The Lantern") && discovery.Contains("sources couldn’t be loaded"), "Failed provider leaves successful discovery usable.");
        });
        fixture.Trending.TrySetResult();
        await latest;
        Check(!home.Find("#home-manga").TextContent.Contains("Delayed anime"), "Delayed Anime data stays off Manga.");
        await home.InvokeAsync(() => home.Find("[aria-labelledby='home-followed-title'] .home-card-target").ClickAsync(new()));
        home.WaitForAssertion(() => Check(home.Find(".home-source-sheet").TextContent.Contains("Read Chapter 2"), "Home opens the exact followed chapter source."));
        await home.InvokeAsync(() => home.Find("[aria-label='Close details']").ClickAsync(new()));
        await home.InvokeAsync(() => home.Find("#home-tab-anime").ClickAsync(new()));
        Check(home.Find(".home-day[aria-pressed='true']").TextContent == date &&
            home.FindAll("#home-anime button").Any(x => x.TextContent == "Trending" && x.GetAttribute("aria-pressed") == "true"),
            "Home restores the Anime day and filter selection.");
        Check(await store.GetPlaybackPositionAsync(episode) == 84.275 && await store.GetReadingPageAsync(chapter) == 4,
            "Rendering cards does not round stored resume positions.");
        Console.WriteLine("PASS: Home section order, independent filters and asynchronous feeds, resume links, missing artwork, followed chapter, provider failure, and empty schedule.");
    }

    private static Task Click(IRenderedComponent<Home> home, string selector, string label) =>
        home.InvokeAsync(() => home.FindAll(selector).Single(button => button.TextContent.Trim() == label).ClickAsync(new()));

    private static void Titles(IRenderedComponent<Home> home, string kind, string[] expected) =>
        Check(home.FindAll($"#home-{kind} .home-section > header h2").Select(title => title.TextContent).SequenceEqual(expected), "Home uses the requested section order.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HomeFixture : HttpMessageHandler
    {
        internal TaskCompletionSource TrendingStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Trending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "home-offline.example") return new(HttpStatusCode.ServiceUnavailable);
            if (uri.Host == "home-books.example")
            {
                if (uri.AbsolutePath.Contains("/catalog/", StringComparison.Ordinal))
                    return Json("""{"metas":[{"id":"lantern","type":"manga","name":"The Lantern"},{"id":"bad-type","type":"anime","name":"Wrong type"}]}""");
                if (uri.AbsolutePath.Contains("/chapters/", StringComparison.Ordinal))
                    return Json("""{"chapters":[{"id":"first","title":"Chapter 1"},{"id":"second","title":"Chapter 2"}]}""");
                return Json("""{"pages":["https://cdn.example/page.webp"]}""");
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = body.RootElement.GetProperty("query").GetString()!;
            var values = body.RootElement.GetProperty("variables");
            if (query.Contains("airingSchedules", StringComparison.Ordinal))
            {
                var from = values.GetProperty("from").GetInt64();
                var midday = new DateTimeOffset(DateTime.Today.AddHours(12)).ToUnixTimeSeconds();
                if (midday < from || midday >= values.GetProperty("until").GetInt64())
                    return Json("""{"data":{"Page":{"airingSchedules":[],"pageInfo":{"hasNextPage":false}}}}""");
                return Json(JsonSerializer.Serialize(new { data = new { Page = new
                {
                    pageInfo = new { hasNextPage = false },
                    airingSchedules = new[] { new { episode = 7, airingAt = midday, media = new {
                        id = 99, type = "ANIME", isAdult = false, title = new { userPreferred = "Scheduled anime" },
                        startDate = new { year = DateTime.Today.Year }, coverImage = new { large = (string?)null }, genres = Array.Empty<string>()
                    } } }
                } } }));
            }
            var kind = values.GetProperty("type").GetString();
            var sort = values.GetProperty("sort")[0].GetString();
            if (kind == "ANIME" && sort == "TRENDING_DESC")
            {
                TrendingStarted.TrySetResult();
                await Trending.Task.WaitAsync(token);
            }
            var title = sort == "END_DATE_DESC" ? "Recently finished" :
                kind == "MANGA" ? "Discovery manga" : sort == "TRENDING_DESC" ? "Delayed anime" : "Other anime";
            var date = DateTime.Today.AddDays(-1);
            return Json(JsonSerializer.Serialize(new { data = new { Page = new { media = new[]
            {
                new { id = kind == "ANIME" ? 10 : 11, type = kind, isAdult = false, title = new { userPreferred = title },
                    coverImage = new { large = (string?)null }, startDate = new { year = date.Year, month = date.Month, day = date.Day },
                    endDate = new { year = date.Year, month = date.Month, day = date.Day }, genres = Array.Empty<string>() }
            }, pageInfo = new { hasNextPage = false } } } }));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
