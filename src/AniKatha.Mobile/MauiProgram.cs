using AniKatha.Core;
using Microsoft.Extensions.DependencyInjection;
namespace AniKatha.Mobile;
public static class MauiProgram
{
 private sealed class AniListTransport
 {
  public AniListTransport() { }
  public HttpClient Http { get; } = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
 }

 public static MauiApp CreateMauiApp()
 {
  var builder=MauiApp.CreateBuilder();
  builder.UseMauiApp<App>();
  builder.Services.AddMauiBlazorWebView();
#if DEBUG
  builder.Services.AddBlazorWebViewDeveloperTools();
#endif
  builder.Services.AddSingleton(new AniListClient(new HttpClient { Timeout=TimeSpan.FromSeconds(25) }));
  builder.Services.AddSingleton(new JikanCatalogue(new HttpClient { Timeout=TimeSpan.FromSeconds(25) }));
  builder.Services.AddSingleton(new AddonClient(new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(20) }));
  builder.Services.AddSingleton(services => new LocalProfiles(FileSystem.AppDataDirectory, "anikatha.db3",
   services.GetRequiredService<AddonClient>(), new AndroidProfileSecrets(),
   id => new AniListAccount(services.GetRequiredService<AniListTransport>().Http, new AniListAuthorization(id)),
   store => new YouTubePlayer(store)));
  builder.Services.AddSingleton<AniListTransport>();
  builder.Services.AddSingleton<IAppearancePlatform, NativeAppearance>();
  builder.Services.AddSingleton<IExternalVideoPlayer, ExternalVideoPlayer>();
  builder.Services.AddSingleton<MainPage>();
  return builder.Build();
 }
}
