using Microsoft.Extensions.DependencyInjection;
using AniKatha.Core;
namespace AniKatha.Mobile;
public partial class App : Application
{
 readonly IServiceProvider services;
 public App(IServiceProvider services)
 {
  InitializeComponent();
  this.services = services;
  Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
  if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet) _ = SyncAniListAsync();
 }
 void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
 {
  if (args.NetworkAccess == NetworkAccess.Internet) _ = SyncAniListAsync();
 }

 async Task SyncAniListAsync()
 {
  try
  {
   await services.GetRequiredService<AniListAccount>().SyncPendingAsync(services.GetRequiredService<LocalStore>());
  }
  catch { /* The persisted queue is available to retry from Profile. */ }
 }

 // Construct the page after Application resources and the window lifecycle exist.
 protected override Window CreateWindow(IActivationState? activationState) =>
  new(services.GetRequiredService<MainPage>());
}
