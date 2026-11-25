using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using AniKatha.UI.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

internal static class HomeChecks
{
    internal static async Task RunAsync()
    {
        var db = Path.Combine(Path.GetTempPath(), $"anikatha-home-{Guid.NewGuid():N}.db3");
        var store = new LocalStore(db);
        var captureDirectory = Environment.GetEnvironmentVariable("ANIKATHA_HOME_CAPTURE_DIRECTORY");
        const string manifest = """
            {"types":["manga"],"resources":["catalog","chapters","pages"],"catalogs":[{"type":"manga","id":"books","name":"Books"}]}
            """;
        var enabled = new InstalledAddon { Url = "https://home-books.example/manifest.json", Name = "Books", ManifestJson = manifest };
        var broken = new InstalledAddon { Url = "https://home-offline.example/manifest.json", Name = "Offline", ManifestJson = manifest };
        await store.SaveAddonAsync(enabled);
        await store.SaveAddonAsync(broken);
        var origin = new AddonResources(enabled).ProviderKey;
        await store.SaveProviderTitleAsync(origin, "manga", "lantern", "The Lantern", captureDirectory is null ? null : Cover(1));
        using var fixture = new HomeFixture();
        using var http = new HttpClient(fixture);
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(new AniListClient(http));
        context.Services.AddSingleton(new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.5") })));

        fixture.PopulateCaptures = !string.IsNullOrWhiteSpace(captureDirectory);
        var fresh = context.Render<Home>();
        fresh.WaitForAssertion(() => Check(fresh.Find("#home-anime").TextContent.Contains("Episodes you start will appear here"),
            "Fresh launch shows compact empty history without an AniList login."));
        if (fixture.PopulateCaptures) Capture(captureDirectory!, "empty", fresh, context);
        fresh.Dispose();

        var chapter = ResumeEntry.Key(origin, "manga", "lantern", "first", origin);
        var episode = ResumeEntry.Key("video-provider", "anime", "series", "episode", "video-provider");
        await store.RememberAsync(new ResumeEntry { Id = "read", Origin = origin, Source = origin, Type = "manga",
            TitleId = "lantern", UnitId = "first", Label = "Chapter 1", Title = "The Lantern" });
        await store.SaveReadingPageAsync(chapter, 4);
        await store.SaveHomeUnitLengthAsync(chapter, 8);
        await store.SavePlaybackPositionAsync(episode, 84.275);
        await store.SaveHomeUnitLengthAsync(episode, 120);

        if (fixture.PopulateCaptures)
        {
            for (var index = 1; index <= 4; index++)
            {
                var id = $"demo-{index}";
                await store.RememberAsync(new ResumeEntry { Id = id, Origin = "demo", Source = "demo", Type = "anime",
                    TitleId = id, UnitId = "first", Label = $"Episode {index + 2}",
                    Title = index == 4 ? "The Lantern at the Edge of the Sea and the Long Road Back Home" : $"Preview anime {index}",
                    Poster = Cover(index) });
                var key = ResumeEntry.Key("demo", "anime", id, "first", "demo");
                await store.SavePlaybackPositionAsync(key, 342.5 + index);
                await store.SaveHomeUnitLengthAsync(key, 1440);
            }
        }

        await store.RememberAsync(new ResumeEntry { Id = "watch", Origin = "video-provider", Source = "video-provider", Type = "anime",
            TitleId = "series", UnitId = "episode", Label = "Episode 2", Title = "Anime with no art" });

        fixture.DeferTrending = !fixture.PopulateCaptures;
        if (fixture.PopulateCaptures)
        {
            await store.SaveAddonAsync(new InstalledAddon { Url = "https://video.example/manifest.json", Name = "Preview episodes",
                YouTubeEpisodesJson = """[{"aniListId":10,"episode":1,"series":"Preview title","title":"Preview episode","videoId":"dQw4w9WgXcQ"}]""" });
        }
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
        if (fixture.PopulateCaptures)
        {
            home.WaitForAssertion(() => Check(home.FindAll("#home-anime .home-card-target").Count >= 10 &&
                home.FindAll("#home-anime time").Count >= 3 && home.FindAll(".home-spotlight-art").Count == 1 &&
                home.Find(".home-spotlight-action").TextContent.Contains("Watch now"), "All Anime capture shelves and the spotlight are populated."));
            Capture(captureDirectory!, "anime", home, context);
            var firstEpisode = home.Find("[aria-labelledby='home-latest-title'] .home-card-target").TextContent;
            await home.InvokeAsync(() => home.Find("[aria-label='Next episodes']").ClickAsync(new()));
            Check(home.Find("[aria-labelledby='home-latest-title'] .home-card-target").TextContent != firstEpisode,
                "The template’s shelf arrow opens another page of real episodes.");
            await home.InvokeAsync(() => home.Find("[aria-label='Previous episodes']").ClickAsync(new()));
            await Click(home, "[aria-labelledby='home-watch-title'] header button", "View more ›");
            Check(home.FindAll("[aria-labelledby='home-watch-title'] .home-card-target").Count == 5, "Watch history expansion keeps the saved cards.");
            await Click(home, "[aria-labelledby='home-upcoming-title'] header button", "View more ›");
            Check(home.FindAll("[aria-labelledby='home-upcoming-title'] .home-card-target").Count == 6, "Upcoming expansion shows additional results.");
            await Click(home, "#home-anime button", "Random");
            Check(home.Find("[role='dialog'] #detail-title").TextContent.Length > 0, "Random opens the details of a latest result.");
            await home.InvokeAsync(() => home.Find("[aria-label='Close details']").ClickAsync(new()));
        }
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
        if (fixture.PopulateCaptures)
        {
            home.WaitForAssertion(() => Check(home.FindAll("#home-manga .home-card-target").Count >= 8, "All supported Manga shelves are populated."));
            Capture(captureDirectory!, "manga", home, context);
        }
        fixture.Trending.TrySetResult();
        await latest;
        home.WaitForElement(".home-spotlight-slide");
        Check(!home.Find("#home-manga").TextContent.Contains("Delayed anime"), "Delayed Anime data stays off Manga.");
        await home.InvokeAsync(() => home.Find("[aria-labelledby='home-followed-title'] .home-card-target").ClickAsync(new()));
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Check(navigation.Uri.Contains("/manga/details/source?") && navigation.Uri.Contains("release=second"),
            "Home opens the manga details route with the exact followed release.");
        var details = context.Render<MangaDetails>();
        details.WaitForAssertion(() => Check(details.Markup.Contains("Read Chapter 2"),
            "The manga details page resolves the exact followed chapter."));
        var chapterKey = ResumeEntry.Key(origin, "manga", "lantern", "second", origin);
        await store.SaveReadingPageAsync(chapterKey, 2);
        await details.FindAll(".design-unit-row button").Single(button => button.TextContent.Trim() == "Chapter 2").ClickAsync(new());
        Check(navigation.Uri.Contains("/manga/read/source?") && navigation.Uri.Contains("release=second"),
            "A single-source chapter opens the dedicated reader directly.");
        var scrollingReader = context.Render<MangaDetails>();
        scrollingReader.WaitForAssertion(() =>
        {
            Check(scrollingReader.FindAll(".continuous-reader .reader-page").Count == 3, "All chapter pages share a vertical stack.");
            Check(scrollingReader.Find(".reader-position").TextContent == "Page 2 of 3", "The dedicated reader restores the saved page.");
            Check(scrollingReader.FindAll(".design-unit-list, .reader-preferences").Count == 0, "Reading hides the catalogue and paged layout settings.");
        });
        await home.InvokeAsync(() => home.Find("#home-tab-anime").ClickAsync(new()));
        Check(home.Find(".home-day[aria-pressed='true']").TextContent == date &&
            home.FindAll("#home-anime button").Any(x => x.TextContent == "Trending" && x.GetAttribute("aria-pressed") == "true"),
            "Home restores the Anime day and filter selection.");
        await home.InvokeAsync(() => home.Find("[aria-label='Hide from Continue Watching']").ClickAsync(new()));
        Check(home.FindAll("a[href='/resume/watch']").Count == 0, "Hiding a card only dismisses it from Home.");
        Check(await store.GetPlaybackPositionAsync(episode) == 84.275 && await store.GetReadingPageAsync(chapter) == 4,
            "Rendering cards does not round stored resume positions.");
        Console.WriteLine("PASS: Home section order, independent filters and asynchronous feeds, resume links, missing artwork, followed chapter, provider failure, and empty schedule.");
    }

    private static string Cover(int index) => $"https://preview.example/art/midnight-lighthouse-{index % 3 + 1}.webp";

    private static void Capture(string directory, string name, IRenderedComponent<Home> home, BunitContext context)
    {
        Directory.CreateDirectory(directory);
        var markup = home.Markup.Replace("https://preview.example/art/", "art/", StringComparison.Ordinal);
        string IconMarkup(string icon) => context.Render<Icon>(parameters => parameters.Add(component => component.Name, icon)).Markup;
        File.WriteAllText(Path.Combine(directory, $"{name}.html"), $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <link rel="stylesheet" href="app.css"><title>AniKatha Home layout fixture</title></head>
            <body><div class="app-shell"><main id="main-content">{{markup}}</main>
            <nav class="bottom-nav" aria-label="Main navigation">
              <a class="active" href="/">{{IconMarkup("home")}}<span>Home</span></a>
              <a href="/anime">{{IconMarkup("anime")}}<span>Anime</span></a>
              <a href="/manga">{{IconMarkup("book")}}<span>Manga</span></a>
              <a href="/library">{{IconMarkup("library")}}<span>Library</span></a>
              <a href="/profile">{{IconMarkup("user")}}<span>Profile</span></a>
            </nav></div></body></html>
            """);
    }

    private static Task Click(IRenderedComponent<Home> home, string selector, string label) =>
        home.InvokeAsync(() => home.FindAll(selector).Single(button => button.TextContent.Trim() == label).ClickAsync(new()));

    private static void Titles(IRenderedComponent<Home> home, string kind, string[] expected) =>
        Check(home.FindAll($"#home-{kind} .home-section > header h2").Select(title => title.TextContent.Trim()).SequenceEqual(expected), "Home uses the requested section order.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HomeFixture : HttpMessageHandler
    {
        internal bool PopulateCaptures { get; set; }
        internal bool DeferTrending { get; set; }
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
                return Json("""{"pages":["https://cdn.example/1.webp","https://cdn.example/2.webp","https://cdn.example/3.webp"]}""");
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = body.RootElement.GetProperty("query").GetString()!;
            var values = body.RootElement.GetProperty("variables");
            if (query.Contains("airingSchedules", StringComparison.Ordinal))
            {
                var from = values.GetProperty("from").GetInt64();
                var until = values.GetProperty("until").GetInt64();
                var newest = values.GetProperty("sort")[0].GetString() == "TIME_DESC";
                var midday = new DateTimeOffset(DateTime.Today.AddHours(12)).ToUnixTimeSeconds();
                var times = newest && PopulateCaptures ? Enumerable.Range(1, 6).Select(i => until - i * 1800) :
                    (PopulateCaptures ? Enumerable.Range(0, 5) : Enumerable.Range(0, 1)).Select(i => midday + i * 1800);
                var airings = times.Where(time => time >= from && time < until).Select((time, index) => new
                {
                    episode = 7 + index, airingAt = time, media = new
                    {
                        id = 99 + index, type = "ANIME", isAdult = false,
                        title = new { userPreferred = index == 0 ? "Scheduled anime" : $"The next episode · preview title {index}" },
                        startDate = new { year = DateTime.Today.Year }, coverImage = new { large = PopulateCaptures ? Cover(index) : null },
                        format = "TV", genres = Array.Empty<string>()
                    }
                }).ToArray();
                return Json(JsonSerializer.Serialize(new { data = new { Page = new { pageInfo = new { hasNextPage = false }, airingSchedules = airings } } }));
            }
            var kind = values.GetProperty("type").GetString();
            var sort = values.GetProperty("sort")[0].GetString();
            if (kind == "ANIME" && sort == "TRENDING_DESC")
            {
                TrendingStarted.TrySetResult();
                if (DeferTrending) await Trending.Task.WaitAsync(token);
            }
            var title = sort == "END_DATE_DESC" ? "Recently finished" :
                kind == "MANGA" ? "Discovery manga" : sort == "TRENDING_DESC" ? "Delayed anime" : "Other anime";
            var date = DateTime.Today.AddDays(-1);
            var items = Enumerable.Range(0, PopulateCaptures ? 6 : 1).Select(index => new
            {
                id = (kind == "ANIME" ? 10 : 20) + index, type = kind, isAdult = false, status = "RELEASING",
                title = new { userPreferred = index == 0 ? title : $"{title} · Collection {index + 1}" },
                coverImage = new { large = PopulateCaptures ? Cover(index) : null },
                bannerImage = PopulateCaptures ? Cover(index + 1) : null, averageScore = 81, episodes = 12,
                startDate = new { year = date.Year, month = date.Month, day = date.Day },
                endDate = new { year = date.Year, month = date.Month, day = date.Day },
                format = kind == "ANIME" ? "TV" : "MANGA", chapters = kind == "MANGA" ? (int?)(12 + index) : null,
                genres = Array.Empty<string>()
            }).ToArray();
            return Json(JsonSerializer.Serialize(new { data = new { Page = new { media = items, pageInfo = new { hasNextPage = false } } } }));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
