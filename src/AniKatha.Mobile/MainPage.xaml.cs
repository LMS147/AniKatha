using AniKatha.Core;

namespace AniKatha.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage(LocalProfiles profiles)
    {
        InitializeComponent();
        OfflineView.Profiles = profiles;
    }
}
