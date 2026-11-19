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
            var state = request.Query.Split('&').Single(item => item.StartsWith("state="))[6..];
            return Task.FromResult(new Uri(Callback + "#access_token=mock&expires_in=3600&state=" + (BadState ? "unmatched" : state)));
        }
        public Task<AniListCredential?> ReadAsync() => Task.FromResult(credential);
        public Task WriteAsync(AniListCredential value) { credential = value; return Task.CompletedTask; }
        public void Clear() => credential = null;
    }

    private sealed class Remote : HttpMessageHandler
    {
        public string Status { get; set; } = "CURRENT";
        public int Progress { get; set; } = 4;
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
                return Json(new { Media = new { id = mediaId, mediaListEntry = new { id = mediaId + 1000 } } });
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
                    Progress = value.GetInt32();
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
