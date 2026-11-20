using AniKatha.Core;

namespace AniKatha.Mobile;

internal sealed class AndroidBackupExport : IBackupExport
{
    public Task ShareAsync(string json) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "sharing-root");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "anikatha-library.json");
        await File.WriteAllTextAsync(path, json);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Save AniKatha backup",
            File = new ShareFile(path, "application/json")
        });
    });
}
