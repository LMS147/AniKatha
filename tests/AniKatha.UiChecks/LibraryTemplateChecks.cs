using System.Net;
using System.Text;
using AniKatha.Core;
using AniKatha.UI.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

internal static class LibraryTemplateChecks
{
    public static async Task RunAsync()
    {
        var store = new LocalStore(Path.Combine(Path.GetTempPath(), $"anikatha-library-{Guid.NewGuid():N}.db3"));
        await store.SaveAsync(new SavedTitle { Id = 41, Title = "The Sky Archive", Kind = "ANIME",
            Poster = "https://preview.example/art/midnight-lighthouse-1.webp", Progress = 6,
            Status = "CURRENT", IsFavourite = true });
        await store.SaveAsync(new SavedTitle { Id = 42, Title = "Across the Tides", Kind = "MANGA",
            Poster = "https://preview.example/art/midnight-lighthouse-2.webp", Progress = 22, Status = "CURRENT" });
        using var http = new HttpClient(new MetadataFixture());
        await using var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(new AniListClient(http));
        var view = context.Render<Library>();

        Task Click(string label) => view.InvokeAsync(() => view.FindAll("button")
            .Single(button => button.TextContent.Trim() == label).ClickAsync(new()));
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        view.WaitForAssertion(() => Check(view.FindAll(".library-recent-item").Count == 2 &&
            view.FindAll(".library-favorite-grid a").Count == 1, "Overview uses profile titles and Favorites."));
        Capture("overview", view.Markup);
        await Click("Anime List");
        view.WaitForAssertion(() => Check(view.Find(".library-item-copy").TextContent.Contains("TV · 2023 · Watching") &&
            view.Find("progress").GetAttribute("max") == "12", "Anime card uses verified format, year and known total."));
        Capture("anime-list", view.Markup);
        await view.InvokeAsync(() => view.Find("[aria-label='Filter anime list']").ClickAsync(new()));
        await view.InvokeAsync(() => view.Find("input[type=search]").InputAsync("absent"));
        Check(view.FindAll(".tracking-library-card").Count == 0, "Search filters the real title list.");
        await Click("Manga List");
        view.WaitForAssertion(() => Check(view.Find(".library-item-copy").TextContent.Contains("22/30 ch"),
            "Manga uses chapter progress and has independent filters."));
        Capture("manga-list", view.Markup);
        await Click("Stats");
        Check(view.Find(".library-breakdown").TextContent.Contains("TV") &&
            view.Find(".library-credits").TextContent.Contains("Sky Studio"), "Anime statistics use real format and main studio values.");
        Capture("anime-stats", view.Markup);
        await Click("Manga");
        Check(view.Find(".library-stats h2").TextContent == "Manga Stats" && !view.Markup.Contains("Sky Studio") &&
            view.Markup.Contains("Mystery"), "Manga statistics use separate metadata.");
        Capture("manga-stats", view.Markup);
        Console.WriteLine("PASS: All five Library templates, saved Favorites, status and search, progress totals, and media-specific statistics.");
    }

    private static void Capture(string name, string markup)
    {
        if (Environment.GetEnvironmentVariable("ANIKATHA_LIBRARY_CAPTURE_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.html"), $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1"><link rel="stylesheet" href="app.css"></head>
            <body><div class="app-shell"><main>{{markup.Replace("https://preview.example/art/", "art/")}}</main>
            <nav class="bottom-nav"><a>Home</a><a>Anime</a><a>Manga</a><a class="active">Library</a><a>Profile</a></nav>
            </div></body></html>
            """);
    }

    private sealed class MetadataFixture : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            var media = body.Contains("\"kind\":\"ANIME\"", StringComparison.Ordinal)
                ? """{"id":41,"type":"ANIME","format":"TV","startDate":{"year":2023},"episodes":12,"genres":["Fantasy"],"studios":{"nodes":[{"name":"Sky Studio"}]},"source":"LIGHT_NOVEL"}"""
                : """{"id":42,"type":"MANGA","format":"MANGA","startDate":{"year":2021},"chapters":30,"genres":["Mystery"],"source":"ORIGINAL"}""";
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"Page":{"media":[""" + media + "]}}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
