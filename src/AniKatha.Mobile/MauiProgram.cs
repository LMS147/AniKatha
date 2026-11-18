using AniKatha.Core;
using Microsoft.Extensions.DependencyInjection;
namespace AniKatha.Mobile;
public static class MauiProgram
{
 public static MauiApp CreateMauiApp()
 {
  var builder=MauiApp.CreateBuilder();
  builder.UseMauiApp<App>();
  builder.Services.AddMauiBlazorWebView();
#if DEBUG
  builder.Services.AddBlazorWebViewDeveloperTools();
#endif
  builder.Services.AddSingleton(_ => new LocalStore(Path.Combine(FileSystem.AppDataDirectory,"anikatha.db3")));
  builder.Services.AddSingleton(new AniListClient(new HttpClient { Timeout=TimeSpan.FromSeconds(25) }));
  builder.Services.AddSingleton(new JikanCatalogue(new HttpClient { Timeout=TimeSpan.FromSeconds(25) }));
  builder.Services.AddSingleton(new AddonClient(new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(20) }));
  builder.Services.AddSingleton<IYouTubePlayer, YouTubePlayer>();
  builder.Services.AddSingleton<MainPage>();
  return builder.Build();
 }
}
