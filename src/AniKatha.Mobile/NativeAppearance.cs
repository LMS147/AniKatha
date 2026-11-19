using AniKatha.Core;

namespace AniKatha.Mobile;

internal sealed class NativeAppearance : IAppearancePlatform
{
    public Task ApplyAsync(string mode) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (Application.Current is { } app)
            app.UserAppTheme = mode switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
    });
}
