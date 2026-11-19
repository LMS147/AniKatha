namespace AniKatha.Core;

public interface IExternalVideoPlayer
{
    Task<bool> OpenAsync(Uri video);
}
