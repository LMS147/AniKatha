using Android.Content;
using AniKatha.Core;
using Microsoft.Maui.ApplicationModel;

namespace AniKatha.Mobile;

internal sealed class ExternalVideoPlayer : IExternalVideoPlayer
{
    public Task<bool> OpenAsync(Uri video) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (!AddonResources.MediaUrl(video.AbsoluteUri) || Platform.CurrentActivity is not { } activity)
            return false;
        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(Android.Net.Uri.Parse(video.AbsoluteUri), "video/*");
        try
        {
            activity.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException)
        {
            return false;
        }
    });
}
