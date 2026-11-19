using AniKatha.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AniKatha.UI.Pages;

public partial class WatchPreview
{
    public sealed record PlaybackTrack(string Id, string Language, string Label, string Role, bool Selected = false);

    private IReadOnlyList<PlaybackTrack> embeddedSubtitles = [];
    private IReadOnlyList<PlaybackTrack> audioTracks = [];
    private string? preferredLanguage, preferredAudioLanguage;
    private string subtitleSize = "normal", subtitleBackground = "dark", subtitlePlacement = "automatic";
    private string subtitleChoice = "", attemptedSubtitle = "", audioChoice = "";
    private bool audioError;
    private string? lastAutomaticChoice;
    private EpisodeSubtitle? activeSidecar;
    private string? activeCues;
    private int subtitleOffset, subtitleVersion;
    private bool subtitleError, manualSubtitle, manualAudio, loadingSubtitle;
    private CancellationTokenSource? subtitleRequest;

    private IEnumerable<PlaybackTrack> SubtitleOptions() =>
        Subtitles.Select((track, index) => new PlaybackTrack($"external:{index}", track.Language, track.Label, track.Role))
            .Concat(embeddedSubtitles);

    private static string TrackLanguage(PlaybackTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.Language) || track.Language == "und") return "Language unspecified";
        try { return System.Globalization.CultureInfo.GetCultureInfo(track.Language).DisplayName; }
        catch (System.Globalization.CultureNotFoundException) { return track.Language; }
    }

    private static string TrackLabel(PlaybackTrack track)
    {
        var role = track.Role.ToLowerInvariant() switch
        {
            "signs" or "signs-and-songs" => "Signs and songs",
            "forced" => "Forced",
            "captions" or "sdh" => "Captions",
            "full" => "Full subtitles",
            _ => track.Role
        };
        var values = new[] { track.Label, role }.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(" · ", values);
    }

    private Task ChangeSubtitle(string choice)
    {
        manualSubtitle = true;
        return ApplySubtitle(choice);
    }

    private async Task ApplySubtitle(string choice)
    {
        subtitleRequest?.Cancel();
        subtitleRequest?.Dispose();
        var token = (subtitleRequest = new()).Token;
        var version = ++subtitleVersion;
        attemptedSubtitle = choice;
        subtitleError = false;
        loadingSubtitle = false;
        if (player is null) return;
        try
        {
            if (!choice.StartsWith("external:", StringComparison.Ordinal))
            {
                await player.InvokeVoidAsync("selectEmbeddedSubtitle", choice);
                if (token.IsCancellationRequested || disposed) return;
                subtitleChoice = choice;
                activeSidecar = null;
                activeCues = null;
                subtitleOffset = 0;
                return;
            }

            if (!int.TryParse(choice.AsSpan("external:".Length), out var index) || index < 0 || index >= Subtitles.Count)
                return;
            loadingSubtitle = true;
            var track = Subtitles[index];
            var cues = await Addons.GetSubtitleAsync(track.Url, token);
            if (token.IsCancellationRequested || disposed) return;
            await player.InvokeVoidAsync("setSubtitle", track.Language, cues, subtitlePlacement, 0, version);
            if (token.IsCancellationRequested || disposed) return;
            subtitleChoice = choice;
            activeSidecar = track;
            activeCues = cues;
            subtitleOffset = 0;
        }
        catch when (!disposed && !token.IsCancellationRequested) { subtitleError = true; }
        catch when (disposed || token.IsCancellationRequested) { }
        finally { if (version == subtitleVersion) loadingSubtitle = false; }
    }

    private async Task ApplyPreferredSubtitle()
    {
        if (manualSubtitle || disposed || player is null) return;
        var choices = SubtitleOptions().ToList();
        var index = MediaPreferences.PreferredTrack(choices.Select(track =>
            new EpisodeSubtitle(track.Language, "", track.Label, track.Role)).ToList(), preferredLanguage);
        var choice = index < 0 ? "" : choices[index].Id;
        if (lastAutomaticChoice == choice) return;
        lastAutomaticChoice = choice;
        await ApplySubtitle(choice);
        if (!disposed) await InvokeAsync(StateHasChanged);
    }

    private async Task ChangeAudio(string choice)
    {
        if (!audioTracks.Any(track => track.Id == choice) || player is null) return;
        manualAudio = true;
        try
        {
            audioError = !await player.InvokeAsync<bool>("selectAudio", choice);
            if (!audioError) audioChoice = choice;
        }
        catch (JSException) { audioError = true; }
    }

    [JSInvokable]
    public async Task UpdateMediaTracks(PlaybackTrack[] subtitles, PlaybackTrack[] audio)
    {
        if (disposed) return;
        embeddedSubtitles = subtitles;
        audioTracks = audio;
        if (!manualAudio || !audio.Any(track => track.Id == audioChoice))
        {
            var match = audio.OrderByDescending(track => MediaPreferences.LanguageMatch(track.Language, preferredAudioLanguage))
                .FirstOrDefault(track => MediaPreferences.LanguageMatch(track.Language, preferredAudioLanguage) > 0);
            var current = audio.FirstOrDefault(track => track.Selected);
            audioChoice = current?.Id ?? audio.FirstOrDefault()?.Id ?? "";
            if (match is not null && player is not null && match.Id != audioChoice)
            {
                if (await player.InvokeAsync<bool>("selectAudio", match.Id)) audioChoice = match.Id;
            }
        }
        await ApplyPreferredSubtitle();
        if (!disposed) await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task SetSubtitleError(int version)
    {
        if (disposed || version != subtitleVersion) return Task.CompletedTask;
        subtitleError = true;
        subtitleChoice = "";
        activeSidecar = null;
        activeCues = null;
        return InvokeAsync(StateHasChanged);
    }

    private Task OffsetSubtitles(int step) => ApplyOffset(Math.Clamp(subtitleOffset + step, -30000, 30000));
    private Task ResetSubtitleOffset() => ApplyOffset(0);

    private async Task ApplyOffset(int offset)
    {
        if (loadingSubtitle || player is null || activeSidecar is not { } track || activeCues is not { } cues) return;
        try
        {
            await player.InvokeVoidAsync("setSubtitle", track.Language, cues, subtitlePlacement, offset, ++subtitleVersion);
            subtitleOffset = offset;
        }
        catch { attemptedSubtitle = subtitleChoice; subtitleError = true; }
    }
}
