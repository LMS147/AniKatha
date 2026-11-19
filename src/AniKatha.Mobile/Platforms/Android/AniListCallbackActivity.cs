using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace AniKatha.Mobile;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "co.zyma.anikatha", DataHost = "anilist", DataPath = "/callback")]
public sealed class AniListCallbackActivity : WebAuthenticatorCallbackActivity;
