using System.Net;
using AniKatha.Core;
using AniKatha.UI.Pages;
using AniKatha.UI.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

internal static class PlayerChecks
{
    internal static async Task RunAsync()
    {
        var database = Path.Combine(Path.GetTempPath(), $"anikatha-player-{Guid.NewGuid()}.db3");
        var store = new LocalStore(database);
        await store.SetSettingAsync(MediaPreferences.SpeedKey, "1.25");
        await store.SetSettingAsync(MediaPreferences.SubtitleLanguageKey, "en");
        await store.SetSettingAsync(MediaPreferences.AudioLanguageKey, "ja");
        await store.SetSettingAsync(MediaPreferences.SubtitleSizeKey, "large");
        await store.SavePlaybackPositionAsync("episode", 19.625);
        using var fixture = new SubtitleFixture();
        using var http = new HttpClient(fixture);
        var external = new ExternalFixture();
        await using var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IExternalVideoPlayer>(external);
        context.Services.AddSingleton(new AddonClient(http, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.5") })));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule("./_content/AniKatha.UI/episode-player.mjs");
        var player = module.SetupModule("attachPlayer", _ => true);
        var tracks = new[]
        {
            new EpisodeSubtitle("en", "https://subtitle.example/signs.vtt", "English", "signs-and-songs"),
            new EpisodeSubtitle("en", "https://subtitle.example/full.srt", "English dialogue", "full"),
            new EpisodeSubtitle("ja", "https://subtitle.example/missing.vtt", "Unavailable", "captions")
        };
        var advanced = 0;
        var screen = context.Render<WatchPreview>(p => p.Add(x => x.EpisodeKey, "episode")
            .Add(x => x.Subtitles, tracks).Add(x => x.TrackCompletion, true).Add(x => x.OnNext, () => advanced++));
        Task Choose(string label, string value) => screen.InvokeAsync(() => screen.FindAll("label")
            .Single(x => x.TextContent.TrimStart().StartsWith(label, StringComparison.Ordinal))
            .QuerySelector("select")!.ChangeAsync(new ChangeEventArgs { Value = value }));
        Task Click(string label) => screen.InvokeAsync(() => screen.FindAll("button")
            .Single(x => x.TextContent.Trim() == label).ClickAsync(new()));
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        screen.WaitForAssertion(() => Check(screen.Markup.Contains("Original subtitle timing"), "Preferred full track should load"));
        Check(fixture.Requests.SequenceEqual(["/full.srt"]), "Full subtitles outrank signs of the same language");
        var attachment = module.Invocations.Single(x => x.Identifier == "attachPlayer");
        Check((double)attachment.Arguments[3]! == 19.625 && (double)attachment.Arguments[5]! == 1.25, "Exact checkpoint and saved default reach engine");
        await Choose("Playback speed", "1.5");
        Check(player.Invocations.Any(x => x.Identifier == "setSpeed" && Equals(x.Arguments[0], 1.5)), "Speed immediately reaches engine");
        await Click("Save as default");
        Check(await new LocalStore(database).GetSettingAsync(MediaPreferences.SpeedKey) == "1.5", "Speed saved across reopen");
        await Click("Later by 250 ms");
        Check(player.Invocations.Last(x => x.Identifier == "setSubtitle").Arguments[3]?.Equals(250) == true &&
            screen.Markup.Contains("250 ms later"), "Subtitle delay is sent and displayed");
        await Click("Reset offset");
        Check(player.Invocations.Last(x => x.Identifier == "setSubtitle").Arguments[3]?.Equals(0) == true, "Reset restores original timing");
        await Choose("Subtitles", "external:2");
        screen.WaitForAssertion(() => Check(screen.Markup.Contains("Retry subtitle"), "Failed track shows track-local retry"));
        Check(!screen.Markup.Contains("Retry video") && screen.Markup.Contains("Original subtitle timing"), "Video and previous track survive HTTP failure");
        await Choose("Subtitles", "");
        var before = player.Invocations.Count(x => x.Identifier == "setSubtitle");
        await screen.InvokeAsync(() => screen.Instance.UpdateMediaTracks(
            [new("em", "en", "Embedded", "captions")],
            [new("dub", "en", "English", "main", true), new("original", "ja", "Japanese", "main")]));
        Check(player.Invocations.Count(x => x.Identifier == "setSubtitle") == before, "Manual Off survives new tracks");
        Check(player.Invocations.Any(x => x.Identifier == "selectAudio" && Equals(x.Arguments[0], "original")), "Audio preference matches independently");
        await Choose("Audio track", "dub");
        Check(await store.GetSettingAsync(MediaPreferences.AudioLanguageKey) == "ja", "Manual audio choice preserves the default");
        await Choose("Subtitles", "em");
        Check(player.Invocations.Last(x => x.Identifier == "selectEmbeddedSubtitle").Arguments[0]?.Equals("em") == true &&
            !screen.Markup.Contains("Reset offset"), "Embedded track selection hides sidecar timing controls");
        await Click("Open in external player");
        Check(external.Opened is not null && screen.Markup.Contains("No installed video app"), "Missing external handler is actionable");
        Check(player.Invocations.Any(x => x.Identifier == "pauseAndCheckpoint") &&
            await store.GetPlaybackPositionAsync("episode") == 19.625 && !await store.IsUnitCompleteAsync("episode"),
            "Handoff checkpoints without completion");
        await screen.InvokeAsync(screen.Instance.CompleteEpisode);
        Check(await store.IsUnitCompleteAsync("episode") && advanced == 0, "Completing an episode with autoplay off does not advance");
        await store.SetSettingAsync(MediaPreferences.SubtitleLanguageKey, "pt-BR");
        var unmatched = context.Render<WatchPreview>(p => p.Add(x => x.EpisodeKey, "another-episode").Add(x => x.Subtitles, tracks));
        unmatched.WaitForAssertion(() => Check(unmatched.FindAll("select").Count == 2 &&
            !unmatched.Markup.Contains("Original subtitle timing"), "An unmatched subtitle language falls back to Off"));
        var settings = context.Render<PlaybackSettings>();
        await settings.InvokeAsync(() => settings.FindAll("button").Single(x => x.TextContent.Trim() == "Reset subtitle appearance").ClickAsync(new()));
        Check(await store.GetSettingAsync(MediaPreferences.SubtitleSizeKey) == "normal" &&
            await store.GetSettingAsync(MediaPreferences.SubtitlePlacementKey) == "automatic", "Appearance reset persists");
        Console.WriteLine("PASS: exact player resume, speed, preferred and manual tracks, Off, subtitle error, delay/reset, audio preference and external checkpoint.");
    }

    private sealed class SubtitleFixture : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            return Task.FromResult(path == "/missing.vtt" ? new HttpResponseMessage(HttpStatusCode.NotFound) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("1\n00:00:01,000 --> 00:00:03,000\nTest dialogue") });
        }
    }

    private sealed class ExternalFixture : IExternalVideoPlayer
    {
        public Uri? Opened { get; private set; }
        public Task<bool> OpenAsync(Uri video)
        {
            Opened = video;
            return Task.FromResult(false);
        }
    }
}
