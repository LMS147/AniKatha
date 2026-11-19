using System.Text.Json;
using System.Globalization;

namespace AniKatha.Core;

public sealed partial class LocalStore
{
    private static string AniListBaselineKey(int account, int media) => $"anilist:{account}:baseline:{media}";

    private static string AniListScoreKey(int account, int media, string? format) => $"anilist:{account}:score:{media}:{format}";

    public async Task<double?> GetAniListScoreAsync(int account, int media, string format)
    {
        var value = await GetSettingAsync(AniListScoreKey(account, media, format));
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score) && score > 0 ? score : null;
    }

    public async Task ImportAniListPageAsync(int account, IReadOnlyList<AniListListEntry> entries, CancellationToken token = default)
    {
        await ReadyAsync();
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
                    if (title.Status == baseline.Status && connection.Find<AppSetting>(QueueKey(account, entry.MediaId, "status")) is null)
                        title.Status = entry.Status;
                    if (title.Progress == baseline.Progress && connection.Find<AppSetting>(QueueKey(account, entry.MediaId, "progress")) is null)
                        title.Progress = entry.Progress;
                    connection.Update(title);
                }
                var scoreKey = AniListScoreKey(account, entry.MediaId, entry.ScoreFormat);
                var score = connection.Find<AppSetting>(scoreKey);
                var pendingScore = connection.Find<AppSetting>(QueueKey(account, entry.MediaId, "score"));
                if (score is null || (pendingScore is null && baseline?.ScoreFormat == entry.ScoreFormat &&
                    score.Value == (baseline.Score?.ToString(CultureInfo.InvariantCulture) ?? "")))
                    connection.InsertOrReplace(new AppSetting { Key = scoreKey, Value = entry.Score?.ToString(CultureInfo.InvariantCulture) ?? "" });
                connection.InsertOrReplace(new AppSetting { Key = key, Value = JsonSerializer.Serialize(entry) });
            }
        });
    }
}
