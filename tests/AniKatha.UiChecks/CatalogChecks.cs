using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using AniKatha.UI.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

internal static class CatalogChecks
{
    public static async Task RunAsync(LocalStore store)
    {
        foreach (var (name, type, extras) in new[]
        {
            ("fast", "anime", """[{"name":"search"},{"name":"skip"}]"""),
            ("slow", "series", """[{"name":"search"}]"""),
            ("manga", "manga", """[{"name":"search"}]"""),
            ("browse", "anime", "[]"),
            ("required", "anime", """[{"name":"search"},{"name":"genre","isRequired":true,"options":["Action & Adventure"]}]""")
        })
        {
            await store.SaveAddonAsync(new InstalledAddon
            {
                Name = name, Url = $"https://{name}.example/manifest.json",
                ManifestJson = $$"""{"types":["{{type}}"],"resources":["catalog"],"catalogs":[{"type":"{{type}}","id":"stories","extra":{{extras}}}]}"""
            });
        }

        using var handler = new CatalogHandler();
        using var http = new HttpClient(handler);
        await using var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.9") })));
        context.Services.AddSingleton(new AniListClient(http));
        context.Services.AddSingleton(new JikanCatalogue(http));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var session = new CatalogSession();
        var root = context.Render<CascadingValue<CatalogSession>>(p => p.Add(x => x.Value, session));
        IRenderedComponent<CascadingValue<CatalogSession>> Show(string kind)
        {
            root.Render(p => p.Add(x => x.Value, session).Add(x => x.ChildContent, builder =>
            {
                builder.OpenComponent<Catalog>(0);
                builder.SetKey(kind);
                builder.AddComponentParameter(1, nameof(Catalog.Kind), kind);
                builder.CloseComponent();
            }));
            return root;
        }
        async Task Search(string query)
        {
            root.Find("[aria-label='Search titles']").Change(query);
            await root.Find("form").SubmitAsync();
        }
        void Expect(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }
        void Contains(string text) => root.WaitForAssertion(() => Expect(root.Markup.Contains(text), "Expected " + text));
        bool Called(string host, string path) => handler.Calls.Any(x => x.Host == host && x.AbsolutePath.Contains(path));

        Show("ANIME");
        Contains("browse initial");
        await Search("old");
        Contains("fast old");
        await Search("current");
        Contains("fast current");
        Contains("supports browsing only");
        Expect(!Called("manga.example", "/catalog/"), "Anime requested a manga catalog");
        Expect(!Called("required.example", "/catalog/"), "Required catalog fetched before selection");
        handler.Old.SetResult(CatalogHandler.Result("stale answer"));
        await handler.Old.Task;
        Expect(!root.Markup.Contains("stale answer"), "Stale results replaced the current query");
        handler.Slow.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Contains("Catalogue request failed.");
        Contains("fast current");
        root.FindAll("button").Single(x => x.TextContent.Trim() == "Retry").Click();
        Contains("slow recovered");
        Expect(!root.Markup.Contains("wrong media"), "Cross-media item was displayed");

        root.FindAll("select").Single(x => x.TextContent.Contains("Choose genre")).Change("Action & Adventure");
        Contains("required current");
        Expect(Called("required.example", "genre=Action%20%26%20Adventure"), "Required extra was not escaped and sent");
        Expect(!Called("fast.example", "genre="), "Extra leaked to another provider");

        root.FindAll("button").Single(x => x.TextContent.Trim() == "Load more").Click();
        Contains("fast page two");
        root.Find("[aria-label='Search titles']").Change("anime draft");
        Show("MANGA");
        await Search("books");
        Contains("manga books");
        Expect(!Called("fast.example", "search=books"), "Manga requested an anime catalog");
        Show("ANIME");
        Contains("fast page two");
        Expect(root.Find("[aria-label='Search titles']").GetAttribute("value") == "anime draft", "Anime draft was not restored");
        Expect(handler.Calls.Count(x => x.Host == "fast.example" && x.AbsolutePath.Contains("search=current")) == 2,
            "Restored Anime results made unnecessary requests");

        var provider = (await store.GetAddonsAsync()).Single(x => x.Name == "fast");
        var sourceKey = new AddonResources(provider).ProviderKey;
        root.Find("[aria-label='Catalogue source']").Change(sourceKey);
        Contains("fast anime draft");
        Expect(!Called("slow.example", "search=anime%20draft"), "Selected source did not limit requests");
        Show("MANGA");
        await store.SetAddonDisabledAsync(provider.Url, true);
        Show("ANIME");
        Contains("slow recovered");
        Expect(!root.FindAll("option").Any(x => x.GetAttribute("value") == sourceKey), "Disabled source still selectable");
        Console.WriteLine("PASS: unified Anime and Manga, required extras, independent requests and retries, pagination, draft restore, source filter, disabled source.");
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        internal ConcurrentQueue<Uri> Calls { get; } = new();
        internal TaskCompletionSource<HttpResponseMessage> Old { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<HttpResponseMessage> Slow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int slowRequests;

        internal static HttpResponseMessage Result(string title, string? type = null) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                metas = new[] { new { id = title, name = title, type }, new { id = "wrong", name = "wrong media", type = (string?)"mixed" } }
            }))
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Calls.Enqueue(uri);
            var path = uri.AbsolutePath;
            var provider = uri.Host.Split('.')[0];
            if (provider == "graphql")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"data":{"Page":{"pageInfo":{"hasNextPage":false},"media":[]}}}""") });
            if (provider == "slow" && path.Contains("search=old")) return Old.Task;
            if (provider == "slow" && path.Contains("search=current") && Interlocked.Increment(ref slowRequests) == 1)
                return Slow.Task;
            if (provider == "slow" && path.Contains("search=")) return Task.FromResult(Result("slow recovered", "series"));
            var query = path.Split("search=").Last().Split('&', '.')[0];
            var title = path.Contains("skip=") ? $"{provider} page two" :
                !path.Contains("search=") ? $"{provider} initial" : $"{provider} {Uri.UnescapeDataString(query)}";
            return Task.FromResult(Result(title));
        }
    }
}
