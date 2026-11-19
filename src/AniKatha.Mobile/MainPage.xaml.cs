using AniKatha.Core;
using AniKatha.UI;

namespace AniKatha.Mobile;

public partial class MainPage : ContentPage
{
    private readonly PlayerBackHandler playerBack;

    public MainPage(LocalProfiles profiles, PlayerBackHandler playerBack)
    {
        this.playerBack = playerBack;
        InitializeComponent();
        OfflineView.Profiles = profiles;
    }

    protected override bool OnBackButtonPressed() => playerBack.TryHandle() || base.OnBackButtonPressed();
}
