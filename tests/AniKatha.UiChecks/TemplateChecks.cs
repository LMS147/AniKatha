using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using AniKatha.UI.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

internal static class TemplateChecks
{
    public static async Task RunAsync()
    {
        using var handler = new Feed();
        using var http = new HttpClient(handler);
        await using var context = new BunitContext();
        context.Services.AddSingleton(new AniListClient(http));
        context.Services.AddSingleton(new AddonClient(http));
        context.Services.AddSingleton(new LocalStore(Path.Combine(Path.GetTempPath(), $"anikatha-templates-{Guid.NewGuid():N}.db3")));

        var shelves = context.Render<CatalogDiscovery>();
        shelves.WaitForAssertion(() =>
        {
            Check(shelves.FindAll(".catalog-shelf").Count == 4, "Anime renders four separate horizontal shelves.");
        });
        var button = shelves.FindAll("button").First(element => element.TextContent.Contains("View all"));
        await shelves.InvokeAsync(() => button.Click());
        Check(shelves.FindAll(".catalog-shelf").Count == 3, "View all expands only the selected shelf.");

        var anime = context.Render<AnimeDetails>(parameters => parameters.Add(component => component.Id, 172));
        anime.WaitForAssertion(() => Check(anime.Find("h1").TextContent == "Fixture anime" &&
            anime.Find("a[href='#anime-sources']").TextContent.Contains("Find episodes"),
            "Anime has a standalone details page with a real source section."));

        var manga = context.Render<MangaDetails>(parameters => parameters.Add(component => component.Id, 173));
        manga.WaitForAssertion(() => Check(manga.Find("h1").TextContent == "Fixture manga" &&
            manga.Find("#chapters").TextContent.Contains("Chapters") && manga.Markup.Contains("Adventure"),
            "Manga displays the title summary and chapter section using provider data."));
        Console.WriteLine("PASS: Template discovery shelves and standalone Anime and Manga detail routes.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Feed : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var variables = body.RootElement.GetProperty("variables");
            var type = variables.GetProperty("type").GetString()!;

            object Title(int id) => new
            {
                id, type, title = new { userPreferred = "Fixture " + type.ToLowerInvariant() }, startDate = new { year = 2026 },
                coverImage = new { large = "https://covers.example/title.webp" }, description = "An example story.",
                averageScore = 87, genres = new[] { "Adventure" }
            };

            object data = variables.TryGetProperty("id", out var id) ? new { Media = Title(id.GetInt32()) } :
                new { Page = new { pageInfo = new { hasNextPage = false }, media = Enumerable.Range(1, 4).Select(Title) } };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data })) };
        }
    }
}
