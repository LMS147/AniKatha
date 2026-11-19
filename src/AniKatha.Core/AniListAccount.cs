using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace AniKatha.Core;

public sealed record AniListCredential(string Token, DateTimeOffset Expires, int ViewerId, string? Name = null, string? ScoreFormat = null);
public sealed record AniListViewer(int Id, string Name, string ScoreFormat);

public interface IAniListAuthorization
{
    string? ClientId { get; }
    Uri Callback { get; }
    Task<Uri> AuthorizeAsync(Uri request, CancellationToken token);
    Task<AniListCredential?> ReadAsync();
    Task WriteAsync(AniListCredential credential);
    void Clear();
}

public sealed partial class AniListAccount(HttpClient http, IAniListAuthorization authorization)
{
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private CancellationTokenSource session = new();
    public bool CanConnect => int.TryParse(authorization.ClientId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;

    public async Task<AniListViewer?> StoredViewerAsync()
    {
        var credential = await ReadValidCredentialAsync();
        return credential is { Name: not null, ScoreFormat: not null } ? new(credential.ViewerId, credential.Name, credential.ScoreFormat) : null;
    }

    public async Task<AniListViewer?> CurrentAsync(CancellationToken token = default)
    {
        var stored = await authorization.ReadAsync();
        if (stored is null) return null;
        return await WithCredentialAsync(async (current, ct) =>
        {
            var viewer = await ViewerAsync(current, ct);
            if (viewer.Id != current.ViewerId) throw new AniListAuthorizationException();
            await UpdateStoredViewerAsync(current, viewer, ct);
            return viewer;
        }, token);
    }

    private async Task UpdateStoredViewerAsync(AniListCredential credential, AniListViewer viewer, CancellationToken token)
    {
        if (credential.Name == viewer.Name && credential.ScoreFormat == viewer.ScoreFormat) return;
        await sessionGate.WaitAsync(token);
        try
        {
            var stored = await authorization.ReadAsync();
            token.ThrowIfCancellationRequested();
            if (stored?.Token == credential.Token)
                await authorization.WriteAsync(credential with { Name = viewer.Name, ScoreFormat = viewer.ScoreFormat });
        }
        finally { sessionGate.Release(); }
    }

    public async Task<AniListViewer> ConnectAsync(CancellationToken token = default)
    {
        if (!CanConnect) throw new InvalidOperationException("AniList sign-in is unavailable for this build.");
        var active = session;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, active.Token);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var url = new Uri("https://anilist.co/api/v2/oauth/authorize?response_type=token&client_id=" +
            Uri.EscapeDataString(authorization.ClientId!) + "&redirect_uri=" +
            Uri.EscapeDataString(authorization.Callback.AbsoluteUri) + "&state=" + state);
        var reply = await authorization.AuthorizeAsync(url, request.Token);
        var credential = ParseCallback(reply, authorization.Callback, state, DateTimeOffset.UtcNow);
        var viewer = await ViewerAsync(credential, request.Token);

        await sessionGate.WaitAsync(request.Token);
        try
        {
            request.Token.ThrowIfCancellationRequested();
            await authorization.WriteAsync(credential with { ViewerId = viewer.Id, Name = viewer.Name, ScoreFormat = viewer.ScoreFormat });
        }
        finally { sessionGate.Release(); }
        return viewer;
    }

    public async Task DisconnectAsync()
    {
        var previous = Volatile.Read(ref session);
        previous.Cancel();
        await sessionGate.WaitAsync();
        try
        {
            authorization.Clear();
            Interlocked.CompareExchange(ref session, new CancellationTokenSource(), previous);
        }
        finally { sessionGate.Release(); }
    }

    private async Task<AniListCredential?> ReadValidCredentialAsync()
    {
        var credential = await authorization.ReadAsync();
        if (credential is null || credential.Expires <= DateTimeOffset.UtcNow) return null;
        return credential;
    }

    private async Task<T> WithCredentialAsync<T>(
        Func<AniListCredential, CancellationToken, Task<T>> action, CancellationToken token)
    {
        var active = session;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, active.Token);
        var credential = await ReadValidCredentialAsync() ?? throw new AniListAuthorizationException();
        request.Token.ThrowIfCancellationRequested();
        var result = await action(credential, request.Token);
        request.Token.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<AniListViewer> ViewerAsync(AniListCredential credential, CancellationToken token)
    {
        var result = await SendAsync(credential, "query { Viewer { id name mediaListOptions { scoreFormat } } }", new { }, token);
        if (!result.TryGetProperty("Viewer", out var viewer) || viewer.ValueKind != JsonValueKind.Object)
            throw new AniListAuthorizationException();
        return new(viewer.GetProperty("id").GetInt32(), viewer.GetProperty("name").GetString()!,
            AddonResources.Field(viewer.GetProperty("mediaListOptions"), "scoreFormat"));
    }

    private async Task<JsonElement> SendAsync(AniListCredential credential, string query, object variables, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AniListClient.Endpoint);
        request.Content = JsonContent.Create(new { query, variables });
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new AniListAuthorizationException();
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out _) || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("AniList could not complete this account request.");
        return data.Clone();
    }

    internal static AniListCredential ParseCallback(Uri uri, Uri callback, string state, DateTimeOffset now)
    {
        if (!string.Equals(uri.GetLeftPart(UriPartial.Path), callback.GetLeftPart(UriPartial.Path), StringComparison.Ordinal))
            throw new AniListAuthorizationException();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || !values.TryAdd(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1])))
                throw new AniListAuthorizationException();
        }
        var returnedState = values.GetValueOrDefault("state");
        var token = values.GetValueOrDefault("access_token");
        if (returnedState != state || string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) ||
            !int.TryParse(values.GetValueOrDefault("expires_in"), NumberStyles.None, CultureInfo.InvariantCulture, out var expires) || expires <= 0)
            throw new AniListAuthorizationException();
        return new(token, now.AddSeconds(expires), 0);
    }
}

public sealed class AniListAuthorizationException() : InvalidOperationException("AniList authorization has expired or was revoked. Connect again.");
