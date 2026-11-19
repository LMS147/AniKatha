using AndroidX.Core.View;
using AniKatha.Core;

namespace AniKatha.Mobile;

internal sealed class NativeAppearance : IAppearancePlatform
{
    private Application? observed;

    public Task ApplyAsync(string mode) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (Application.Current is not { } app) return;
        if (observed != app)
        {
            if (observed is not null) observed.RequestedThemeChanged -= OnThemeChanged;
            app.RequestedThemeChanged += OnThemeChanged;
            observed = app;
        }

        app.UserAppTheme = mode switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
        ApplySystemBars(app.RequestedTheme);
    });

    private static void OnThemeChanged(object? sender, AppThemeChangedEventArgs args) =>
        MainThread.BeginInvokeOnMainThread(() => ApplySystemBars(args.RequestedTheme));

    private static void ApplySystemBars(AppTheme theme)
    {
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window is not { } window) return;
        var light = theme == AppTheme.Light;
        using var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        controller.AppearanceLightStatusBars = light;
        controller.AppearanceLightNavigationBars = light;
    }
}
