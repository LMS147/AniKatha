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
            return ("Completed on this device. AniList sync pending.", SendAsync(account, store, viewer.Id, mapping));
        if (mapping is null)
            return ("Completed on this device. This unit has no verified AniList mapping.", null);
        if (await store.GetSettingAsync(LocalStore.AutomaticAniListKey) == "false")
            return ("Completed on this device. Automatic AniList tracking is off.", null);
        if (viewer is null)
            return ("Completed on this device. Connect AniList to track future completions.", null);
        return ("Completed on this device. Check the imported title and any earlier units in Library.", null);
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
                return title?.Progress >= mapping.Progress ? "AniList progress confirmed." :
                    "Completed on this device. AniList kept its current count.";
            }
        }
        catch { /* The saved outbox retains changes for the next sync attempt. */ }
        return "Completed on this device. AniList sync pending. Retry from Profile.";
    }
}
