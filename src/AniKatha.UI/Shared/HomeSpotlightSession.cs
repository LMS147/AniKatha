namespace AniKatha.UI.Shared;

// MainLayout is kept when navigating between Home and playback routes.
public sealed class HomeSpotlightSession
{
    public bool Manga { get; set; }
    public int? MediaId { get; set; }
}
