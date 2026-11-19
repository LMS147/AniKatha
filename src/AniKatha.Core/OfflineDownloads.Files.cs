namespace AniKatha.Core;

public sealed partial class OfflineDownloads
{
    public async Task<DownloadJob?> OpenAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            await LoadJobsAsync();
            if (!jobs.TryGetValue(id, out var job) || job.State != DownloadState.Complete ||
                job.Assets is not { Count: > 0 } assets) return null;
            foreach (var asset in assets)
            {
                var path = FindAsset(id, asset.Name);
                if (path is null || new FileInfo(path).Length != asset.Bytes) return null;
            }
            return job;
        }
        finally { gate.Release(); }
    }

    public string? FindAsset(string id, string name) => FindAssetIn(root, id, name);

    public static string? FindAssetIn(string directory, string id, string name)
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit) || !MediaAssetName(name)) return null;
        var path = Path.Combine(directory, "complete", id, name);
        return File.Exists(path) ? path : null;
    }

    private static bool MediaAssetName(string name)
    {
        if (name == "video.mp4") return true;
        if (!name.StartsWith("page-", StringComparison.Ordinal)) return false;
        var dot = name.IndexOf('.');
        return dot >= 9 && dot <= 13 && name[5..dot].All(char.IsAsciiDigit) &&
            name[(dot + 1)..] is "png" or "jpg" or "webp" or "gif" or "avif";
    }
}
