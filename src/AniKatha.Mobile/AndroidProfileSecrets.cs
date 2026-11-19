using AniKatha.Core;

namespace AniKatha.Mobile;

internal sealed class AndroidProfileSecrets : IProfileSecrets
{
    private static string Key(string id) => "anikatha-profile-lock-" + id;
    public Task<string?> ReadAsync(string profileId) => SecureStorage.Default.GetAsync(Key(profileId));
    public Task WriteAsync(string profileId, string value) => SecureStorage.Default.SetAsync(Key(profileId), value);
    public void Remove(string profileId) => SecureStorage.Default.Remove(Key(profileId));
}
