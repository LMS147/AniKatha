using System.Globalization;
using AniKatha.Core;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace AniKatha.Mobile;

public sealed class YouTubePlayer(LocalStore store) : IYouTubePlayer
{
 public Task OpenAsync(YouTubeEpisode episode) => MainThread.InvokeOnMainThreadAsync(async () =>
 {
  if (!YouTubeEpisode.IsVideoId(episode.VideoId))
   throw new ArgumentException("Invalid YouTube video ID.", nameof(episode));

  var root = Application.Current?.Windows.FirstOrDefault()?.Page
   ?? throw new InvalidOperationException("AniKatha has no active window.");
  var position = await store.GetPlaybackPositionAsync(episode.PlaybackKey);
  await root.Navigation.PushModalAsync(new PlayerPage(episode, position, store));
 });

 sealed class PlayerPage : ContentPage
 {
  readonly WebView web;
  readonly IDispatcherTimer timer;
  readonly SemaphoreSlim saveGate = new(1);
  readonly LocalStore store;
  readonly YouTubeEpisode episode;
  double savedPosition;
  bool closing;

  internal PlayerPage(YouTubeEpisode episode, double position, LocalStore store)
  {
   this.episode = episode;
   this.store = store;
   savedPosition = position;

   var origin = $"https://{AppInfo.PackageName.ToLowerInvariant()}";
   web = new WebView
   {
    Source = new HtmlWebViewSource
    {
     BaseUrl = origin + "/",
     Html = PlayerHtml(episode.VideoId, origin, position)
    }
   };
   var close = new Button { Text = "Close player" };
   close.Clicked += async (_, _) =>
   {
    if (closing) return;
    closing = true;
    close.IsEnabled = false;
    timer!.Stop();
    await SaveAsync();
    await Navigation.PopModalAsync();
   };
   var external = new Button { Text = "Open in YouTube" };
   external.Clicked += async (_, _) => await Launcher.Default.OpenAsync(new Uri(episode.WatchUrl));

   var toolbar = new Grid { Padding = new Thickness(8), ColumnSpacing = 8 };
   toolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
   toolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
   toolbar.Add(close, 0);
   toolbar.Add(external, 1);

   var layout = new Grid();
   layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
   layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
   layout.Add(toolbar);
   layout.Add(web, 0, 1);
   Content = layout;

   timer = Dispatcher.CreateTimer();
   timer.Interval = TimeSpan.FromSeconds(3);
   timer.Tick += async (_, _) =>
   {
    if (saveGate.CurrentCount > 0)
     await SaveAsync();
   };
  }

  protected override void OnAppearing()
  {
   base.OnAppearing();
   timer.Start();
  }

  protected override void OnDisappearing()
  {
   timer.Stop();
   if (!closing)
    _ = SaveAsync();
   base.OnDisappearing();
  }

  async Task SaveAsync()
  {
   await saveGate.WaitAsync();
   try
   {
    var response = await web.EvaluateJavaScriptAsync("window.checkpoint ? window.checkpoint() : -1")
     .WaitAsync(TimeSpan.FromSeconds(2));
    if (double.TryParse(response, NumberStyles.Float, CultureInfo.InvariantCulture, out var position)
     && double.IsFinite(position) && position >= 0 && position != savedPosition)
    {
     await store.SavePlaybackPositionAsync(episode.PlaybackKey, position);
     savedPosition = position;
    }
   }
   catch (Exception ex)
   {
    System.Diagnostics.Debug.WriteLine($"YouTube checkpoint unavailable: {ex.Message}");
   }
   finally { saveGate.Release(); }
  }
 }

 static string PlayerHtml(string id, string origin, double position) => $$"""
  <!doctype html><html lang="en"><head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="strict-origin-when-cross-origin">
  <style>
  body { margin:0; min-height:100dvh; display:grid; place-content:center; background:#0b0b10; color:#f5f4fa; font:16px system-ui; }
  main { width:min(100vw, 960px); }
  iframe { width:100%; min-height:200px; height:auto; aspect-ratio:16/9; border:0; }
  p { margin:12px 16px; }
  </style></head>
  <body><main><div id="player"></div><p id="status" role="status">Loading YouTube…</p></main>
  <script>
   let player;
   const statusText = document.getElementById('status');
   function onYouTubeIframeAPIReady() {
    player = new YT.Player('player', {
     videoId: '{{id}}',
     playerVars: { origin: '{{origin}}', playsinline: 1, start: {{Math.Floor(position).ToString(CultureInfo.InvariantCulture)}} },
     events: {
      onReady: () => { statusText.textContent = ''; },
      onError: () => { statusText.textContent = 'This episode couldn’t play here. Try Open in YouTube above.'; }
     }
    });
   }
   window.checkpoint = () => {
    const state = player?.getPlayerState?.();
    if (state === 0) return 0;
    return state === 1 || state === 2 ? player.getCurrentTime() : -1;
   };
   const script = document.createElement('script');
   script.src = 'https://www.youtube.com/iframe_api';
   script.onerror = () => { statusText.textContent = 'YouTube couldn’t load. Check your internet connection.'; };
   document.head.appendChild(script);
  </script></body></html>
  """;
}
