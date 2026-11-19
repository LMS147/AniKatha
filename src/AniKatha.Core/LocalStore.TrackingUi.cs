using System.Globalization;
using System.Text.Json;

namespace AniKatha.Core;

public sealed record AniListImportSummary(bool HasImported, DateTimeOffset? LastImport, int? Count = null);

public sealed partial class LocalStore
{
    public event Action? AniListChanged;

    private void NotifyAniListChanged() => AniListChanged?.Invoke();

    private static string AniListConfirmationKey(int account, int media) => $"anilist:{account}:confirmed:{media}";
    private static string AniListKnownTotalKey(int media) => $"tracking:total:{media}";
    private static string AniListImportKey(int account) => $"anilist:{account}:last-import";

    internal Task RecordAniListImportAsync(int account, int count) =>
        SetSettingAsync(AniListImportKey(account), JsonSerializer.Serialize(new AniListImportSummary(true, DateTimeOffset.UtcNow, count)));

    public async Task<AniListImportSummary> ReadAniListImportAsync(int account)
    {
        if (await GetSettingAsync(AniListImportKey(account)) is { } value)
            return JsonSerializer.Deserialize<AniListImportSummary>(value) ?? new(false, null);
        await ReadyAsync();
        var hasEntries = await db.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM AppSetting WHERE Key LIKE ?)",
            $"anilist:{account}:baseline:%");
        return new(hasEntries != 0, null);
    }

    public async Task<int?> GetTrackingTotalAsync(int? account, int media)
    {
        if (account is int id && await GetSettingAsync(AniListBaselineKey(id, media)) is { } baseline &&
            JsonSerializer.Deserialize<AniListListEntry>(baseline)?.Total is int total && total > 0)
            return total;
        return int.TryParse(await GetSettingAsync(AniListKnownTotalKey(media)), NumberStyles.None,
            CultureInfo.InvariantCulture, out var known) && known > 0 ? known : null;
    }

    public async Task<IReadOnlyDictionary<int, int>> TrackingTotalsAsync(int? account)
    {
        var totals = new Dictionary<int, int>();
        foreach (var setting in await GetSettingsAsync("tracking:total:"))
            if (int.TryParse(setting.Key.AsSpan("tracking:total:".Length), out var key) &&
                int.TryParse(setting.Value, out var known) && known > 0) totals[key] = known;
        if (account is int id)
            foreach (var setting in await GetSettingsAsync($"anilist:{id}:baseline:"))
                if (JsonSerializer.Deserialize<AniListListEntry>(setting.Value) is { Total: > 0 } entry)
                    totals[entry.MediaId] = entry.Total.Value;
        return totals;
    }

    public async Task<bool> HasAcknowledgedTrackingAsync(int account, int media) =>
        await GetSettingAsync(AniListConfirmationKey(account, media)) == "true";

    public async Task<IReadOnlySet<int>> ConfirmedTrackingTitlesAsync(int account)
    {
        var prefix = $"anilist:{account}:confirmed:";
        var entries = await GetSettingsAsync(prefix);
        return entries.Select(entry => int.TryParse(entry.Key.AsSpan(prefix.Length), out var id) ? id : 0).ToHashSet();
    }

    public async Task SaveLocalTrackingAsync(int id, string? status, int? progress)
    {
        await ReadyAsync();
        await db.RunInTransactionAsync(connection =>
        {
            var item = connection.Find<SavedTitle>(id) ?? throw new InvalidOperationException("This title is no longer in Library.");
            if (status is not null) item.Status = status;
            if (progress is int count) item.Progress = count;
            item.UpdatedAt = DateTime.UtcNow;
            connection.Update(item);
            connection.Execute("DELETE FROM AppSetting WHERE Key LIKE ?", $"anilist:%:confirmed:{id}");
        });
        NotifyAniListChanged();
    }
}
