using System.Globalization;
using System.Net;
using System.Text.Json;
using AniKatha.Core;

internal static class AniListAccountChecks
{
    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anikatha-account-{Guid.NewGuid()}.db3");
        var store = new LocalStore(path);
        using var fixture = new Remote();
        using var http = new HttpClient(fixture);
        var auth = new Authorization();
        var account = new AniListAccount(http, auth);

        auth.BadState = true;
        try { await account.ConnectAsync(); throw new Exception("Invalid authorization state accepted"); }
        catch (AniListAuthorizationException) { }
        auth.BadState = false;
        var viewer = await account.ConnectAsync();
        Assert(viewer.Id == 7 && viewer.ScoreFormat == "POINT_10_DECIMAL", "Viewer and scale");
        Assert(AniListAccount.ScoreScale(viewer.ScoreFormat) == (10d, 0.1d), "Decimal scale");

        await store.SaveProviderTitleAsync("local-provider", "manga", "42", "Local manga");
        await store.SaveReadingPageAsync("manga:local-position", 7);
        await store.SavePlaybackPositionAsync("anime:local-position", 125.75);
        Assert(await account.ImportAsync(store, viewer.Id) == 3, "Import both kinds across pages");
        Assert(await account.ImportAsync(store, viewer.Id) == 3 && (await store.GetLibraryAsync()).Count == 4,
            "Repeated import is idempotent and leaves provider titles intact");
        Assert(await store.GetAniListScoreAsync(viewer.Id, 42, viewer.ScoreFormat) == 8.4, "Imported score");
        Assert(await store.GetAniListScoreAsync(viewer.Id, 44, viewer.ScoreFormat) is null, "Unset score");

        fixture.Offline = true;
        await store.QueueAniListEditAsync(viewer.Id, 42, "progress", "3");
        Assert(await account.SyncPendingAsync(store) == 0, "Offline retains queue");
        store = new LocalStore(path);
        Assert((await store.AniListEditsAsync(viewer.Id)).Single(x => x.Field == "progress").Failed, "Queue persists on reopen");
        await store.QueueAniListEditAsync(viewer.Id, 42, "progress", "0");
        await store.QueueAniListEditAsync(viewer.Id, 42, "score", "0", viewer.ScoreFormat);
        await store.QueueAniListEditAsync(8, 43, "status", "PAUSED");

        fixture.Offline = false;
        fixture.Progress = 9;
        fixture.Status = "COMPLETED";
        await account.ImportAsync(store, viewer.Id);
        var title = (await store.GetLibraryAsync()).Single(x => x.Id == 42);
        Assert(title.Status == "COMPLETED" && title.Progress == 0, "Remote status merged and deliberate local reset preserved");
        Assert(await account.SyncPendingAsync(store) == 2 && fixture.Progress == 0 && fixture.Score == 0,
            "Progress reset and clearing score reach AniList");
        Assert((await store.AniListEditsAsync(8)).Count == 1, "Other account revisions untouched");

        await store.QueueAniListEditAsync(viewer.Id, 42, "progress", "1");
        fixture.DelayNextProgress = true;
        var oldSend = account.SyncPendingAsync(store);
        await fixture.Sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.QueueAniListEditAsync(viewer.Id, 42, "progress", "2");
        fixture.Release.TrySetResult();
        await oldSend;
        Assert((await store.AniListEditsAsync(viewer.Id)).Single().Value == "2", "Older acknowledgement keeps newer edit");
        await account.SyncPendingAsync(store);
        Assert(fixture.Progress == 2 && (await store.AniListEditsAsync(viewer.Id)).Count == 0, "Newest revision acknowledged");

        await store.QueueAniListEditAsync(viewer.Id, 42, "status", "PAUSED");
        await account.SyncPendingAsync(store);
        Assert(fixture.Status == "PAUSED", "Local status pushed");
        fixture.Status = "CURRENT";
        await account.ImportAsync(store, viewer.Id);
        Assert((await store.GetLibraryAsync()).Single(x => x.Id == 42).Status == "CURRENT", "Remote status imported after prior acknowledgement");

        await AddonProgressAsync(store, account, fixture, viewer.Id, path);

        await store.QueueAniListEditAsync(8, 43, "progress", "1");
        auth.ChangeViewer(8);
        auth.Expire();
        var expiredCalls = fixture.Calls;
        Assert(await account.SyncPendingAsync(store) == 0 && fixture.Calls == expiredCalls &&
               (await store.AniListEditsAsync(8)).Any(edit => edit.Field == "progress"),
            "Expired authorization preserves the queued account edits");
        auth.ChangeViewer(7);
        await account.SyncPendingAsync(store);
        Assert((await store.AniListEditsAsync(8)).Any(edit => edit.Field == "progress"), "Switching accounts does not replay a different account's edits");

