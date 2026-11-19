using System.Globalization;
using System.Text.Json;

namespace AniKatha.Core;

public sealed partial class LocalStore
{
    private static string AddonAniListKey(string unitKey) => $"anilist-unit:{unitKey}";

    public Task SaveAddonAniListProgressAsync(string unitKey, AddonAniListProgress? mapping) =>
        SetSettingAsync(AddonAniListKey(unitKey), JsonSerializer.Serialize(mapping));

    public const string AutomaticAniListKey = "anilist:automatic-progress";
    private static string VerifiedCompletionKey(int viewer, int media, int count) =>
        $"anilist:{viewer}:completed:{media}:{count}";
    private static string VerifiedCompletionPattern(int viewer, int media) =>
        $"anilist:{viewer}:completed:{media}:%";

    public async Task<AddonAniListProgress?> GetAddonAniListProgressAsync(string unitKey)
    {
        var json = await GetSettingAsync(AddonAniListKey(unitKey));
        return json is null ? null : JsonSerializer.Deserialize<AddonAniListProgress>(json);
    }

    public async Task<bool> CompleteAddonUnitAsync(string unitKey, int? viewerId, AddonAniListProgress? expectedMapping = null)
    {
        await ReadyAsync();
        var queued = false;
        await db.RunInTransactionAsync(connection =>
        {
            var completionKey = $"completed:{unitKey}";
            if (connection.Find<AppSetting>(completionKey)?.Value == "true") return;
            connection.InsertOrReplace(new AppSetting { Key = completionKey, Value = "true" });

            if (viewerId is not int viewer || connection.Find<AppSetting>(AutomaticAniListKey)?.Value == "false") return;
            var mappingJson = connection.Find<AppSetting>(AddonAniListKey(unitKey))?.Value;
            var mapping = mappingJson is null ? null : JsonSerializer.Deserialize<AddonAniListProgress>(mappingJson);
            if (mapping is null || (expectedMapping is not null && mapping != expectedMapping)) return;
            var title = connection.Find<SavedTitle>(mapping.MediaId);
            if (title is null || title.ProviderKey is not null || title.Kind != mapping.Kind ||
                title.Progress >= mapping.Progress) return;

            var baselineJson = connection.Find<AppSetting>(AniListBaselineKey(viewer, mapping.MediaId))?.Value;
            if (baselineJson is null || JsonSerializer.Deserialize<AniListListEntry>(baselineJson) is not { } baseline ||
                baseline.Kind != mapping.Kind) return;

            var key = QueueKey(viewer, mapping.MediaId, "progress");
            var pendingJson = connection.Find<AppSetting>(key)?.Value;
            var expected = baseline.Progress;
            if (pendingJson is not null)
            {
                var pending = JsonSerializer.Deserialize<AniListEdit>(pendingJson);
                if (pending?.ExpectedProgress is not int floor ||
                    pending.Value != title.Progress.ToString(CultureInfo.InvariantCulture)) return;
                expected = floor;
            }
            else if (title.Progress != baseline.Progress) return;

            connection.InsertOrReplace(new AppSetting { Key = VerifiedCompletionKey(viewer, mapping.MediaId, mapping.Progress), Value = "true" });
            var count = title.Progress;
            while (count < int.MaxValue && connection.Find<AppSetting>(VerifiedCompletionKey(viewer, mapping.MediaId, count + 1)) is not null)
                count++;
            if (count == title.Progress) return;

            title.Progress = count;
            title.UpdatedAt = DateTime.UtcNow;
            connection.Update(title);
            var edit = new AniListEdit(mapping.MediaId, "progress", count.ToString(CultureInfo.InvariantCulture),
                Guid.NewGuid().ToString("N"), null, ExpectedProgress: expected);
            connection.InsertOrReplace(new AppSetting { Key = key, Value = JsonSerializer.Serialize(edit) });
            queued = true;
        });
        return queued;
    }
}
