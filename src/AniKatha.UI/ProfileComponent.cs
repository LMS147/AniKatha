using AniKatha.Core;
using Microsoft.AspNetCore.Components;

namespace AniKatha.UI;

public abstract class ProfileComponent : ComponentBase
{
    [Inject] private IServiceProvider ServiceProvider { get; set; } = null!;
    [CascadingParameter] public ProfileSession? ProfileSession { get; set; }

    protected LocalStore Store => ProfileSession?.Store ??
        (LocalStore)ServiceProvider.GetService(typeof(LocalStore))!;
    protected OfflineDownloads? ProfileDownloads => ProfileSession is { } session ? session.Downloads :
        (OfflineDownloads?)ServiceProvider.GetService(typeof(OfflineDownloads));
    protected AniListAccount? ProfileAniList => ProfileSession is { } session ? session.AniList :
        (AniListAccount?)ServiceProvider.GetService(typeof(AniListAccount));
    protected IYouTubePlayer? ProfileYouTube => ProfileSession is { } session ? session.YouTube :
        (IYouTubePlayer?)ServiceProvider.GetService(typeof(IYouTubePlayer));
}