        fixture.Revoked = true;
        try { await account.CurrentAsync(); throw new Exception("Revoked token accepted"); }
        catch (AniListAuthorizationException) { }
        await account.DisconnectAsync();
        var calls = fixture.Calls;
        Assert(await account.CurrentAsync() is null && await account.SyncPendingAsync(store) == 0 && fixture.Calls == calls,
            "Disconnected account makes no authenticated requests");
        Assert(await store.GetReadingPageAsync("manga:local-position") == 7 &&
               await store.GetPlaybackPositionAsync("anime:local-position") == 125.75, "Precise progress survived account changes");
        Console.WriteLine("PASS: AniList callback state, pagination, idempotent import, score scale, durable queue, reset conflicts, stale acknowledgement, revocation and disconnect.");
    }

    private static async Task AddonProgressAsync(LocalStore store, AniListAccount account, Remote fixture, int viewer, string path)
    {
        async Task<bool> Complete(string type, string unit, AddonAniListProgress? mapping, int? accountId)
        {
            var key = ResumeEntry.Key("origin", type, "title", unit, "source");
            await store.SaveAddonAniListProgressAsync(key, mapping);
            var queued = await store.CompleteAddonUnitAsync(key, accountId);
            Assert(await store.IsUnitCompleteAsync(key), "Every completion stays in local history");
            return queued;
        }

        Assert(!await Complete("anime", "unmapped", null, viewer), "Legacy add-on remains local");
        Assert(!await Complete("anime", "wrong-type", new(44, "ANIME", 1), viewer), "Reject cross-media mappings");
        Assert(!await Complete("anime", "other-viewer", new(42, "ANIME", 3), 99), "Require linked account baseline");
        Assert(!await Complete("anime", "jump", new(42, "ANIME", 9), viewer), "A skipped episode cannot mark intervening episodes complete");
        Assert(await Complete("anime", "episode-3", new(42, "ANIME", 3), viewer), "Queue the next completed episode");
        fixture.Offline = true;
        Assert(await account.SyncPendingAsync(store) == 0, "Offline retains automatic progress");
        Assert(await Complete("anime", "episode-4", new(42, "ANIME", 4), viewer), "Queue consecutive offline completion");
        store = new LocalStore(path);
        Assert((await store.AniListEditsAsync(viewer)).Single() is { ExpectedProgress: 2, Value: "4" },
            "Keep the prior baseline and latest consecutive completion across restart");
        fixture.Offline = false;
        Assert(await account.SyncPendingAsync(store) == 1 && fixture.Progress == 4, "Retry sends the completed count");

        Assert(await Complete("anime", "episode-5", new(42, "ANIME", 5), viewer), "Queue before remote progress changes");
        fixture.Progress = 7;
        await account.SyncPendingAsync(store);
        Assert(fixture.Progress == 7 && (await store.GetLibraryAsync()).Single(x => x.Id == 42).Progress == 7,
            "Automatic playback cannot reduce a higher remote count");

        Assert(await Complete("anime", "episode-8", new(42, "ANIME", 8), viewer), "Queue before remote reset");
        fixture.Progress = 0;
        await account.SyncPendingAsync(store);
        Assert(fixture.Progress == 0 && (await store.GetLibraryAsync()).Single(x => x.Id == 42).Progress == 0,
            "Automatic playback follows an external reset");
        await store.QueueAniListEditAsync(viewer, 42, "progress", "2");
        Assert(!await Complete("anime", "episode-3", new(42, "ANIME", 3), viewer), "Replay stays complete locally");
        Assert(!await Complete("anime", "different-source-3", new(42, "ANIME", 3), viewer),
            "Automatic progress cannot replace a pending manual edit");
        await account.SyncPendingAsync(store);

        Assert(!await Complete("manga", "chapter-2", new(44, "MANGA", 2), viewer) && (await store.AniListEditsAsync(viewer)).Count == 0,
            "A later chapter stays local while a gap remains");
        Assert(await Complete("manga", "chapter-1", new(44, "MANGA", 1), viewer), "Filling a gap includes previously completed chapters");
        Assert(await account.SyncPendingAsync(store) == 1 && fixture.MangaProgress == 2, "Upload the contiguous manga count");
        Assert(!await Complete("manga", "chapter-2-alternate", new(44, "MANGA", 2), viewer), "Alternate provider cannot count twice");
        await store.SetSettingAsync(LocalStore.AutomaticAniListKey, "false");
        Assert(!await Complete("manga", "chapter-3", new(44, "MANGA", 3), viewer), "Opt-out retains local completion");
        await store.SetSettingAsync(LocalStore.AutomaticAniListKey, "true");
        Console.WriteLine("PASS: mapped add-on completion, offline replay, gaps, duplicates, account and media isolation, and remote count protection.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Authorization : IAniListAuthorization
    {
        public string ClientId => "123";
        public Uri Callback => new("co.zyma.anikatha://anilist/callback");
        public bool BadState { get; set; }
        private AniListCredential? credential;
        public Task<Uri> AuthorizeAsync(Uri request, CancellationToken token)
        {
            Assert(request.GetLeftPart(UriPartial.Path) == "https://anilist.co/api/v2/oauth/authorize",
                "OAuth uses the official AniList authorization endpoint");
            Assert(request.Query.StartsWith("?response_type=token&client_id=123&state=", StringComparison.Ordinal) &&
                !request.Query.Contains("redirect_uri", StringComparison.Ordinal),
                "Implicit OAuth uses the registered redirect and sends the public client ID and state");
            var state = request.Query.Split('&').Single(item => item.StartsWith("state="))[6..];
            return Task.FromResult(new Uri(Callback + "#access_token=mock&expires_in=3600&state=" + (BadState ? "unmatched" : state)));
        }
        public Task<AniListCredential?> ReadAsync() => Task.FromResult(credential);
        public Task WriteAsync(AniListCredential value) { credential = value; return Task.CompletedTask; }
        public void Clear() => credential = null;
        public void ChangeViewer(int viewer) => credential = credential! with { ViewerId = viewer, Expires = DateTimeOffset.UtcNow.AddHours(1) };
        public void Expire() => credential = credential! with { Expires = DateTimeOffset.UtcNow.AddMinutes(-1) };
    }

    private sealed class Remote : HttpMessageHandler
    {
        public string Status { get; set; } = "CURRENT";
        public int Progress { get; set; } = 4;
        public int MangaProgress { get; set; }
        public double Score { get; set; } = 8.4;
        public bool Offline { get; set; }
        public bool Revoked { get; set; }
        public int Calls { get; private set; }
        public bool DelayNextProgress { get; set; }
        public TaskCompletionSource Sending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (Offline) throw new HttpRequestException("Offline fixture");
            if (Revoked) return new(HttpStatusCode.Unauthorized);
            Assert(request.Headers.Authorization?.Scheme == "Bearer", "Authenticated requests use a Bearer header");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = doc.RootElement.GetProperty("query").GetString()!;
            var variables = doc.RootElement.GetProperty("variables");
            if (query.Contains("Viewer"))
                return Json(new { Viewer = new { id = 7, name = "Reader", mediaListOptions = new { scoreFormat = "POINT_10_DECIMAL" } } });
            if (query.Contains("mediaListEntry"))
            {
                var mediaId = variables.GetProperty("id").GetInt32();
                return Json(new { Media = new { id = mediaId, mediaListEntry = new { id = mediaId + 1000, progress = mediaId == 44 ? MangaProgress : Progress } } });
            }
            if (query.Contains("SaveMediaListEntry"))
            {
                var mediaId = variables.GetProperty("id").GetInt32();
                Assert(variables.GetProperty("entryId").GetInt32() == mediaId + 1000, "Mutation uses list entry ID, not the title ID");
                var value = variables.GetProperty("value");
                if (query.Contains("progress:"))
                {
                    if (DelayNextProgress)
                    {
                        DelayNextProgress = false;
                        Sending.TrySetResult();
                        await Release.Task.WaitAsync(token);
                    }
                    if (mediaId == 44) MangaProgress = value.GetInt32();
                    else Progress = value.GetInt32();
                }
                if (query.Contains("score:")) Score = value.GetDouble();
                if (query.Contains("status:")) Status = value.GetString()!;
                return Json(new { SaveMediaListEntry = new { mediaId = variables.GetProperty("id").GetInt32() } });
            }
            var type = variables.GetProperty("kind").GetString();
            var page = variables.GetProperty("page").GetInt32();
            var first = type == "ANIME" && page == 1;
            var id = first ? 42 : type == "ANIME" ? 43 : 44;
            return Json(new { Page = new
            {
                pageInfo = new { hasNextPage = first },
                mediaList = new[] { new
                {
                    mediaId = id, status = first ? Status : "PLANNING", progress = first ? Progress : 0, score = first ? Score : 0,
                    media = new { id, type, title = new { userPreferred = $"Title {id.ToString(CultureInfo.InvariantCulture)}" }, coverImage = new { large = "" } }
                } }
            } });
        }

        private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { data }))
        };
    }
}
