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
                if (path is null || new FileInfo(path).Length != asset.Bytes)
                {
                    await SaveAsync(job with { State = DownloadState.Failed, Message = "Downloaded files are incomplete. Retry this item." });
                    return null;
                }
            }
            return job;
        }
        finally { gate.Release(); }
    }

    public long UsedBytes => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
    public long AvailableBytes => new DriveInfo(root).AvailableFreeSpace;

    public async Task RemoveAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            await LoadJobsAsync();
            if (!jobs.TryGetValue(id, out var job) || job.State is DownloadState.Downloading or DownloadState.Queued) return;
            var path = Path.Combine(complete, id);
            var tombstone = Path.Combine(staging, "removed-" + id);
            if (Directory.Exists(path))
            {
                DeleteDirectory(tombstone);
                Directory.Move(path, tombstone);
            }
            try
            {
                await store.DeleteSettingAsync(JobPrefix + id);
                jobs.Remove(id);
            }
            catch
            {
                if (Directory.Exists(tombstone)) Directory.Move(tombstone, path);
                throw;
            }
            TryDeleteDirectory(tombstone);
            TryDeleteDirectory(Path.Combine(staging, id));
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    private void RecoverStaging()
    {
        foreach (var path in Directory.EnumerateDirectories(staging))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("removed-", StringComparison.Ordinal) && jobs.TryGetValue(name[8..], out var job) &&
                job.State == DownloadState.Complete && !Directory.Exists(Path.Combine(complete, job.Id)))
                Directory.Move(path, Path.Combine(complete, job.Id));
            else TryDeleteDirectory(path);
        }
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
