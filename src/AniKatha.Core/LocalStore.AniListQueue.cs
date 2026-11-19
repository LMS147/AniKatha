using System.Globalization;
using System.Text.Json;
using SQLite;

namespace AniKatha.Core;

public sealed record AniListEdit(int MediaId, string Field, string Value, string Revision, string? ScoreFormat, bool Failed = false);

public sealed partial class LocalStore
{
    private static string QueuePrefix(int account) => $"anilist:{account}:edit:";
    private static string QueueKey(int account, int media, string field) => $"{QueuePrefix(account)}{media}:{field}";

    public async Task QueueAniListEditAsync(int account, int media, string field, string value, string? scoreFormat = null)
    {
        await initialized.Value;
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
                    item.Progress = int.Parse(value, CultureInfo.InvariantCulture);
                    item.UpdatedAt = DateTime.UtcNow;
                    connection.Update(item);
                    break;
                case "score":
                    connection.InsertOrReplace(new AppSetting { Key = AniListScoreKey(account, media), Value = value == "0" ? "" : value });
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
            var edit = new AniListEdit(media, field, value, Guid.NewGuid().ToString("N"), scoreFormat);
            connection.InsertOrReplace(new AppSetting { Key = QueueKey(account, media, field), Value = JsonSerializer.Serialize(edit) });
        });
    }

    public async Task<IReadOnlyList<AniListEdit>> AniListEditsAsync(int account)
    {
        await initialized.Value;
        var settings = await db.QueryAsync<AppSetting>("SELECT * FROM AppSetting WHERE Key LIKE ? ORDER BY Key", QueuePrefix(account) + "%");
        return settings.Select(setting => JsonSerializer.Deserialize<AniListEdit>(setting.Value)!).ToList();
    }

    public async Task FinishAniListEditAsync(int account, AniListEdit edit, bool success)
    {
        await initialized.Value;
        await db.RunInTransactionAsync(connection =>
        {
            var key = QueueKey(account, edit.MediaId, edit.Field);
            var current = connection.Find<AppSetting>(key);
            if (current is null || JsonSerializer.Deserialize<AniListEdit>(current.Value)?.Revision != edit.Revision) return;
            if (success) connection.Delete<AppSetting>(key);
            else connection.Update(new AppSetting { Key = key, Value = JsonSerializer.Serialize(edit with { Failed = true }) });
        });
    }
}
