namespace AniKatha.Core;

public sealed partial class AniListAccount
{
    private readonly SemaphoreSlim syncGate = new(1, 1);

    public async Task<int> SyncPendingAsync(LocalStore store, CancellationToken token = default)
    {
        await syncGate.WaitAsync(token);
        try
        {
            var viewer = await StoredViewerAsync();
            if (viewer is null) return 0;
            var edits = await store.AniListEditsAsync(viewer.Id);
            if (edits.Count == 0) return 0;
            if (edits.Any(edit => edit.Field == "score"))
            {
                var liveViewer = await CurrentAsync(token);
                if (liveViewer?.Id != viewer.Id) return 0;
                viewer = liveViewer;
            }
            var sent = 0;
            foreach (var edit in edits)
            {
                token.ThrowIfCancellationRequested();
                if (!await store.IsCurrentAniListEditAsync(viewer.Id, edit)) continue;
                try
                {
                    if (edit.Field == "score" && edit.ScoreFormat != viewer.ScoreFormat)
                        throw new InvalidOperationException("The score scale changed. Enter the score again.");
                    await UpdateFieldAsync(viewer.Id, edit.MediaId, edit.Field, edit.Value, token);
                    await store.FinishAniListEditAsync(viewer.Id, edit, true);
                    sent++;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    await store.FinishAniListEditAsync(viewer.Id, edit, false);
                    if (edit.Field == "score" && edit.ScoreFormat != viewer.ScoreFormat) continue;
                    break;
                }
            }
            return sent;
        }
        finally { syncGate.Release(); }
    }
}
