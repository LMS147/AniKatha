using Microsoft.Extensions.DependencyInjection;
using AniKatha.Core;
namespace AniKatha.Mobile;
public partial class App : Application
{
 readonly IServiceProvider services;
 readonly LocalProfiles profiles;

 public App(IServiceProvider services)
 {
  InitializeComponent();
  this.services = services;
  profiles = services.GetRequiredService<LocalProfiles>();
  profiles.Changed += OnProfileChanged;
  Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
  _ = InitializeProfilesAsync();
 }

 async Task InitializeProfilesAsync()
 {
  try { await profiles.InitializeAsync(); }
  catch { /* The app displays a retry action at startup. */ }
 }

 void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
 {
  if (args.NetworkAccess == NetworkAccess.Internet && profiles.Session is { } session)
   _ = SyncAniListAsync(session);
 }

 void OnProfileChanged()
 {
  if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && profiles.Session is { } session)
   _ = SyncAniListAsync(session);
 }

 static async Task SyncAniListAsync(ProfileSession session)
 {
  try
  {
   if (session.AniList is { } account) await account.SyncPendingAsync(session.Store);
  }
  catch { /* The profile's persisted queue is available to retry. */ }
 }

 protected override Window CreateWindow(IActivationState? activationState) =>
  new(services.GetRequiredService<MainPage>());
}
