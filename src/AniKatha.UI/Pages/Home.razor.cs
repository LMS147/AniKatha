using System.Globalization;
using System.Text.Json;
using AniKatha.Core;
using AniKatha.UI.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using static AniKatha.Core.AddonResources;

namespace AniKatha.UI.Pages;

public partial class Home
{
    [Inject] private AniListClient Api { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private AddonClient Addons { get; set; } = null!;

    private static readonly string[] ReleaseOptions = ["New Release", "Newly Added", "Just Completed"];
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<DateOnly, Feed<AiringEpisode>> schedule = [];
    private readonly Feed<HomeCard> watch = new(), latest = new(), trending = new(), upcoming = new(),
        newRelease = new(), completed = new(), followed = new(), reading = new(), discover = new(),
        addonManga = new(), popular = new(), newManga = new();

    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly day = DateOnly.FromDateTime(DateTime.Today);
    private bool manga, mangaLoaded, showTrending, hasFollowed, disposed;
    private int watchLimit = 4, upcomingLimit = 4, readingLimit = 4;
    private string release = ReleaseOptions[0];
    private Media? selected;
    private HomeProviderTitle? selectedProvider;
    private ElementReference animeTab, mangaTab;
    private List<AddonResources> providers = [];

    private Feed<AiringEpisode> ActiveSchedule => schedule[day];
    private IEnumerable<HomeCard> DiscoveryCards => addonManga.Items.Take(4)
        .Concat(discover.Items.Take(8 - Math.Min(addonManga.Items.Count, 4)));
    private string FollowedMessage => hasFollowed ? "No newer chapters were found from your enabled sources." :
        "Save a manga in Library to follow its chapters here.";

    protected override Task OnInitializedAsync() =>
        Task.WhenAll(LoadWatch(), LoadLatest(), LoadUpcoming(), LoadRelease(), LoadSchedule());

    private Task Switch(bool next)
    {
        manga = next;
        if (!manga || mangaLoaded) return Task.CompletedTask;
        mangaLoaded = true;
        return Task.WhenAll(LoadReading(), LoadFollowed(), LoadDiscovery(), LoadPopular(), LoadNewManga());
    }

    private async Task HandleTabKeys(KeyboardEventArgs args)
    {
        var next = args.Key switch
        {
            "ArrowLeft" or "ArrowRight" => !manga,
            "Home" => false,
            "End" => true,
            _ => manga
        };
        if (next == manga) return;
        var switchTask = Switch(next);
        try { await (next ? mangaTab : animeTab).FocusAsync(); }
        catch (JSDisconnectedException) { }
        await switchTask;
    }

    private static string Pressed(bool value) => value ? "true" : "false";
    private void OpenCard(HomeCard card) => (selected, selectedProvider) = (card.Detail, card.Provider);
    private void CloseCard() => (selected, selectedProvider) = (null, null);
    private void HandleDetailsKeys(KeyboardEventArgs args) { if (args.Key == "Escape") CloseCard(); }

    private Task LoadWatch() => Load(watch, token => ReadHistory(false, token));
    private Task LoadReading() => Load(reading, token => ReadHistory(true, token));
    private Task LoadUpcoming() => LoadTitles(upcoming, HomeCatalogue.UpcomingAnime, "Upcoming");
    private Task LoadPopular() => LoadTitles(popular, HomeCatalogue.RecentPopularManga, ranked: true);
    private Task LoadNewManga() => LoadTitles(newManga, HomeCatalogue.NewManga);

    private Task LoadRelease() => release switch
    {
        "Just Completed" => LoadTitles(completed, HomeCatalogue.JustCompleted, "Completed"),
        "New Release" => LoadTitles(newRelease, HomeCatalogue.NewRelease, "Released"),
        _ => Task.CompletedTask
    };

    private Task ChangeRelease(string next)
    {
        release = next;
        return (next == "Just Completed" ? completed : newRelease).Started ? Task.CompletedTask : LoadRelease();
    }

    private Task LatestFilter(bool useTrending)
    {
        showTrending = useTrending;
        return useTrending && !trending.Started ? LoadTrending() : Task.CompletedTask;
    }

    private Task RetryLatest() => showTrending ? LoadTrending() : LoadLatest();
    private Task LoadTrending() => LoadTitles(trending, HomeCatalogue.TrendingAnime);
    private void RandomEpisode()
    {
        if (latest.Items.Count == 0) return;
        var card = latest.Items[Random.Shared.Next(latest.Items.Count)];
        if (card.Link is { } link) Navigation.NavigateTo(link);
        else OpenCard(card);
    }

    private Task LoadLatest() => Load(latest, async token =>
    {
        var now = DateTimeOffset.Now;
        var airings = await Api.HomeAiringsAsync(now.AddDays(-7), now, true, token);
        var embedded = (await Store.GetAddonsAsync()).SelectMany(addon => addon.GetYouTubeEpisodes())
            .ToLookup(episode => (episode.AniListId, episode.Number));
        return airings.OrderByDescending(item => item.AirsAt).DistinctBy(item => item.Media.Id)
            .Select(item =>
            {
                var card = FromAiring(item);
                var source = embedded[(item.Media.Id, item.Episode)].FirstOrDefault();
                return source is null ? card : card with { Link = $"/anime/addon/{source.AniListId}/{source.Number}/{source.VideoId}" };
            }).ToArray();
    });

    private Task ChangeDay(DateOnly date)
    {
        day = date;
        return schedule.ContainsKey(date) ? Task.CompletedTask : LoadSchedule();
    }

    private Task LoadSchedule()
    {
        var date = day;
        if (!schedule.TryGetValue(date, out var feed)) schedule[date] = feed = new();
        return Load(feed, async token =>
        {
            var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
            var until = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
            return (await Api.HomeAiringsAsync(from, until, false, token)).OrderBy(item => item.AirsAt).ToArray();
        });
    }

    private Task LoadTitles(Feed<HomeCard> feed, HomeCatalogue catalogue, string? note = null, bool ranked = false) =>
        Load(feed, async token => (await Api.HomeCatalogueAsync(catalogue, today, token))
            .Select((title, index) => FromTitle(title, note, ranked ? index + 1 : null)).ToArray());

    private static HomeCard FromAiring(AiringEpisode episode) =>
        new(episode.Media.Title, episode.Media.Poster, Caption: $"Episode {episode.Episode}",
            Note: episode.Media.Format?.Replace('_', ' ') ?? episode.AirsAt.ToLocalTime().ToString("MMM d"), Detail: episode.Media);

    private static HomeCard FromTitle(HomeTitle title, string? note, int? rank)
    {
        var media = title.Media;
        var caption = title.Date?.ToString("MMM d, yyyy") ??
            (title.Season is { Length: > 0 } season && media.Year is int year ? $"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(season.ToLowerInvariant())} {year}" : null);
        var chapters = media.Kind == "MANGA" && media.Chapters is int count && count > 0 ? $"{count} chapters" : null;
        var info = string.Join(" · ", new[] { note, media.Format?.Replace('_', ' '), chapters }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new(media.Title, media.Poster, media.Kind == "MANGA", Caption: caption, Note: info, Rank: rank, Detail: media);
    }

    private async Task<IReadOnlyList<HomeCard>> ReadHistory(bool readingHistory, CancellationToken token)
    {
        var history = await Store.HistoryAsync();
        var library = await Store.GetLibraryAsync();
        var result = new List<HomeCard>();
        foreach (var entry in history.Where(item => (item.Type == "manga") == readingHistory))
        {
            token.ThrowIfCancellationRequested();
            var key = ResumeEntry.Key(entry.Origin, entry.Type, entry.TitleId, entry.UnitId, entry.Source);
            if (!readingHistory && await Store.IsUnitCompleteAsync(key)) continue;
            var length = await Store.GetHomeUnitLengthAsync(key);
            var savedPoster = library.FirstOrDefault(item => item.ProviderKey == entry.Origin &&
                item.ExternalId == entry.TitleId && item.ResourceType == entry.Type)?.Poster;
            var poster = entry.Poster ?? savedPoster;
            if (readingHistory)
            {
                var page = await Store.GetReadingPageAsync(key);
                var totalPages = length is double total && total >= page && total <= int.MaxValue && total == Math.Truncate(total)
                    ? (int?)total : null;
                result.Add(new(entry.Title, poster, true, entry.Label, totalPages is int count ? $"Page {page} / {count}" : $"Page {page}",
                    $"/resume/{entry.Id}", totalPages is int pages ? (double)page / pages : null));
            }
            else
            {
                var seconds = await Store.GetPlaybackPositionAsync(key);
                if (seconds <= 0) continue;
                result.Add(new(entry.Title, poster, Caption: entry.Label,
                    Note: length is double duration && duration >= seconds ? $"{Elapsed(seconds)} / {Elapsed(duration)}" : Elapsed(seconds),
                    Link: $"/resume/{entry.Id}", Progress: length is double known && known >= seconds ? seconds / known : null));
            }
        }
        return result;
    }

    private static string Elapsed(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : time.ToString(@"m\:ss");
    }

    private Task LoadDiscovery() => Task.WhenAll(
        Load(discover, async token => (await Api.SearchAsync(new("MANGA"), token)).Items
            .Where(item => item.Kind == "MANGA").Select(item => new HomeCard(item.Title, item.Poster, true,
                Caption: item.Chapters is int chapters ? $"{chapters} chapters" : null, Detail: item)).ToArray()),
        Load(addonManga, ReadInstalledManga));

    private async Task<List<AddonResources>> ReadProviders() =>
        providers = (await Store.GetAddonsAsync()).Select(addon => new AddonResources(addon)).ToList();

    private async Task<IReadOnlyList<HomeCard>> ReadInstalledManga(CancellationToken token)
    {
        var enabled = await ReadProviders();
        var cards = new List<HomeCard>();
        foreach (var provider in enabled)
        {
            foreach (var catalog in provider.Catalogs("MANGA").Where(catalog => !catalog.Extras.Any(extra => extra.Required && extra.Name != "skip")))
            {
                try
                {
                    var skip = catalog.Extras.Any(extra => extra.Required && extra.Name == "skip") ? "skip=0" : null;
                    var root = await Addons.FetchJsonAsync(provider.Resource("catalog", "manga", catalog.Id, skip), token);
                    token.ThrowIfCancellationRequested();
                    if (!root.TryGetProperty("metas", out var response) || response.ValueKind != JsonValueKind.Array)
                        throw new FormatException();
                    foreach (var listing in Items(root, "metas").Where(item => Field(item, "type") is "" or "manga").Take(6))
                    {
                        var id = Field(listing, "id");
                        var name = Field(listing, "name");
                        if (id.Length == 0 || name.Length == 0) continue;
                        var title = new SavedTitle { Kind = "MANGA", Title = name, ExternalId = id, Poster = Field(listing, "poster") };
                        cards.Add(new(name, title.Poster, true, Note: provider.Addon.Name,
                            Provider: new(provider, "manga", title)));
                    }
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    addonManga.Failed = true;
                }
            }
            addonManga.Items = cards.DistinctBy(card => (card.Provider!.Source.ProviderKey, card.Provider.Title.ExternalId)).ToArray();
            if (!disposed) StateHasChanged();
        }
        return addonManga.Items;
    }

    private Task LoadFollowed() => Load(followed, ReadFollowed);
    private async Task<IReadOnlyList<HomeCard>> ReadFollowed(CancellationToken token)
    {
        var titles = (await Store.GetLibraryAsync()).Where(title => title.Kind == "MANGA" && title.Status != "DROPPED").ToArray();
        hasFollowed = titles.Length > 0;
        if (!hasFollowed) return [];
        var enabled = await ReadProviders();
        var history = (await Store.HistoryAsync()).Where(entry => entry.Type == "manga").ToArray();
        var result = new List<HomeCard>();
        foreach (var title in titles.Take(12))
        {
            token.ThrowIfCancellationRequested();
            foreach (var provider in enabled)
            {
                var canonical = title.ProviderKey is null && title.Id > 0;
                var id = canonical ? $"anilist:{title.Id}" : title.ExternalId;
                if (id is not { Length: > 0 } ||
                    (canonical ? !provider.HasPrefix("chapters", "manga", id) :
                        provider.ProviderKey != title.ProviderKey || !provider.Supports("chapters", "manga", id))) continue;
                try
                {
                    var root = await Addons.FetchJsonAsync(provider.Resource("chapters", "manga", id), token);
                    if (canonical && (!root.TryGetProperty("meta", out var meta) || AddonAniListProgress.TitleId(meta) != title.Id)) continue;
                    if (!root.TryGetProperty("chapters", out var chapterList) || chapterList.ValueKind != JsonValueKind.Array)
                        throw new FormatException();
                    var chapters = Items(root, "chapters")
                        .Where(unit => Field(unit, "id").Length > 0)
                        .Select(unit => (Unit: unit, Number: ChapterNumber(unit, canonical)))
                        .Where(chapter => chapter.Number.HasValue)
                        .OrderByDescending(chapter => chapter.Number).ToArray();
                    if (chapters.Length == 0) continue;

                    var chosen = chapters[0];
                    var prior = history.Where(entry => entry.Origin == provider.ProviderKey && entry.TitleId == id)
                        .Select(entry => chapters.FirstOrDefault(chapter => Field(chapter.Unit, "id") == entry.UnitId))
                        .Where(chapter => chapter.Number.HasValue)
                        .OrderByDescending(chapter => chapter.Number).FirstOrDefault();
                    if (chosen.Number <= Math.Max(canonical ? title.Progress : 0, prior.Number ?? 0)) continue;

                    var label = Field(chosen.Unit, "title");
                    if (label.Length == 0) label = $"Chapter {chosen.Number}";
                    var unitId = Field(chosen.Unit, "id");
                    var canOpen = provider.ResolveUnitProviders("manga", chosen.Unit, enabled).Any();
                    var previous = prior.Number is not null ? Field(prior.Unit, "title")
                        : canonical && title.Progress > 0 ? $"Chapter {title.Progress}" : null;
                    var hasMetadata = root.TryGetProperty("meta", out var metadata);
                    var poster = title.Poster ?? (hasMetadata ? Field(metadata, "poster") : null);
                    var copy = new SavedTitle { Id = title.Id, Kind = title.Kind, Title = title.Title, Poster = poster, ExternalId = id };
                    result.Add(new(title.Title, poster, true, label, canOpen ? previous is { Length: > 0 } ? $"Last opened · {previous}" : null :
                        "Open title to check sources", Provider: new(provider, "manga", copy, canOpen ? unitId : null, canonical ? title.Id : null)));
                    break;
                }
                catch (AddonResourceNotFoundException) { }
                catch (Exception) when (!token.IsCancellationRequested) { followed.Failed = true; }
            }
        }
        return result;
    }

    private static decimal? ChapterNumber(JsonElement chapter, bool requireAniListNumber)
    {
        if (chapter.TryGetProperty("anilistProgress", out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number) && number > 0) return number;
        if (requireAniListNumber) return null;
        var label = Field(chapter, "title");
        return label.StartsWith("Chapter ", StringComparison.OrdinalIgnoreCase) &&
            decimal.TryParse(label.AsSpan(8), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed : null;
    }

    private async Task Load<T>(Feed<T> feed, Func<CancellationToken, Task<IReadOnlyList<T>>> read)
    {
        if (feed.Loading || disposed) return;
        feed.Started = feed.Loading = true;
        feed.Failed = false;
        try
        {
            var token = stop.Token;
            var items = await read(token);
            if (!disposed) feed.Items = items;
        }
        catch (Exception) when (disposed) { }
        catch (Exception) { feed.Failed = true; }
        finally
        {
            feed.Loading = false;
            if (!disposed) await InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose()
    {
        disposed = true;
        stop.Cancel();
        stop.Dispose();
    }

    private sealed class Feed<T>
    {
        public IReadOnlyList<T> Items { get; set; } = [];
        public bool Loading { get; set; }
        public bool Failed { get; set; }
        public bool Started { get; set; }
        public bool HasItems => Items.Count > 0;
    }
}
