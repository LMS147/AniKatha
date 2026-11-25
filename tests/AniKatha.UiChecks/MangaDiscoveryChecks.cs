using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using AniKatha.UI.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

internal static class MangaDiscoveryChecks
{
    public static async Task RunAsync()
    {
        var store = new LocalStore(Path.Combine(Path.GetTempPath(), $"manga-discovery-{Guid.NewGuid():N}.db3"));
        const string fast = """[{"type":"manga","id":"ranked","extra":[{"name":"skip"}]},{"type":"manga","id":"overlap","format":"Manhwa","ranking":"popularity"},{"type":"manga","id":"novels","format":"Novel","ranking":"popularity"},{"type":"manga","id":"community","ranking":"score"}]""";
        const string slow = """[{"type":"manga","id":"slow","format":"Manga","ranking":"popularity"}]""";
        const string failure = """[{"type":"manga","id":"retry","format":"Manga","ranking":"popularity"}]""";
        const string generic = """[{"type":"manga","id":"generic","name":"Popular Manhwa"}]""";
        foreach (var (name, catalogs) in new[] { ("fast", fast), ("slow", slow), ("offline", failure), ("generic", generic) })
        {
            await store.SaveAddonAsync(new()
            {
                Name = name, Url = $"https://{name}.example/manifest.json",
                ManifestJson = $$"""{"types":["manga"],"resources":["catalog","chapters","pages"],"catalogs":{{catalogs}}}"""
            });
        }
        using var fixture = new Fixture();
        using var http = new HttpClient(fixture);
        await using var context = new BunitContext();
        var session = new CatalogSession();
        context.Services.AddCascadingValue(_ => session);
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.9") })));
        context.Services.AddSingleton(new AniListClient(http));
        context.Services.AddSingleton(new JikanCatalogue(http));
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var screen = context.Render<Manga>();
        string[] labels = ["Popular Manga", "Popular Manhwa", "Popular Light Novels", "Top Rated"];
        string Shelf(string name) => $".manga-discovery[aria-label='{name}']";
        string[] Titles(string shelf) => screen.FindAll($"{Shelf(shelf)} .poster-card strong").Select(x => x.TextContent).ToArray();
        void Has(string shelf, string title) => screen.WaitForAssertion(() => Check(Titles(shelf).Contains(title), $"Missing {title} in {shelf}"));
        Task Button(string shelf, string startsWith) => screen.InvokeAsync(() => screen
            .FindAll($"{Shelf(shelf)} button").First(element => element.TextContent.Trim().StartsWith(startsWith)).ClickAsync(new()));

        Check(screen.FindAll(".manga-discovery h2").Select(x => x.TextContent).SequenceEqual(labels), "Manga shelf order or initial rendering changed");
        screen.WaitForAssertion(() => Check(!screen.Find("fieldset").HasAttribute("disabled"), "Search did not become usable"));
        Has(labels[0], "Fast Manga");
        Has(labels[1], "Fast Manhwa");
        Has(labels[2], "Fast Light Novel");
        Has(labels[2], "Explicit Light Novel");
        Has(labels[3], "Fast Manga");
        Has(labels[3], "Provider Rating");
        Check(!fixture.Delayed.Task.IsCompleted && !fixture.BuiltIn.Task.IsCompleted, "Fast add-on should render before slower feeds");
        Check(!screen.Markup.Contains("Generic Novel") && !screen.Markup.Contains("Unclassified Reader"), "Unclassified results were ranked");
        Check(screen.FindAll(".design-provider-title").Count == 0, "Add-on catalogs still render in separate blocks");
        Check(Titles(labels[1]).Count(title => title == "Fast Manhwa") == 1, "Same-provider ID was duplicated");
        Check(!fixture.Calls.Any(uri => uri.AbsolutePath.Contains("/chapters/")), "Discovery fetched chapters per card");

        fixture.BuiltIn.SetResult();
        fixture.Delayed.SetResult();
        Has(labels[0], "Slow Manga");
        Has(labels[0], "AniList Japanese Manga");
        Check(Titles(labels[0]).First() == "Fast Manga", "Late supply shifted a visible card or provider IDs collided");
        Has(labels[1], "AniList Korean Manhwa");
        Has(labels[2], "AniList Light Novel");
        Has(labels[3], "AniList Rated");
        Check(!Titles(labels[3]).Contains("AniList Unrated"), "Unrated AniList title shown");
        await Button(labels[0], "Retry offline");
        Has(labels[0], "Recovered Manga");
        Check(Titles(labels[0]).Contains("Fast Manga"), "Retry cleared data from another feed");

        var capture = Environment.GetEnvironmentVariable("ANIKATHA_MANGA_CAPTURE_DIRECTORY");
        if (capture is not null)
        {
            Directory.CreateDirectory(capture);
            var markup = screen.Markup.Replace("https://art.example/", "art/");
            await File.WriteAllTextAsync(Path.Combine(capture, "manga.html"),
                "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>" +
                "<link rel='stylesheet' href='app.css'></head><body><div class='app-shell'><main>" + markup +
                "</main></div></body></html>");
        }

        foreach (var label in labels)
        {
            await Button(label, "View all");
            Check(!screen.Find($"{Shelf(label)} .catalog-art-grid").ClassList.Contains("catalog-shelf"), "View all did not expand " + label);
            if (label != labels[0]) await Button(label, "Show less");
        }
        await Button(labels[0], "More from fast");
        Has(labels[0], "Later Manga");
        Has(labels[2], "Later Light Novel");
        Check(fixture.Calls.Count(uri => uri.AbsolutePath.Contains("/ranked/skip=5.json")) == 1, "Shared offset did not count raw entries");

        var count = fixture.Calls.Count;
        var nav = context.Services.GetRequiredService<NavigationManager>();
        await screen.InvokeAsync(() => screen.FindAll($"{Shelf(labels[0])} .poster-card")
            .Single(button => button.QuerySelector("strong")?.TextContent == "Fast Manga").ClickAsync(new()));
        Check(nav.Uri.Contains("title=shared") && nav.Uri.Contains("catalog=ranked"), "Card did not preserve title and catalog IDs");
        var detail = context.Render<MangaDetails>();
        detail.WaitForAssertion(() => Check(detail.Markup.Contains("Fixture Chapter"), "Provider chapter list did not open"));
        await detail.InvokeAsync(() => detail.FindAll(".design-unit-row button")
            .First(button => button.TextContent.Trim().StartsWith("Fixture Chapter")).ClickAsync(new()));
        Check(nav.Uri.Contains("/manga/read/source") && nav.Uri.Contains("release=chapter-one"), "Selected chapter did not open in the provider reader");
        var reader = context.Render<MangaDetails>();
        var chapterView = reader.WaitForComponent<ReadMangaPreview>();
        chapterView.WaitForAssertion(() => Check(chapterView.Markup.Contains("Page 1 of 3"), "Wrong chapter opened"));
        await chapterView.InvokeAsync(() => chapterView.Instance.ObservePage(2));
        reader.Dispose();
        var resumed = context.Render<MangaDetails>();
        resumed.WaitForAssertion(() => Check(resumed.Markup.Contains("Page 2 of 3"), "Reader did not resume at page 2"));
        Check(fixture.Calls.Skip(count).All(uri => !uri.AbsolutePath.Contains("/catalog/")), "Details or reader refetched discovery catalogs");

        screen.Dispose();
        var catalogCalls = fixture.Calls.Count;
        screen = context.Render<Manga>();
        Has(labels[0], "Later Manga");
        Check(fixture.Calls.Count == catalogCalls, "Warm navigation refetched fresh shelves");
        Check(!screen.Find($"{Shelf(labels[0])} .catalog-art-grid").ClassList.Contains("catalog-shelf"), "Returning from details lost the expanded shelf");
        var genericProvider = new AddonResources((await store.GetAddonsAsync()).Single(addon => addon.Name == "generic"));
        await screen.InvokeAsync(() => screen.Find(".design-catalog > .manga-provider-picker select").ChangeAsync(genericProvider.ProviderKey));
        screen.WaitForAssertion(() => Check(screen.FindAll(".design-provider-title").Any(x => x.TextContent.Contains("Unclassified Reader")), "Browse-only catalog became unreachable"));
        await screen.InvokeAsync(() => screen.Find(".manga-discovery-back").ClickAsync(new()));
        Has(labels[0], "Fast Manga");
        screen.Dispose();
        await store.SetAddonDisabledAsync("https://fast.example/manifest.json", true);
        screen = context.Render<Manga>();
        Has(labels[0], "Slow Manga");
        Check(!screen.Markup.Contains("Fast Manga"), "Disabled add-on resurfaced from discovery cache");
        screen.Dispose();
        await store.SetAddonDisabledAsync("https://fast.example/manifest.json", false);
        screen = context.Render<Manga>();
        Has(labels[0], "Fast Manga");
        Console.WriteLine("PASS: Manga shelf classification, catalog ranking, progressive rendering, late results, targeted retry, shared pagination, provider navigation and chapter resume, warm cache, and disabled sources.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Calls { get; } = new();
        public TaskCompletionSource Delayed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BuiltIn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int retries;
        private static string Art(int index) => $"https://art.example/midnight-lighthouse-{index}.webp";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            Calls.Enqueue(uri);
            var path = uri.AbsolutePath;
            if (uri.Host == "graphql.anilist.co")
            {
                await BuiltIn.Task.WaitAsync(token);
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var vars = doc.RootElement.GetProperty("variables");
                var format = vars.GetProperty("format").GetString();
                var name = (vars.GetProperty("country").GetString(), format) switch
                {
                    ("JP", "MANGA") => "Japanese Manga",
                    ("KR", "MANGA") => "Korean Manhwa",
                    ("JP", "NOVEL") => "Light Novel",
                    _ => "Rated"
                };
                object Media(int id, string title, int? score) => new
                {
                    id, type = "MANGA", title = new { userPreferred = title },
                    coverImage = new { large = Art(1 + id % 3) }, startDate = new { year = 2025 },
                    format = format ?? "MANGA", averageScore = score
                };
                return Json(new { data = new { Page = new { pageInfo = new { hasNextPage = true },
                    media = new[] { Media(101, $"AniList {name}", 86), Media(102, "AniList Unrated", null) } } } });
            }

            if (path.StartsWith("/chapters/"))
                return Json(new { meta = new { id = "shared", name = "Fast Manga" },
                    chapters = new[] { new { id = "chapter-one", title = "Fixture Chapter", chapter = "1" } } });
            if (path.StartsWith("/pages/"))
                return Json(new { pages = Enumerable.Range(1, 3).Select(Art).ToArray() });
            if (uri.Host == "slow.example") await Delayed.Task.WaitAsync(token);
            if (uri.Host == "offline.example" && Interlocked.Increment(ref retries) == 1)
                return new(HttpStatusCode.ServiceUnavailable);

            object Card(string id, string name, string? format = null, int? popularity = null, int? communityScore = null, int art = 1)
                => new { id, name, type = "manga", format, popularity, communityScore, poster = Art(art) };
            object[] cards = path switch
            {
                "/catalog/manga/ranked.json" =>
                [
                    Card("shared", "Fast Manga", "Manga", 10, 91),
                    Card("manhwa", "Fast Manhwa", "Manhwa", 22, 89, 2),
                    Card("light", "Fast Light Novel", "Light Novel", 24, 82, 3),
                    Card("novel", "Generic Novel", "Novel", 100),
                    Card("unclassified", "Unclassified Reader")
                ],
                "/catalog/manga/ranked/skip=5.json" => [Card("later", "Later Manga", "Manga", 5), Card("laterlight", "Later Light Novel", "Light Novel", 3)],
                "/catalog/manga/overlap.json" => [Card("manhwa", "Fast Manhwa")],
                "/catalog/manga/novels.json" => [Card("explicitlight", "Explicit Light Novel", "Light Novel"), Card("broadnovel", "Generic Novel")],
                "/catalog/manga/community.json" => [new { id = "provider-score", name = "Provider Rating", type = "manga", poster = Art(2) }],
                "/catalog/manga/slow.json" => [Card("shared", "Slow Manga")],
                "/catalog/manga/retry.json" => [Card("again", "Recovered Manga", art: 3)],
                "/catalog/manga/generic.json" => [Card("generic", "Unclassified Reader")],
                _ => []
            };
            return Json(new { metas = cards });
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
