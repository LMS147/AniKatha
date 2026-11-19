using System.Reflection;
using System.Text.Json;
using AniKatha.Core;
using Microsoft.Maui.Authentication;

namespace AniKatha.Mobile;

internal sealed class AniListAuthorization(string profileId) : IAniListAuthorization
{
    private string StorageKey => profileId == LocalProfile.DefaultId
        ? "anilist-credential" : "anilist-credential-" + profileId;
    public string? ClientId { get; } = typeof(AniListAuthorization).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(item => item.Key == "AniListClientId")?.Value;
    public Uri Callback { get; } = new("co.zyma.anikatha://anilist/callback");

    public async Task<Uri> AuthorizeAsync(Uri request, CancellationToken token)
    {
        var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = request,
            CallbackUrl = Callback
        }, token);
        return result.CallbackUri ?? throw new AniListAuthorizationException();
    }

    public async Task<AniListCredential?> ReadAsync()
    {
        var json = await SecureStorage.Default.GetAsync(StorageKey);
        return json is null ? null : JsonSerializer.Deserialize<AniListCredential>(json);
    }

    public Task WriteAsync(AniListCredential credential) =>
        SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(credential));

    public void Clear() => SecureStorage.Default.Remove(StorageKey);
}
