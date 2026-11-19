using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AniKatha.Core;

public enum DownloadState { Queued, Downloading, Complete, Cancelled, Failed }

public sealed record DownloadedAsset(string Name, long Bytes);
public sealed record DownloadSelection(string Origin, string Type, string TitleId, string UnitId, string Source,
    string Title, string Label, string? StreamId = null)
{
    public string ProgressKey => ResumeEntry.Key(Origin, Type, TitleId, UnitId, Source);
    public string DownloadId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        ProgressKey + "/" + Uri.EscapeDataString(StreamId ?? ""))));
}

public sealed record DownloadJob(DownloadSelection Selection, DownloadState State, long Bytes = 0, int Pages = 0,
    int? TotalPages = null, IReadOnlyList<DownloadedAsset>? Assets = null, string? Message = null)
{
    public string Id => Selection.DownloadId;
}

public sealed partial class OfflineDownloads
{
    public OfflineDownloads(LocalStore store, AddonClient client, string directory)
    {
        this.store = store;
        this.client = client;
        root = Path.GetFullPath(directory);
        complete = Path.Combine(root, "complete");
        staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(complete);
        Directory.CreateDirectory(staging);
    }

    private readonly LocalStore store;
    private readonly AddonClient client;
    private readonly string root, complete, staging;

    public static string AssetUrl(string id, string asset) => $"offline-media/{id}/{asset}";

    private async Task<DownloadedAsset[]> FetchAsync(DownloadJob job, string folder, Func<long, int, int?, Task> report, CancellationToken token)
    {
        var selection = job.Selection;
        var sources = await store.GetAddonsAsync();
        var source = sources.Select(row => new AddonResources(row)).FirstOrDefault(row => row.ProviderKey == selection.Source);
        var resource = selection.Type == "manga" ? "pages" : "stream";
        if (source is null || !source.Supports(resource, selection.Type, selection.UnitId))
            throw new InvalidDataException("Enable or reinstall the source to download this item.");
        var data = await client.FetchJsonAsync(source.Resource(resource, selection.Type, selection.UnitId), token);
        return selection.Type == "manga"
            ? await FetchPagesAsync(data, folder, report, token)
            : await FetchVideoAsync(data, selection.StreamId, folder, report, token);
    }

    private async Task<DownloadedAsset[]> FetchPagesAsync(JsonElement data, string folder, Func<long, int, int?, Task> report,
        CancellationToken token)
    {
        var pages = AddonResources.Items(data, "pages").ToArray();
        if (pages.Length == 0 || pages.Any(page => page.ValueKind != JsonValueKind.String || !AddonResources.MediaUrl(page.GetString()!)))
            throw new InvalidDataException("The provider did not return readable pages.");
        var assets = new List<DownloadedAsset>(pages.Length);
        long completedBytes = 0;
        foreach (var page in pages)
        {
            var asset = await client.DownloadMediaAsync(page.GetString()!, folder, assets.Count + 1, false,
                bytes => report(completedBytes + bytes, assets.Count, pages.Length), token);
            completedBytes += asset.Bytes;
            assets.Add(asset);
            await report(completedBytes, assets.Count, pages.Length);
        }
        return assets.ToArray();
    }
}
