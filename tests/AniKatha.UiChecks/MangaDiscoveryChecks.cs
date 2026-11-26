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
        const string fast = """[{"type":"manga","id":"ranked","ranking":"popularity","extra":[{"name":"skip"}]},{"type":"manga","id":"overlap","format":"Manhwa","ranking":"popularity"},{"type":"manga","id":"novels","format":"Novel","ranking":"popularity"},{"type":"manga","id":"community","ranking":"score","extra":[{"name":"skip"}]}]""";
        const string slow = """[{"type":"manga","id":"slow","format":"Manga","ranking":"popularity"}]""";
        const string failure = """[{"type":"manga","id":"retry","format":"Manga","ranking":"popularity"}]""";
        const string generic = """[{"type":"manga","id":"generic","extra":[{"name":"search"},{"name":"status","options":["Completed"]}]}]""";
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

        string[] labels = ["Popular Manga", "Popular Manhwa", "Popular Light Novels", "Top Rated"];
        var screen = context.Render<Manga>();
        screen.WaitForAssertion(() =>
        {
            Check(screen.FindAll(".manga-discovery h2").Select(x => x.TextContent).SequenceEqual(labels), "Manga shelf order changed");
            Check(screen.FindAll(".manga-discovery p").Count(p => p.TextContent.Contains("No enabled add-on")) == 4,
                "All shelves need an add-on empty state");
            Check(screen.Find("a[href='/profile/sources']").TextContent.Contains("Manage add-ons"), "Sources link missing");
            Check(fixture.Calls.IsEmpty, "Discovery called a built-in fallback");
        });
        screen.Dispose();

        foreach (var (name, catalogs) in new[] { ("fast", fast), ("slow", slow), ("offline", failure), ("generic", generic) })
        {
            await store.SaveAddonAsync(new()
            {
                Name = name, Url = $"https://{name}.example/manifest.json",
                ManifestJson = $$"""{"types":["manga"],"resources":["catalog","chapters","pages"],"catalogs":{{catalogs}}}"""
            });
        }

        var genericProvider = new AddonResources((await store.GetAddonsAsync()).Single(addon => addon.Name == "generic"));
        await store.SetSettingAsync("catalogue-source-MANGA", genericProvider.ProviderKey);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/manga?source={genericProvider.ProviderKey}");
        screen = context.Render<Manga>();
        string Shelf(string name) => $".manga-discovery[aria-label='{name}']";
        string[] Titles(string shelf) => screen.FindAll($"{Shelf(shelf)} .poster-card strong").Select(x => x.TextContent).ToArray();
        void Has(string shelf, string title) => screen.WaitForAssertion(() => Check(Titles(shelf).Contains(title), $"Missing {title} in {shelf}"));
        Task Button(string shelf, string startsWith) => screen.InvokeAsync(() => screen
            .FindAll($"{Shelf(shelf)} button").First(element => element.TextContent.Trim().StartsWith(startsWith)).ClickAsync(new()));

        void NoPicker() => Check(screen.FindAll("select[aria-label='Catalogue source'], .manga-discovery select, .manga-provider-picker, .manga-discovery-back").Count == 0
            && !screen.Markup.Contains("Choose a provider") && !screen.Markup.Contains("Browse by provider"),
            "Browse Manga rendered a provider picker");
        async Task Search(string value)
        {
            await screen.InvokeAsync(() => screen.Find("input[type='search']").ChangeAsync(value));
            await screen.InvokeAsync(() => screen.Find("form").SubmitAsync());
        }

        screen.WaitForAssertion(() => Check(!screen.Find("fieldset").HasAttribute("disabled"), "Search did not become usable"));
        Has(labels[0], "Fast Manga");
        NoPicker();
        Check(screen.Find(".design-catalog > form + .manga-discovery h2").TextContent == "Popular Manga",
            "The first shelf did not follow the closed search controls");
        Has(labels[1], "Fast Manhwa");
        Has(labels[2], "Fast Light Novel");
        Has(labels[2], "Explicit Light Novel");
        Has(labels[3], "Provider Rating");
        Check(!fixture.Delayed.Task.IsCompleted, "Fast add-on should render before the slow add-on");
        Check(!screen.Markup.Contains("Generic Novel") && !screen.Markup.Contains("Unclassified Reader"), "Unclassified results were ranked");
        Check(screen.FindAll(".design-provider-title").Count == 0, "Add-on catalogs still render in separate blocks");
        Check(Titles(labels[1]).Count(title => title == "Fast Manhwa") == 1, "Same-provider ID was duplicated");
        Check(!fixture.Calls.Any(uri => uri.AbsolutePath.Contains("/chapters/") || uri.Host is "generic.example" or "graphql.anilist.co"),
            "Discovery fetched a chapter, a generic catalog or a built-in feed");

        fixture.Delayed.SetResult();
        Has(labels[0], "Slow Manga");
        Check(Titles(labels[0]).First() == "Fast Manga", "Late supply shifted a visible card or provider IDs collided");
        Check(Titles(labels[3]).SequenceEqual(new[] { "Provider Rating", "Second Rated" }), "Only declared community-rated order belongs on Top Rated");
        await Button(labels[0], "Retry offline");
        Has(labels[0], "Recovered Manga");
        Check(Titles(labels[0]).Contains("Fast Manga"), "Retry cleared data from another feed");
        Check(fixture.Calls.Count(uri => uri.Host == "offline.example") == 2 &&
            fixture.Calls.Count(uri => uri.AbsolutePath == "/catalog/manga/ranked.json") == 1, "Retry reloaded an unrelated feed");

        var cover = screen.FindComponent<MediaPoster>().FindComponent<CatalogCover>();
        await cover.InvokeAsync(() => cover.Find("img").TriggerEventAsync("onerror", EventArgs.Empty));
        Check(cover.Find("[data-cover-state='failed'] svg") is not null && cover.FindAll("img").Count == 0,
            "A failed shelf cover must leave a placeholder without a broken-image element");

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
        await Button(labels[1], "View all");
        NoPicker();
        Check(Titles(labels[1]).SequenceEqual(new[] { "Fast Manhwa" }), "Expanded catalog duplicated a same-provider title");
        await Button(labels[1], "Show less");

        await Button(labels[3], "View all");
        await Button(labels[3], "More from fast");
        Has(labels[3], "More Rated");
        Check(fixture.Calls.Count(uri => uri.AbsolutePath.Contains("/community/skip=3.json")) == 1, "Rating pagination lost the original provider order or offset");
        await Button(labels[3], "Show less");

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
        NoPicker();
        await screen.InvokeAsync(() => screen.Find("[aria-label='Toggle filters']").ClickAsync(new()));
        Check(screen.Find("#catalog-filters").HasAttribute("hidden") == false, "Content filter panel did not open");
        NoPicker();
        await screen.InvokeAsync(() => screen.Find("#catalog-filters select").ChangeAsync("Action"));
        await screen.InvokeAsync(() => screen.Find("form").SubmitAsync());
        Check(screen.FindAll("[aria-label='Clear Genre filter']").Count == 1, "Content filter was not applied");
        Check(screen.FindAll(".manga-discovery").Count == 0, "Content filter did not switch to results");
        NoPicker();
        await screen.InvokeAsync(() => screen.Find("[aria-label='Clear Genre filter']").ClickAsync(new()));
        Has(labels[0], "Fast Manga");

        await Search("reader");
        screen.WaitForAssertion(() => Check(screen.FindAll(".design-provider-title").Any(x => x.TextContent.Contains("Unclassified Reader")),
            "Unified search missed a searchable unranked Manga add-on"));
        Check(screen.FindAll("label").All(label => !label.TextContent.Contains("Choose status")), "Provider-specific filters appeared in unified results");
        NoPicker();
        var providerCover = screen.FindComponent<ManifestCatalog>().FindComponent<CatalogCover>();
        await providerCover.InvokeAsync(() => providerCover.Find("img").TriggerEventAsync("onerror", EventArgs.Empty));
        Check(providerCover.Find("[data-cover-state='failed'] svg") is not null && providerCover.FindAll("img").Count == 0,
            "A failed search-result cover must leave a placeholder");
        await Search("missing");
        screen.WaitForAssertion(() => Check(screen.Markup.Contains("No matching titles"), "Missing result did not show an empty state"));
        NoPicker();
        await Search("");
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
        screen.Dispose();
        await store.RemoveAddonAsync("https://fast.example/manifest.json");
        await store.SetAddonDisabledAsync("https://slow.example/manifest.json", true);
        await store.SetAddonDisabledAsync("https://offline.example/manifest.json", true);
        var beforeGeneric = fixture.Calls.Count;
        screen = context.Render<Manga>();
        screen.WaitForAssertion(() => Check(screen.FindAll(".manga-discovery p").Count(p => p.TextContent.Contains("No enabled add-on")) == 4,
            "Generic-only setup should have four empty ranked shelves"));
        Check(fixture.Calls.Count == beforeGeneric, "Generic-only discovery queried a feed");
        Console.WriteLine("PASS: Manga add-on-only shelves, empty and generic-only states, removal, classification, catalog ranking, progressive rendering, late results, targeted retry, shared pagination, provider navigation and chapter resume, warm cache, and disabled sources.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Calls { get; } = new();
        public TaskCompletionSource Delayed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int retries;
        private static string Art(int index) => $"https://art.example/midnight-lighthouse-{index}.webp";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            Calls.Enqueue(uri);
            var path = uri.AbsolutePath;
            if (uri.Host == "graphql.anilist.co")
                throw new InvalidOperationException("Ranked Manga discovery must not request AniList.");

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
                "/catalog/manga/community.json" =>
                [
                    new { id = "provider-score", name = "Provider Rating", type = "manga", poster = Art(2) },
                    Card("second-score", "Second Rated", communityScore: 85, art: 3),
                    Card("unrated", "Unrated Entry")
                ],
                "/catalog/manga/community/skip=3.json" => [Card("more-score", "More Rated", communityScore: 75)],
                "/catalog/manga/slow.json" => [Card("shared", "Slow Manga")],
                "/catalog/manga/retry.json" => [Card("again", "Recovered Manga", art: 3)],
                "/catalog/manga/generic/search=reader.json" => [Card("generic", "Unclassified Reader", "Manhwa", 100, 99)],
                _ => []
            };
            return Json(new { metas = cards });
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
