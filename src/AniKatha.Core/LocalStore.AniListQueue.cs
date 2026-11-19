using System.Globalization;
using System.Text.Json;
using SQLite;

namespace AniKatha.Core;

public sealed record AniListEdit(int MediaId, string Field, string Value, string Revision, string? ScoreFormat, bool Failed = false, int? ExpectedProgress = null);

public sealed partial class LocalStore
{
    private static string QueuePrefix(int account) => $"anilist:{account}:edit:";
    private static string QueueKey(int account, int media, string field) => $"{QueuePrefix(account)}{media}:{field}";

    public async Task QueueAniListEditAsync(int account, int media, string field, string value, string? scoreFormat = null)
    {
        await ReadyAsync();
        await db.RunInTransactionAsync(connection =>
        {
            var item = connection.Find<SavedTitle>(media);
            if (account <= 0 || media <= 0 || item is null || item.ProviderKey is not null)
                throw new InvalidOperationException("AniList changes require a saved AniList title.");
            switch (field)
            {
                case "status":
                    item.Status = value;
                    item.UpdatedAt = DateTime.UtcNow;
                    connection.Update(item);
                    break;
                case "progress":
                    connection.Execute("DELETE FROM AppSetting WHERE Key LIKE ?", VerifiedCompletionPattern(account, media));
                    item.Progress = int.Parse(value, CultureInfo.InvariantCulture);
                    item.UpdatedAt = DateTime.UtcNow;
                    connection.Update(item);
                    break;
                case "score":
                    connection.InsertOrReplace(new AppSetting { Key = AniListScoreKey(account, media, scoreFormat), Value = value == "0" ? "" : value });
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
            var edit = new AniListEdit(media, field, value, Guid.NewGuid().ToString("N"), scoreFormat);
            connection.InsertOrReplace(new AppSetting { Key = QueueKey(account, media, field), Value = JsonSerializer.Serialize(edit) });
        });
    }

    public async Task<IReadOnlyList<AniListEdit>> AniListEditsAsync(int account)
    {
        await ReadyAsync();
        var settings = await db.QueryAsync<AppSetting>("SELECT * FROM AppSetting WHERE Key LIKE ? ORDER BY Key", QueuePrefix(account) + "%");
        return settings.Select(setting => JsonSerializer.Deserialize<AniListEdit>(setting.Value)!).ToList();
    }

    public async Task<bool> IsCurrentAniListEditAsync(int account, AniListEdit edit)
    {
        var value = await GetSettingAsync(QueueKey(account, edit.MediaId, edit.Field));
        return value is not null && JsonSerializer.Deserialize<AniListEdit>(value)?.Revision == edit.Revision;
    }

    public async Task FinishAniListEditAsync(int account, AniListEdit edit, bool success, int? remoteProgress = null)
    {
        await ReadyAsync();
        await db.RunInTransactionAsync(connection =>
        {
            var key = QueueKey(account, edit.MediaId, edit.Field);
            var current = connection.Find<AppSetting>(key);
            if (current is null || JsonSerializer.Deserialize<AniListEdit>(current.Value)?.Revision != edit.Revision) return;
            if (!success)
            {
                connection.Update(new AppSetting { Key = key, Value = JsonSerializer.Serialize(edit with { Failed = true }) });
                return;
            }
            connection.Delete<AppSetting>(key);
            if (remoteProgress is int remote && edit.ExpectedProgress is int floor && remote < floor)
                connection.Execute("DELETE FROM AppSetting WHERE Key LIKE ?", VerifiedCompletionPattern(account, edit.MediaId));
            if (remoteProgress is int progress && connection.Find<SavedTitle>(edit.MediaId) is { } title &&
                title.ProviderKey is null && title.Progress.ToString(CultureInfo.InvariantCulture) == edit.Value)
            {
                title.Progress = progress;
                connection.Update(title);
            }
            var baselineKey = AniListBaselineKey(account, edit.MediaId);
            var baselineSetting = connection.Find<AppSetting>(baselineKey);
            if (baselineSetting is null || JsonSerializer.Deserialize<AniListListEntry>(baselineSetting.Value) is not { } baseline) return;
            var updated = edit.Field switch
            {
                "status" => baseline with { Status = edit.Value },
                "progress" => baseline with { Progress = remoteProgress ?? int.Parse(edit.Value, CultureInfo.InvariantCulture) },
                "score" when double.TryParse(edit.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score) =>
                    baseline with { Score = score > 0 ? score : null, ScoreFormat = edit.ScoreFormat ?? baseline.ScoreFormat },
                _ => baseline
            };
            connection.Update(new AppSetting { Key = baselineKey, Value = JsonSerializer.Serialize(updated) });
        });
    }
}
