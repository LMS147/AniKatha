using System.Text.Json;
using SQLite;

namespace AniKatha.Core;

public sealed partial class LocalStore
{
    private static string AniListBaselineKey(int account, int media) => $"anilist:{account}:baseline:{media}";

    public async Task ImportAniListPageAsync(int account, IReadOnlyList<AniListListEntry> entries, CancellationToken token = default)
    {
        await initialized.Value;
        await db.RunInTransactionAsync(connection =>
        {
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.MediaId <= 0 || entry.Kind is not ("ANIME" or "MANGA")) continue;
                var title = connection.Find<SavedTitle>(entry.MediaId);
                if (title is not null && (title.ProviderKey is not null || title.Kind != entry.Kind)) continue;
                var key = AniListBaselineKey(account, entry.MediaId);
                var baseline = connection.Find<AppSetting>(key)?.Value is { } json
                    ? JsonSerializer.Deserialize<AniListListEntry>(json) : null;
                if (title is null)
                {
                    title = new SavedTitle
                    {
                        Id = entry.MediaId, Kind = entry.Kind, Title = entry.Title, Poster = entry.Poster,
                        Status = entry.Status, Progress = entry.Progress, UpdatedAt = DateTime.UtcNow
                    };
                    connection.Insert(title);
                }
                else if (baseline is not null)
                {
                    if (title.Status == baseline.Status) title.Status = entry.Status;
                    if (title.Progress == baseline.Progress) title.Progress = entry.Progress;
                    connection.Update(title);
                }
                connection.InsertOrReplace(new AppSetting { Key = key, Value = JsonSerializer.Serialize(entry) });
            }
        });
    }
}
