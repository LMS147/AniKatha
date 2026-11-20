using System.Text;
using AniKatha.Core;

internal static class BackupChecks
{
    public static async Task RunAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "anikatha-backup-" + Guid.NewGuid().ToString("N"));
        var original = new LocalStore(Path.Combine(folder, "original.db3"));
        var restored = new LocalStore(Path.Combine(folder, "restored.db3"));
        await original.SaveAsync(new SavedTitle { Id = 101, Title = "Anime", Kind = "ANIME", Progress = 8,
            Status = "CURRENT", IsFavourite = true, Poster = "https://example.com/cover.png?private=secret" });
        await original.SaveAsync(new SavedTitle { Id = 102, Title = "Manga", Kind = "MANGA", Progress = 12 });
        var provider = new string('A', 64);
        await original.SaveProviderTitleAsync(provider, "anime", "provider-title", "Provider anime");
        await restored.SaveProviderTitleAsync(provider, "manga", "other-title", "Existing provider manga");

        async Task<string> AddHistory(string kind, string unit)
        {
            var key = ResumeEntry.Key(provider, kind, "title", unit, provider);
            var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            await original.RememberAsync(new ResumeEntry { Id = id, Origin = provider, Source = provider,
                Type = kind, TitleId = "title", UnitId = unit, Title = kind });
            return key;
        }

        var episode = await AddHistory("anime", "episode-2");
        var chapter = await AddHistory("manga", "chapter-3");
        await original.SavePlaybackPositionAsync(episode, 123.456789);
        await original.SaveReadingPageAsync(chapter, 27);
        await original.CompleteUnitAsync(chapter);
        await original.SaveHomeUnitLengthAsync(chapter, 32);
        await original.SetSettingAsync("anilist:token", "secret");
        await original.SaveAddonAsync(new() { Url = "https://example.com/manifest.json?key=secret", Name = "Source" });

        var json = await original.ExportLibraryBackupAsync();
        Require(!json.Contains("secret", StringComparison.Ordinal), "Credentials excluded");
        using var file = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var backup = await LocalStore.ReadLibraryBackupAsync(file);
        var preview = await restored.MergeLibraryBackupAsync(backup);
        Require(preview == new BackupMerge(2, 1, 2, 4, 0) && (await restored.GetLibraryAsync()).Count == 1, "Preview does not write");
        Require(await restored.MergeLibraryBackupAsync(backup, apply: true) == preview, "Matches preview");
        var titles = await restored.GetLibraryAsync();
        Require(titles.Single(item => item.Id == 101) is { IsFavourite: true, Status: "CURRENT", Progress: 8, Poster: null } &&
            titles.Single(item => item.Id == 102) is { Kind: "MANGA", Progress: 12 }, "Portable title fields round trip");
        Require(await restored.GetPlaybackPositionAsync(episode) == 123.456789 &&
            await restored.GetReadingPageAsync(chapter) == 27 && await restored.IsUnitCompleteAsync(chapter) &&
            await restored.GetHomeUnitLengthAsync(chapter) == 32, "Exact checkpoints round trip");
        await restored.SaveReadingPageAsync(chapter, 29);
        await restored.SaveAsync(new SavedTitle { Id = 101, Title = "Edited locally", Kind = "ANIME", Progress = 11 });
        Require(await restored.MergeLibraryBackupAsync(backup, apply: true) == new BackupMerge(0, 0, 0, 0, 9) &&
            (await restored.GetLibraryAsync()).Single(item => item.Id == 101).Progress == 11 &&
            await restored.GetReadingPageAsync(chapter) == 29, "Repeat import skips existing titles and checkpoints");

        try
        {
            await restored.MergeLibraryBackupAsync(backup with { Version = 99 }, apply: true);
            throw new Exception("Invalid version accepted");
        }
        catch (InvalidDataException) { }
        Require((await restored.GetLibraryAsync()).Count == 4 && (await restored.HistoryAsync()).Count == 2,
            "Invalid import leaves records intact");
        Console.WriteLine("PASS: Anime and Manga backup round trip, preview, credential exclusion, reimport, and invalid file preservation.");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
