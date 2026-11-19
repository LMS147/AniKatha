using AniKatha.Core;
using AniKatha.Preview.Components;
var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new AniListClient(new HttpClient { Timeout=TimeSpan.FromSeconds(25) }));
builder.Services.AddSingleton(new JikanCatalogue(new HttpClient { Timeout=TimeSpan.FromSeconds(25) }));
builder.Services.AddSingleton(new AddonClient(new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(20) }));
builder.Services.AddSingleton(services => new LocalProfiles(Path.Combine(builder.Environment.ContentRootPath, "App_Data"),
 "preview.db3", services.GetRequiredService<AddonClient>()));
var app=builder.Build();
app.UseStaticFiles();app.UseAntiforgery();
app.MapGet("/offline-media/{profile}/{id}/{asset}", (string profile, string id, string asset, LocalProfiles profiles, HttpResponse response) =>
{
 var path = profiles.Session is { } session && session.Id == profile ? session.Downloads.FindAsset(id, asset) : null;
 if (path is null) return Results.NotFound();
 var type = Path.GetExtension(asset) switch
 {
  ".mp4" => "video/mp4", ".jpg" => "image/jpeg", ".png" => "image/png",
  ".gif" => "image/gif", ".webp" => "image/webp", ".avif" => "image/avif", _ => "application/octet-stream"
 };
 response.Headers.CacheControl = "no-store";
 return Results.File(path, type, enableRangeProcessing: true);
});
app.MapGet("/health",()=>Results.Ok(new {status="ok"}));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(AniKatha.UI.Routes).Assembly);
app.Run();
