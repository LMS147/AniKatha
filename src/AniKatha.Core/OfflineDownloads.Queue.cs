using System.Text.Json;

namespace AniKatha.Core;

public sealed partial class OfflineDownloads
{
    private const string JobPrefix = "offline-job:";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, DownloadJob> jobs = new(StringComparer.Ordinal);
    private bool loaded, running;
    private string? activeId;
    private CancellationTokenSource? activeCancellation;
    public event Action? Changed;

    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            await LoadJobsAsync();
            StartWorker();
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<DownloadJob>> ListAsync()
    {
        await gate.WaitAsync();
        try { await LoadJobsAsync(); return jobs.Values.ToArray(); }
        finally { gate.Release(); }
    }

    public async Task QueueAsync(DownloadSelection selection)
    {
        await gate.WaitAsync();
        try
        {
            await LoadJobsAsync();
            var id = selection.DownloadId;
            if (!jobs.TryGetValue(id, out var current) || current.State is DownloadState.Cancelled or DownloadState.Failed)
                await SaveAsync(new(selection, DownloadState.Queued));
            StartWorker();
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    public async Task CancelAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            await LoadJobsAsync();
            if (activeId == id) activeCancellation?.Cancel();
            else if (jobs.TryGetValue(id, out var current) && current.State == DownloadState.Queued)
                await SaveAsync(current with { State = DownloadState.Cancelled });
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    private async Task LoadJobsAsync()
    {
        if (loaded) return;
        foreach (var setting in await store.GetSettingsAsync(JobPrefix))
        {
            if (JsonSerializer.Deserialize<DownloadJob>(setting.Value) is not { } job ||
                setting.Key != JobPrefix + job.Id) continue;
            if (job.State == DownloadState.Downloading)
                await SaveAsync(new(job.Selection, DownloadState.Queued));
            else jobs[job.Id] = job;
        }
        RecoverStaging();
        loaded = true;
    }

    private async Task SaveAsync(DownloadJob job)
    {
        await store.SetSettingAsync(JobPrefix + job.Id, JsonSerializer.Serialize(job));
        jobs[job.Id] = job;
    }

    private void StartWorker()
    {
        if (running || !jobs.Values.Any(job => job.State == DownloadState.Queued)) return;
        running = true;
        _ = Task.Run(WorkAsync);
    }

    private async Task WorkAsync()
    {
        try
        {
            while (true)
            {
                DownloadJob? job;
                CancellationTokenSource cancellation;
                await gate.WaitAsync();
                try
                {
                    job = jobs.Values.FirstOrDefault(item => item.State == DownloadState.Queued);
                    if (job is null) { running = false; return; }
                    await SaveAsync(job with { State = DownloadState.Downloading });
                    activeId = job.Id;
                    activeCancellation = cancellation = new();
                }
                finally { gate.Release(); }
                Changed?.Invoke();
                await ExecuteAsync(job, cancellation);
                Changed?.Invoke();
            }
        }
        catch
        {
            await gate.WaitAsync();
            try { running = false; activeId = null; activeCancellation?.Dispose(); activeCancellation = null; }
            finally { gate.Release(); }
            Changed?.Invoke();
        }
    }

    private async Task ExecuteAsync(DownloadJob job, CancellationTokenSource cancellation)
    {
        var folder = Path.Combine(staging, job.Id);
        var destination = Path.Combine(complete, job.Id);
        var token = cancellation.Token;
        DownloadedAsset[]? downloaded = null;
        string? error = null;
        try
        {
            DeleteDirectory(folder);
            DeleteDirectory(destination);
            Directory.CreateDirectory(folder);
            var update = DateTimeOffset.MinValue;
            var reportedPages = 0;
            downloaded = await FetchAsync(job, folder, async (bytes, pages, total) =>
            {
                if (pages == reportedPages && DateTimeOffset.UtcNow - update < TimeSpan.FromMilliseconds(400)) return;
                update = DateTimeOffset.UtcNow;
                reportedPages = pages;
                await gate.WaitAsync(token);
                try { await SaveAsync(job with { State = DownloadState.Downloading, Bytes = bytes, Pages = pages, TotalPages = total }); }
                finally { gate.Release(); }
                Changed?.Invoke();
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (InvalidDataException ex) { error = ex.Message; }
        catch (IOException) { error = "Could not write the download. Check available space."; }
        catch { error = "The source could not finish the download. Retry when connected."; }

        await gate.WaitAsync();
        try
        {
            if (token.IsCancellationRequested)
                await SaveAsync(job with { State = DownloadState.Cancelled });
            else if (downloaded is null)
                await SaveAsync(job with { State = DownloadState.Failed, Message = error });
            else
            {
                try
                {
                    Directory.Move(folder, destination);
                    await SaveAsync(job with
                    {
                        State = DownloadState.Complete, Bytes = downloaded.Sum(file => file.Bytes), Assets = downloaded,
                        Pages = job.Selection.Type == "manga" ? downloaded.Length : 0,
                        TotalPages = job.Selection.Type == "manga" ? downloaded.Length : null
                    });
                }
                catch
                {
                    TryDeleteDirectory(destination);
                    await SaveAsync(job with { State = DownloadState.Failed, Message = "Could not finalize the download." });
                }
            }
        }
        finally
        {
            TryDeleteDirectory(folder);
            activeId = null;
            activeCancellation = null;
            cancellation.Dispose();
            gate.Release();
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { DeleteDirectory(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
