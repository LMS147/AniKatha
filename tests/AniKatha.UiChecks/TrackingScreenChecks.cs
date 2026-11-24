using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

internal static class TrackingScreenChecks
{
    internal static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "anikatha-tracking-ui-" + Guid.NewGuid().ToString("N"), "profile.db3");
        var store = new LocalStore(path);
        using var fixture = new Remote();
        using var http = new HttpClient(fixture);
        var account = new AniListAccount(http, fixture);
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(account);
        context.Services.AddSingleton(new AddonClient(http));
        context.Services.AddSingleton(new AniListClient(http));
        context.Services.AddSingleton(new JikanCatalogue(http));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/profile/anilist");
        var view = context.Render<Routes>();
        void Has(string label) => view.WaitForAssertion(() =>
        {
            Check(view.Markup.Contains(label, StringComparison.Ordinal), "Missing " + label);
        });
        Task Open(string route) => view.InvokeAsync(() => navigation.NavigateTo(route));
        Task Click(string label) => view.InvokeAsync(() => view.FindAll("button")
            .Single(element => element.TextContent.Trim() == label).ClickAsync(new()));
        Task Input(string selector, string value) => view.InvokeAsync(() => view.Find(selector).InputAsync(new ChangeEventArgs { Value = value }));
        Task Submit() => view.InvokeAsync(() => view.Find("form").SubmitAsync());

        Has("Connect AniList");
        Capture("anilist-disconnected", view.Markup);
        var connecting = Click("Connect AniList");
        Has("Waiting for AniList sign-in in your browser");
        Check(view.FindAll("button").Single(x => x.TextContent.Trim() == "Connect AniList").HasAttribute("disabled"),
            "A second browser sign-in cannot be started");
        fixture.CompleteBrowser.TrySetCanceled();
        await connecting;
        Has("Sign-in cancelled");
        fixture.CompleteBrowser = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.CompleteBrowser.TrySetResult();
        await Click("Connect AniList");
        Has(Remote.Username);
        Check(view.Find("img.tracking-account-avatar").GetAttribute("src") == "https://example.com/avatar.png", "Uses account avatar");
        await Click("Import lists");
        Has("Checked 2 tracking entries");
        Has("Last list import");
        Has("Refresh lists");
        Has("Open Library");
        await view.InvokeAsync(() => view.Find("img.tracking-account-avatar").TriggerEventAsync("onerror", new EventArgs()));
        Check(view.Find(".tracking-account-avatar").TextContent == "A", "A failed avatar displays initials");
        Capture("anilist-connected", view.Markup);
        await Open("/profile?section=sync");
        Has("Auto-sync completed episodes");
        Has("Last list import");
        var autoSync = view.Find(".profile-sync-toggle input");
        await autoSync.ChangeAsync(false);
        Check(await store.GetSettingAsync(LocalStore.AutomaticAniListKey) == "false", "Sync template keeps the existing preference.");
        await autoSync.ChangeAsync(true);
        Capture("profile-sync-connected", view.Markup);
        await Open("/profile/anilist");
        Has("Refresh lists");
        fixture.Empty = true;
        await Click("Refresh lists");
        Has("No Anime or Manga entries found");
        Check((await store.GetLibraryAsync()).Count == 2, "Empty refresh retains earlier entries");
        fixture.Empty = false;
        fixture.Offline = true;
        await Click("Refresh lists");
        Has("Could not reach AniList");
        Check((await store.GetLibraryAsync()).Count == 2, "Failed refresh retains Library");

        await Open("/library");
        Has("2 of 12 episodes watched");
        Has("4 chapters read");
        Check(view.FindAll(".library-recent-item span.library-template-cover").Count == 2, "Missing covers have a fallback");
        await Click("Anime List");
        Has("Watching");
        await Click("Manga List");
        Has("Reading");
        await Open("/library/title/42/tracking");
        Has("Episodes watched");
        Capture("anilist-editor", view.Markup);
        Check(string.IsNullOrEmpty(view.FindAll("input[type=number]")[1].GetAttribute("value")), "Unrated score is empty");
        await Input("input[inputmode=numeric]", "13");
        await Submit();
        Has("Enter a whole number from 0 to 12.");
        await Input("input[inputmode=numeric]", "3");
        await Input("input[inputmode=decimal]", "8.5");
        await view.Find("select").ChangeAsync(new ChangeEventArgs { Value = "PAUSED" });
        await Submit();
        Has("Saved on this device · Waiting to sync");
        Capture("anilist-pending", view.Markup);
        Check((await new LocalStore(path).AniListEditsAsync(7)).Count == 3, "All fields remain pending after reopening SQLite");
        Check(!view.Markup.Contains("Synced to AniList", StringComparison.Ordinal), "Offline must not invent a confirmation");
        await Open("/library");
        Has("Saved on this device · Waiting to sync");
        await Open("/library/title/42/tracking");
        Has("Saved on this device · Waiting to sync");
        await Input("input[inputmode=numeric]", "11");
        await Open("/library");
        await Open("/library/title/42/tracking");
        view.WaitForAssertion(() => Check(view.Find("input[inputmode=numeric]").GetAttribute("value") == "3", "Leaving discards unsaved edits"));

        fixture.Offline = false;
        await account.SyncPendingAsync(store); // The same path the mobile connectivity handler uses.
        Has("Synced to AniList");
        Check(fixture.Updates.GetValueOrDefault("status") == "PAUSED" &&
            fixture.Updates.GetValueOrDefault("progress") == "3" &&
            fixture.Updates.GetValueOrDefault("score") == "8.5", "Existing AniList mutations acknowledged all fields");
        await Click("Reset completed count to zero");
        await Submit();
        Has("Save the lower completed count?");
        await Click("Confirm lower count");
        Has("Synced to AniList");
        Check(fixture.Updates["progress"] == "0", "Confirmed intentional reset syncs");
        await Open("/library/title/44/tracking");
        Has("Chapters read");
        Has("The total is unknown.");
        await store.SavePlaybackPositionAsync("exact-position", 73.125);
        fixture.Expired = true;
        await Open("/profile/anilist");
        Has("Reconnect required");
        await Click("Disconnect AniList");
        Has("will remain on this device");
        await Click("Keep connected");
        Has("Reconnect required");
        await Click("Disconnect AniList");
        await Click("Disconnect account");
        Has("AniList disconnected.");
        Check((await store.GetLibraryAsync()).Count == 2 && await store.GetPlaybackPositionAsync("exact-position") == 73.125,
            "Disconnection keeps Library and exact resume");
        Console.WriteLine("PASS: normal navigation, cancelled browser flow, import/empty/offline, known and unknown totals, draft, durable edits, live acknowledgement, reset, expiry and disconnect.");
    }

    private static void Capture(string filename, string markup)
    {
        var folder = Environment.GetEnvironmentVariable("ANIKATHA_TRACKING_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, filename + ".html"), """
            <!doctype html><html lang="en"><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <link rel="stylesheet" href="app.css"><body>
            """ + markup + "</body></html>");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Remote : HttpMessageHandler, IAniListAuthorization
    {
        internal const string Username = "A-very-long-AniList-username-for-a-narrow-phone";
        private AniListCredential? credential;
        public string? ClientId => "123";
        public Uri Callback { get; } = new("co.zyma.anikatha://anilist/callback");
        public bool Offline { get; set; }
        public bool Empty { get; set; }
        public bool Expired { get; set; }
        public TaskCompletionSource CompleteBrowser { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dictionary<string, string> Updates { get; } = new(StringComparer.Ordinal);
        public async Task<Uri> AuthorizeAsync(Uri request, CancellationToken token)
        {
            await CompleteBrowser.Task.WaitAsync(token);
            var state = request.Query.Split('&').Single(item => item.StartsWith("state=", StringComparison.Ordinal))[6..];
            return new Uri(Callback + "#access_token=fixture&expires_in=3600&state=" + state);
        }
        public Task<AniListCredential?> ReadAsync() => Task.FromResult(Expired && credential is { } saved
            ? saved with { Expires = DateTimeOffset.UtcNow.AddMinutes(-1) } : credential);
        public Task WriteAsync(AniListCredential value) { credential = value; Expired = false; return Task.CompletedTask; }
        public void Clear() { credential = null; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Offline) throw new HttpRequestException("Offline fixture.");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = doc.RootElement.GetProperty("query").GetString()!;
            var variables = doc.RootElement.GetProperty("variables");
            if (query.Contains("Viewer {")) return Json(new { Viewer = new { id = 7, name = Username, avatar = new { large = "https://example.com/avatar.png" }, mediaListOptions = new { scoreFormat = "POINT_10_DECIMAL" } } });
            if (query.Contains("mediaList(userId:"))
            {
                var anime = variables.GetProperty("kind").GetString() == "ANIME";
                var id = anime ? 42 : 44;
                object[] list = Empty ? [] :
                [
                    new { mediaId = id, status = "CURRENT", progress = anime ? 2 : 4, score = 0, media = new
                    { id, type = anime ? "ANIME" : "MANGA", episodes = anime ? 12 : (int?)null, chapters = (int?)null,
                      title = new { userPreferred = anime ? "The light in the sky from a very long seasonal anime title" : "Midnight pages" },
                      coverImage = new { large = (string?)null } } }
                ];
                return Json(new { Page = new { pageInfo = new { hasNextPage = false }, mediaList = list } });
            }
            var mediaId = variables.GetProperty("id").GetInt32();
            if (query.Contains("SaveMediaListEntry"))
            {
                var field = new[] { "status", "score", "progress" }.Single(x => query.Contains(x + ": $value"));
                Updates[field] = variables.GetProperty("value").ToString();
                return Json(new { SaveMediaListEntry = new { mediaId } });
            }
            return Json(new { Media = new { id = mediaId, mediaListEntry = new { id = mediaId + 100, progress = 2 } } });
        }
        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data = value })) };
    }
}
