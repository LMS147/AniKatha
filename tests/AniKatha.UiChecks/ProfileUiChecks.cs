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
        void Capture(string screen)
        {
            if (Environment.GetEnvironmentVariable("ANIKATHA_PROFILE_CAPTURE_DIRECTORY") is not { Length: > 0 } folder) return;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, screen + ".html"), $$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <link rel="stylesheet" href="app.css"><title>AniKatha Profile</title>
                </head><body>{{view.Markup}}</body></html>
                """);
        }
        async Task Go(string uri) => await view.InvokeAsync(() => location.NavigateTo(uri));
        async Task Click(string label) => await view.InvokeAsync(() => view.FindAll("button")
            .Single(x => x.TextContent.Trim() == label || x.GetAttribute("aria-label") == label).ClickAsync(new()));
        Has("Story explorer");
        Has("/profile/sources");
        await view.Find("#profile-username").InputAsync("!");
        Has("Use 3–10 letters or digits");
        await view.Find("#profile-username").InputAsync("Reader27");
        await view.Find("#profile-display-name").InputAsync("Story keeper");
        await Click("Preferences");
        Has("Dim watched episodes");
        Capture("preferences");
        await view.FindAll(".profile-switch input").First().ChangeAsync(true);
        if (await profiles.Session!.Store.GetSettingAsync(MediaPreferences.PlayOnOpenKey) != "true")
            throw new Exception("Play-on-open preference was not persisted");
        await Click("Sync");
        Has("AniKatha JSON");
        Capture("sync");
        await Click("Import");
        Has("Choose an AniKatha backup");
        await Click("Security");
        Has("Secure PIN storage is unavailable on this platform.");
        Capture("security");
        await Click("Account");
        if (view.Find("#profile-display-name").GetAttribute("value") != "Story keeper")
            throw new Exception("Account draft was lost");
        await view.Find(".profile-form").SubmitAsync();
        Has("Profile saved.");
        Capture("account");
        var reloaded = new LocalProfiles(path, "legacy.db3", addons);
        await reloaded.InitializeAsync();
        if (reloaded.Active.Name != "Story keeper" || reloaded.Active.Details?.Username != "Reader27")
            throw new Exception("Account fields did not survive restart");
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
        if (view.Find(".appearance-preview").GetAttribute("data-theme") != "dark")
            throw new Exception("Cancel did not restore saved preview");
        await Click("Light");
        await Click("Ocean");
        await Click("Compact");
        await Click("Save appearance");
        Has("Appearance saved.");
        if (view.Find(".app-shell").GetAttribute("data-theme") != "light")
            throw new Exception("Save did not update the shell");
        await Go("/profile/profiles");
        await view.FindAll(".settings-row").Single(x => x.TextContent.Contains("Story keeper")).ClickAsync(new());
        Has("Story keeper");
        if (view.Find(".app-shell").GetAttribute("data-theme") != "dark")
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
