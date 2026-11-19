using System.Net;
using System.Text.Json;
using AniKatha.Core;

internal static class LocalProfileChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "anikatha-profiles-" + Guid.NewGuid().ToString("N"));
        var legacy = new LocalStore(Path.Combine(root, "anikatha.db3"));
        await legacy.SaveAsync(new SavedTitle { Id = 42, Kind = "ANIME", Title = "Original anime" });
        await legacy.SavePlaybackPositionAsync("same-episode", 31.625);
        await legacy.SaveReadingPageAsync("same-chapter", 7);
        await legacy.RememberAsync(new ResumeEntry { Id = "original", Title = "Original anime", Type = "anime" });
        await legacy.SetSettingAsync("accent", "#83c8ff");
        await legacy.SetSettingAsync(MediaPreferences.SpeedKey, "1.25");
        await legacy.SaveAddonAsync(new InstalledAddon { Url = "https://provider.example/manifest.json", Name = "Original provider" });
        await legacy.QueueAniListEditAsync(9, 42, "progress", "3");
        var offline = Path.Combine(root, "offline", "complete", new string('A', 64));
        Directory.CreateDirectory(offline);
        File.WriteAllText(Path.Combine(offline, "video.mp4"), "existing file");

        using var http = new HttpClient();
        var secrets = new Secrets();
        var authorizations = new Dictionary<string, TrackingAuthorization>();
        var clients = new AddonClient(http);
        var profiles = new LocalProfiles(root, "anikatha.db3", clients, secrets, id =>
        {
            var authorization = new TrackingAuthorization(id == LocalProfile.DefaultId ? "Original account" : "Second account");
            authorizations[id] = authorization;
            return new AniListAccount(http, authorization);
        });
        await profiles.InitializeAsync();
        var first = profiles.Session!;
        Require((await first.Store.GetLibraryAsync()).Single().Title == "Original anime", "Legacy Library belongs to default");
        Require((await first.Store.HistoryAsync()).Single().Id == "original" &&
            await first.Store.GetReadingPageAsync("same-chapter") == 7 &&
            await first.Store.GetPlaybackPositionAsync("same-episode") == 31.625, "Legacy resume uses previous keys");
        Require((await first.Store.GetAddonsAsync()).Count == 1 &&
            await first.Store.GetSettingAsync(MediaPreferences.SpeedKey) == "1.25" &&
            (await AppearanceSettings.LoadAsync(first.Store)).Accent == "Ocean", "Legacy source and settings stay intact");
        Require(first.Downloads.FindAsset(new string('A', 64), "video.mp4") is not null, "Existing file is still served");
        Require((await first.Store.AniListEditsAsync(9)).Count == 1 &&
            (await first.AniList!.StoredViewerAsync())?.Name == "Original account", "Default outbox and account remain together");

        var second = await profiles.SaveAsync(null, "Night shelf", "moon");
        await profiles.ActivateAsync(second.Id);
        var later = profiles.Session!;
        Require((await later.Store.GetLibraryAsync()).Count == 0 && (await later.Store.GetAddonsAsync()).Count == 0 &&
            later.Downloads.FindAsset(new string('A', 64), "video.mp4") is null, "New profiles cannot see original data or downloads");
        Require((await later.Store.AniListEditsAsync(9)).Count == 0 &&
            (await later.AniList!.StoredViewerAsync())?.Name == "Second account", "Credentials and pending changes stay isolated");
        await later.Store.SavePlaybackPositionAsync("same-episode", 12.125);
        await new AppearanceSettings("light", "Rose", "compact").SaveAsync(later.Store);
        await first.Store.SavePlaybackPositionAsync("same-episode", 40.125);
        Require(await later.Store.GetPlaybackPositionAsync("same-episode") == 12.125, "Late work is still bound to previous profile");
        Require((await profiles.SaveAsync(second.Id, "Night stories", "star")).Id == second.Id, "Renaming keeps identity");

        var recovery = await profiles.SetPinAsync("2468", null);
        Require(recovery is { Length: 32 } && !secrets.Values[second.Id].Contains(recovery), "Recovery code is hashed");
        var reopened = new LocalProfiles(root, "anikatha.db3", clients, secrets);
        await reopened.InitializeAsync();
        Require(reopened.Active.Id == second.Id && reopened.Session is null, "Locked profile is gated after startup");
        await Reject(() => reopened.ActivateAsync(second.Id, "0000"));
        Require(reopened.Session is null, "Wrong PIN does not open data");
        await reopened.ActivateAsync(second.Id, "2468");
        Require((await AppearanceSettings.LoadAsync(reopened.Session!.Store)) == new AppearanceSettings("light", "Rose", "compact"),
            "Saved appearance survives restart");
        await Reject(() => reopened.RemoveAsync(second.Id));
        await Reject(() => reopened.SetPinAsync("8642", "9999"));
        var replacement = await reopened.SetPinAsync("8642", "2468");
        Require(replacement is not null && replacement != recovery, "Changing PIN replaces the recovery code");
        await reopened.ActivateAsync(LocalProfile.DefaultId);
        await Reject(() => reopened.ActivateAsync(second.Id, recovery, recover: true));
        await reopened.ActivateAsync(second.Id, replacement, recover: true);
        Require(!reopened.Active.Locked && !secrets.Values.ContainsKey(second.Id), "Recovery removes the old verifier");
        await reopened.SetPinAsync("1357", null);
        await reopened.SetPinAsync(null, "1357");
        Require(!reopened.Active.Locked, "PIN removal authorized by current PIN");
        await reopened.ActivateAsync(LocalProfile.DefaultId);
        await reopened.RemoveAsync(second.Id);
        Require(reopened.All.Count == 1 && !Directory.Exists(Path.Combine(root, "profiles", second.Id)) &&
            await reopened.Session!.Store.GetPlaybackPositionAsync("same-episode") == 40.125, "Removal does not delete original data");
        await Reject(() => reopened.RemoveAsync(LocalProfile.DefaultId));
        var survivor = await reopened.SaveAsync(null, "Keep this profile", "");
        await reopened.ActivateAsync(survivor.Id);
        Require(await reopened.RemoveAsync(LocalProfile.DefaultId) && !File.Exists(Path.Combine(root, "anikatha.db3")) &&
            !Directory.Exists(Path.Combine(root, "offline")), "Confirmed removal of original removes just its legacy files");
        Console.WriteLine("PASS: legacy Library, sources, downloads, progress and settings; profile isolation, restart, PIN and recovery, guarded removal.");
    }

    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected operation to be rejected");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Secrets : IProfileSecrets
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public Task<string?> ReadAsync(string profileId) => Task.FromResult(Values.GetValueOrDefault(profileId));
        public Task WriteAsync(string profileId, string value) { Values[profileId] = value; return Task.CompletedTask; }
        public void Remove(string profileId) => Values.Remove(profileId);
    }

    private sealed class TrackingAuthorization(string name) : IAniListAuthorization
    {
        public string? ClientId => null;
        public Uri Callback { get; } = new("co.zyma.anikatha://anilist/callback");
        public Task<Uri> AuthorizeAsync(Uri request, CancellationToken token) => throw new NotSupportedException();
        public Task<AniListCredential?> ReadAsync() => Task.FromResult<AniListCredential?>(
            new("fixture", DateTimeOffset.UtcNow.AddDays(1), 9, name, "POINT_10"));
        public Task WriteAsync(AniListCredential credential) => Task.CompletedTask;
        public void Clear() { }
    }
}
