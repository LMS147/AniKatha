using System.Net;
using AniKatha.Core;
using AniKatha.UI;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

internal static class ProfileUiChecks
{
    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "anikatha-profile-ui-" + Guid.NewGuid().ToString("N"));
        using var handler = new SourceFixture();
        using var http = new HttpClient(handler);
        var addons = new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.8") }));
        var profiles = new LocalProfiles(path, "legacy.db3", addons);
        await profiles.InitializeAsync();
        await profiles.Session!.Store.SaveAddonAsync(new InstalledAddon
        {
            Url = "https://provider.example/manifest.json",
            Name = "Original source",
            ManifestJson = """{"types":["anime"],"resources":["stream"]}"""
        });
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(profiles);
        context.Services.AddSingleton(addons);
        context.Services.AddSingleton(new AniListClient(http));
        context.Services.AddSingleton(new JikanCatalogue(http));
        var location = context.Services.GetRequiredService<NavigationManager>();
        location.NavigateTo("/profile");
        var view = context.Render<Routes>();
        void Has(string value) => view.WaitForAssertion(() =>
        {
            if (!view.Markup.Contains(value, StringComparison.Ordinal)) throw new Exception("Missing " + value);
        });
        async Task Go(string uri) => await view.InvokeAsync(() => location.NavigateTo(uri));
        async Task Click(string label) => await view.InvokeAsync(() => view.FindAll("button")
            .Single(x => x.TextContent.Trim() == label || x.GetAttribute("aria-label") == label).ClickAsync(new()));
        Has("Story explorer");
        Has("/profile/sources");
        await Go("/profile/profiles/new");
        await view.Find("label input").InputAsync(new ChangeEventArgs { Value = "Late night reader" });
        await Click("Moon");
        await view.Find("form").SubmitAsync();
        Has("Late night reader");
        await view.FindAll(".settings-row").Single(x => x.TextContent.Contains("Late night reader")).ClickAsync(new());
        Has("On this device");
        await Go("/profile/sources");
        Has("No sources installed");
        await Go("/profile/appearance");
        await Click("Light");
        await Click("Ocean");
        await Click("Compact");
        await Click("Cancel preview");
        if (view.Find(".appearance-preview").GetAttribute("data-theme") != "system")
            throw new Exception("Cancel did not restore saved preview");
        await Click("Light");
        await Click("Ocean");
        await Click("Compact");
        await Click("Save appearance");
        Has("Appearance saved.");
        if (view.Find(".app-shell").GetAttribute("data-theme") != "light")
            throw new Exception("Save did not update the shell");
        await Go("/profile/profiles");
        await view.FindAll(".settings-row").Single(x => x.TextContent.Contains("Story explorer")).ClickAsync(new());
        Has("Story explorer");
        if (view.Find(".app-shell").GetAttribute("data-theme") != "system")
            throw new Exception("Another profile's theme leaked to original");
        await Go("/profile/sources");
        Has("Original source");
        async Task CheckManifest(string address)
        {
            await view.Find("#source-manifest").InputAsync(new ChangeEventArgs { Value = address });
            await view.Find("form").SubmitAsync();
        }
        await CheckManifest("https://provider.example/manifest.json");
        Has("already installed");
        await CheckManifest("http://provider.example");
        Has("Use a public HTTPS URL");
        await CheckManifest("https://manga.example/manifest.json");
        Has("Chapter pages");
        await Click("Add source");
        Has("Added Manga source.");
        var manga = () => view.FindAll(".source-card").Single(x => x.TextContent.Contains("Manga source"));
        await manga().QuerySelectorAll("button").Single(x => x.TextContent == "Move up").ClickAsync(new());
        Has("Source moved up.");
        if ((await profiles.Session!.Store.GetAddonsAsync(true))[0].Name != "Manga source")
            throw new Exception("Profile source order did not persist.");
        await manga().QuerySelectorAll("button").Single(x => x.TextContent == "Refresh").ClickAsync(new());
        Has("Manifest refreshed.");
        await manga().QuerySelectorAll("button").Single(x => x.TextContent == "Remove").ClickAsync(new());
        Has("downloaded files stay");
        await Click("Remove source");
        Has("Removed Manga source.");
        if ((await profiles.Session!.Store.GetAddonsAsync(true)).Count != 1)
            throw new Exception("Removal affected another source.");
        Console.WriteLine("PASS: Discoverable profile hub, editor, switcher, isolated Sources and Appearance preview/cancel/save.");
    }

    private sealed class SourceFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host != "manga.example") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"manga","name":"Manga source","version":"1","types":["manga"],"resources":["pages","chapters","catalog"],
                     "catalogs":[{"id":"stories","type":"manga","extra":[{"name":"search"}]}]}
                    """)
            };
            return Task.FromResult(response);
        }
    }
}
