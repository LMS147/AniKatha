using System.Net;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

internal static class DownloadChecks
{
    public static async Task RunAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "anikatha-downloads-" + Guid.NewGuid());
        var store = new LocalStore(Path.Combine(folder, "library.db3"));
        var files = Path.Combine(folder, "assets");
        var addon = new InstalledAddon
        {
            Name = "Download Fixture", Url = "https://provider.example/manifest.json",
            ManifestJson = """{"types":["manga","anime"],"resources":["pages","stream"]}"""
        };
        await store.SaveAddonAsync(addon);
        var provider = new AddonResources(addon).ProviderKey;
        var manga = new DownloadSelection(provider, "manga", "1", "chapter-1", provider, "Book", "Chapter 1");
        var anime = new DownloadSelection(provider, "anime", "1", "episode-1", provider, "Show", "Episode 1", "file");
        var cancel = manga with { UnitId = "cancel" };
        var damaged = manga with { UnitId = "broken" };
        using var handler = new Provider();
        using var http = new HttpClient(handler);
        var client = new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.5") }));
        var downloads = new OfflineDownloads(store, client, files);

        await downloads.QueueAsync(cancel);
        await handler.Cancelling.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await downloads.CancelAsync(cancel.DownloadId);
        await WaitForState(downloads, cancel, DownloadState.Cancelled);
        Assert(await downloads.OpenAsync(cancel.DownloadId) is null, "Cancelled files are not playable");

        await downloads.QueueAsync(damaged);
        await WaitForState(downloads, damaged, DownloadState.Failed);
        Assert(await downloads.OpenAsync(damaged.DownloadId) is null, "A damaged page leaves the whole chapter incomplete");

        Directory.CreateDirectory(Path.Combine(files, "staging", manga.DownloadId));
        await File.WriteAllTextAsync(Path.Combine(files, "staging", manga.DownloadId, "incomplete.part"), "partial");
        await store.SetSettingAsync("offline-job:" + manga.DownloadId, JsonSerializer.Serialize(new DownloadJob(manga, DownloadState.Downloading)));
        var reopenedStore = new LocalStore(Path.Combine(folder, "library.db3"));
        var reopened = new OfflineDownloads(reopenedStore, client, files);
        await reopened.StartAsync();
        await reopened.QueueAsync(anime);
        await reopened.QueueAsync(anime);
        await WaitForState(reopened, manga, DownloadState.Complete);
        await WaitForState(reopened, anime, DownloadState.Complete);
        var saved = await reopened.ListAsync();
        Assert(saved.Count(x => x.Selection == anime) == 1, "Duplicate job collapsed");
        Assert((await reopened.OpenAsync(manga.DownloadId))?.Assets?.Select(x => x.Name).SequenceEqual(["page-0001.png", "page-0002.png"]) == true,
            "Chapter restored from interrupted queue in page order");
        Assert(reopened.UsedBytes > 0 && reopened.AvailableBytes > 0, "Storage accounting uses the actual filesystem");
        Assert(reopened.FindAsset(manga.DownloadId, "../video.mp4") is null, "Asset route rejects paths outside the job");

        await reopenedStore.SaveReadingPageAsync(manga.ProgressKey, 2);
        await reopenedStore.SavePlaybackPositionAsync(anime.ProgressKey, 17.25);
        await reopenedStore.SaveProviderTitleAsync(provider, "manga", manga.TitleId, manga.Title);
        handler.Offline = true;
        var calls = handler.Calls;
        await reopenedStore.SetAddonDisabledAsync(addon.Url, true);
        await using var context = new BunitContext();
        context.Services.AddSingleton(reopenedStore);
        context.Services.AddSingleton(reopened);
        context.Services.AddSingleton(client);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/library?view=downloads");
        var library = context.Render<Library>();
        library.WaitForAssertion(() => Assert(library.FindAll("a").Count(x => x.TextContent.Contains("Open offline")) == 2, "Both downloads are visible"));
        var book = context.Render<OfflineItem>(p => p.Add(x => x.Id, manga.DownloadId));
        book.WaitForAssertion(() => Assert(book.Find(".reader-page").GetAttribute("src")?.EndsWith("/page-0002.png") == true,
            "Offline reader restores the existing page"));
        var show = context.Render<OfflineItem>(p => p.Add(x => x.Id, anime.DownloadId));
        show.WaitForAssertion(() =>
        {
            var player = show.FindComponent<WatchPreview>().Instance;
            Assert(player.VideoUrl.EndsWith("/video.mp4") && player.EpisodeKey == anime.ProgressKey, "Offline player uses the same key");
        });
        Assert(handler.Calls == calls, "Opening either offline item does not contact a provider");
        await reopened.RemoveAsync(manga.DownloadId);
        await reopened.RemoveAsync(anime.DownloadId);
        Assert(await reopened.GetDownloadCountAsync() == 0, "Both completed files removed");
        Assert((await reopenedStore.GetLibraryAsync()).Count == 1 &&
            await reopenedStore.GetReadingPageAsync(manga.ProgressKey) == 2 &&
            await reopenedStore.GetPlaybackPositionAsync(anime.ProgressKey) == 17.25, "Remove download leaves library and precise progress");
        Console.WriteLine("PASS: persistent queue, interrupted recovery, cancellation, invalid files, source identity, offline Library and precise progress after deletion.");
    }

    private static async Task<int> GetDownloadCountAsync(this OfflineDownloads service) =>
        (await service.ListAsync()).Count(x => x.State == DownloadState.Complete);

    private static async Task WaitForState(OfflineDownloads service, DownloadSelection selection, DownloadState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            var job = (await service.ListAsync()).FirstOrDefault(x => x.Id == selection.DownloadId);
            if (job?.State == expected) return;
            await Task.Delay(20, timeout.Token);
        }
        throw new InvalidOperationException("Download did not reach " + expected);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Provider : HttpMessageHandler
    {
        public bool Offline { get; set; }
        public int Calls { get; private set; }
        public TaskCompletionSource Cancelling { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static readonly byte[] Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXuIAAAAASUVORK5CYII=");
        private static readonly byte[] Video = [0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 105, 115, 111, 109, 109, 112, 52, 49];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
        {
            Calls++;
            if (Offline) throw new HttpRequestException("Fixture offline");
            var path = message.RequestUri!.AbsolutePath;
            if (path == "/cancel.png")
            {
                Cancelling.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            HttpContent body = path switch
            {
                "/pages/manga/cancel.json" => new StringContent("""{"pages":["https://provider.example/cancel.png"]}"""),
                "/pages/manga/broken.json" => new StringContent("""{"pages":["https://provider.example/1.png","https://provider.example/broken.png"]}"""),
                "/pages/manga/chapter-1.json" => new StringContent("""{"pages":["https://provider.example/1.png","https://provider.example/2.png"]}"""),
                "/stream/anime/episode-1.json" => new StringContent("""{"streams":[{"id":"file","downloadable":true,"url":"https://provider.example/video.mp4"}]}"""),
                "/video.mp4" => new ByteArrayContent(Video),
                "/broken.png" => new StringContent("error page"),
                _ => new ByteArrayContent(Image)
            };
            return new(HttpStatusCode.OK) { Content = body };
        }
    }
}
