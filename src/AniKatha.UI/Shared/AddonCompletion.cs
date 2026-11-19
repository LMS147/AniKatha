using AniKatha.Core;

namespace AniKatha.UI.Shared;

internal static class AddonCompletion
{
    public static async Task<(string Message, Task<string>? Sync)> SaveAsync(LocalStore store, AniListAccount? account, string unitKey)
    {
        var mapping = await store.GetAddonAniListProgressAsync(unitKey);
        AniListViewer? viewer = null;
        if (mapping is not null && account is not null)
        {
            try { viewer = await account.StoredViewerAsync(); }
            catch { /* Local completion remains available if secure storage cannot be read. */ }
        }

        var queued = await store.CompleteAddonUnitAsync(unitKey, viewer?.Id, mapping);
        if (queued && viewer is not null && account is not null && mapping is not null)
            return ("Saved on this device · Waiting to sync", SendAsync(account, store, viewer.Id, mapping));
        if (mapping is null)
            return ("Progress saved on this device. This unit has no verified AniList match.", null);
        if (await store.GetSettingAsync(LocalStore.AutomaticAniListKey) == "false")
            return ("Progress saved on this device. Automatic AniList tracking is off.", null);
        if (viewer is null)
            return ("Progress saved on this device. Connect AniList to sync future completions.", null);
        return ("Progress saved on this device. Check the imported title and earlier units in Library.", null);
    }

    private static async Task<string> SendAsync(AniListAccount account, LocalStore store, int viewer, AddonAniListProgress mapping)
    {
        try
        {
            await account.SyncPendingAsync(store);
            var pending = await store.AniListEditsAsync(viewer);
            if (!pending.Any(edit => edit.MediaId == mapping.MediaId && edit.Field == "progress"))
            {
                var title = (await store.GetLibraryAsync()).Find(item => item.Id == mapping.MediaId);
                return title?.Progress >= mapping.Progress && await store.HasAcknowledgedTrackingAsync(viewer, mapping.MediaId)
                    ? "AniList progress confirmed." :
                    "Completed on this device. AniList kept its current count.";
            }
        }
        catch { /* The saved outbox retains changes for the next sync attempt. */ }
        try
        {
            if (await account.CachedLinkAsync() is { RequiresReconnect: true })
                return "Saved on this device · Reconnect AniList to sync";
        }
        catch { }
        return "Saved on this device · Waiting to sync. Retry from Profile.";
    }
}
