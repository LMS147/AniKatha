namespace AniKatha.UI;

public sealed class PlayerBackHandler
{
    private object? owner;
    private Func<Task>? callback;

    public void Register(object player, Func<Task> handleBack)
    {
        owner = player;
        callback = handleBack;
    }

    public bool TryHandle()
    {
        if (callback is not { } handleBack) return false;
        _ = handleBack();
        return true;
    }

    public void Clear(object player)
    {
        if (!ReferenceEquals(owner, player)) return;
        owner = null;
        callback = null;
    }
}
