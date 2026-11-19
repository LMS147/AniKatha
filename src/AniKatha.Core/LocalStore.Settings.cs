namespace AniKatha.Core;

public sealed partial class LocalStore
{
    internal async Task<IReadOnlyList<AppSetting>> GetSettingsAsync(string prefix)
    {
        await initialized.Value;
        return await db.QueryAsync<AppSetting>("SELECT * FROM AppSetting WHERE Key LIKE ? ORDER BY rowid", prefix + "%");
    }

    internal async Task DeleteSettingAsync(string key)
    {
        await initialized.Value;
        await db.DeleteAsync<AppSetting>(key);
    }
}
