using System.Security.Cryptography;
using System.Text.Json;

namespace AniKatha.Core;

public sealed record LocalProfile(string Id, string Name, string Avatar, bool Locked = false)
{
    public const string DefaultId = "default";
    public static LocalProfile Default { get; } = new(DefaultId, "Story explorer", "");
}

public sealed record ProfileSession(string Id, LocalStore Store, OfflineDownloads Downloads, AniListAccount? AniList = null,
    IYouTubePlayer? YouTube = null);

public interface IProfileSecrets
{
    Task<string?> ReadAsync(string profileId);
    Task WriteAsync(string profileId, string value);
    void Remove(string profileId);
}

public sealed class LocalProfiles(string root, string legacyDatabase, AddonClient addons, IProfileSecrets? secrets = null,
    Func<string, AniListAccount?>? createAniList = null, Func<LocalStore, IYouTubePlayer?>? createYouTube = null)
{
    private readonly LocalStore registry = new(Path.Combine(root, "profiles.db3"));
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, ProfileSession> sessions = new(StringComparer.Ordinal);
    private bool loaded;
    public event Action? Changed;
    public bool SupportsPin => secrets is not null;
    public IReadOnlyList<LocalProfile> All { get; private set; } = [];
    public LocalProfile Active { get; private set; } = LocalProfile.Default;
    public ProfileSession? Session { get; private set; }

    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (loaded) return;
            var saved = await registry.GetSettingAsync("profiles-v1");
            var state = saved is null ? new([LocalProfile.Default], LocalProfile.DefaultId) :
                JsonSerializer.Deserialize<Registry>(saved) ?? throw new InvalidDataException("Could not read profiles.");
            RecoverRemovals(state.Profiles);
            await ApplyAsync(state, unlock: false);
            loaded = true;
        }
        finally { gate.Release(); }
    }

    public async Task<LocalProfile> SaveAsync(string? id, string name, string avatar)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 32 || name.Any(char.IsControl))
            throw new InvalidOperationException("Enter a name between 1 and 32 characters.");
        if (avatar is not ("" or "star" or "moon" or "flower" or "bolt"))
            throw new InvalidOperationException("Choose an available avatar.");
        await InitializeAsync();
        await gate.WaitAsync();
        try
        {
            var existing = id is null ? null : Find(id);
            var profile = new LocalProfile(existing?.Id ?? Guid.NewGuid().ToString("N"), name, avatar, existing?.Locked ?? false);
            if (existing?.Locked == true && (Session is null || Active.Id != id))
                throw new InvalidOperationException("Switch to and unlock this profile before editing it.");
            var updated = existing is null ? All.Append(profile) : All.Select(item => item.Id == id ? profile : item);
            await ApplyAsync(new(updated.ToArray(), Active.Id), Session is not null);
            return profile;
        }
        finally { gate.Release(); }
    }

    public async Task ActivateAsync(string id, string? pin = null, bool recover = false)
    {
        await InitializeAsync();
        await gate.WaitAsync();
        try
        {
            var target = Find(id);
            if (Session is not null && Active.Id == id && !recover) return;
            if (target.Locked)
            {
                await CheckSecretAsync(id, pin, recover);
                if (recover) await ReplaceProtectionAsync(target, null, Active.Id, Session is not null);
            }
            await ApplyAsync(new(All.ToArray(), id), unlock: true);
        }
        finally { gate.Release(); }
    }

    public async Task<string?> SetPinAsync(string? pin, string? currentPin)
    {
        await InitializeAsync();
        await gate.WaitAsync();
        try
        {
            if (secrets is null || Session is null) throw new InvalidOperationException("Profile protection is unavailable.");
            if (Active.Locked) await CheckSecretAsync(Active.Id, currentPin, recover: false);
            if (pin is not null && (pin.Length is < 4 or > 8 || !pin.All(char.IsAsciiDigit)))
                throw new InvalidOperationException("Use a PIN with 4–8 digits.");
            var recovery = pin is null ? null : Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var protection = pin is null ? null : new Verifier(Hash(pin), Hash(recovery!));
            await ReplaceProtectionAsync(Active, protection, Active.Id, unlock: true);
            return recovery;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> RemoveAsync(string id)
    {
        await InitializeAsync();
        await gate.WaitAsync();
        try
        {
            var target = Find(id);
            if (All.Count <= 1 || Active.Id == id)
                throw new InvalidOperationException("Switch to another profile before removing this one.");
            var original = id == LocalProfile.DefaultId;
            if (target.Locked) throw new InvalidOperationException("Unlock this profile and remove its PIN first.");
            var directory = ProfileDirectory(id);
            // Archive atomically before hiding the registry entry, so a failed registry write can restore the files.
            var removed = directory + ".removed";
            if (sessions.TryGetValue(id, out var session))
            {
                await session.Downloads.RetireAsync();
                if (session.AniList is not null) await session.AniList.DisconnectAsync();
                await session.Store.CloseAsync();
                sessions.Remove(id);
            }
            else if (createAniList?.Invoke(id) is { } account) await account.DisconnectAsync();

            if (!original && Directory.Exists(directory)) Directory.Move(directory, removed);
            try { await ApplyAsync(new(All.Where(item => item.Id != id).ToArray(), Active.Id), Session is not null); }
            catch
            {
                if (Directory.Exists(removed)) Directory.Move(removed, directory);
                throw;
            }
            secrets?.Remove(id);
            if (original) return ClearOriginalFiles();
            try { if (Directory.Exists(removed)) Directory.Delete(removed, recursive: true); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            return true;
        }
        finally { gate.Release(); }
    }

    private void RecoverRemovals(LocalProfile[] current)
    {
        if (!current.Any(profile => profile.Id == LocalProfile.DefaultId)) ClearOriginalFiles();
        var directory = Path.Combine(root, "profiles");
        if (!Directory.Exists(directory)) return;
        foreach (var removed in Directory.EnumerateDirectories(directory, "*.removed"))
        {
            var id = Path.GetFileNameWithoutExtension(removed);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            if (current.Any(profile => profile.Id == id) && !Directory.Exists(ProfileDirectory(id)))
                Directory.Move(removed, ProfileDirectory(id));
            else
            {
                try { Directory.Delete(removed, recursive: true); }
                catch (IOException) { /* Retry cleanup next time the app starts. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private bool ClearOriginalFiles()
    {
        try
        {
            var database = Path.Combine(root, legacyDatabase);
            foreach (var file in new[] { database, database + "-wal", database + "-shm", database + "-journal" })
                File.Delete(file);
            var offline = Path.Combine(root, "offline");
            if (Directory.Exists(offline)) Directory.Delete(offline, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private LocalProfile Find(string id) => All.FirstOrDefault(item => item.Id == id) ??
        throw new InvalidOperationException("This profile is unavailable.");

    private string ProfileDirectory(string id) => Path.Combine(root, "profiles", id);

    private async Task ApplyAsync(Registry state, bool unlock)
    {
        var profile = state.Profiles.Single(item => item.Id == state.ActiveId);
        var session = profile.Locked && !unlock ? null : GetSession(profile.Id);
        await registry.SetSettingAsync("profiles-v1", JsonSerializer.Serialize(state));
        All = state.Profiles;
        Active = profile;
        Session = session;
        Changed?.Invoke();
        if (session is not null) _ = StartDownloadsAsync(session);
    }

    private ProfileSession GetSession(string id)
    {
        if (sessions.TryGetValue(id, out var session)) return session;
        var original = id == LocalProfile.DefaultId;
        var folder = original ? root : ProfileDirectory(id);
        var store = new LocalStore(original ? Path.Combine(root, legacyDatabase) : Path.Combine(folder, "data.db3"));
        session = new(id, store, new OfflineDownloads(store, addons, Path.Combine(folder, "offline")),
            createAniList?.Invoke(id), createYouTube?.Invoke(store));
        sessions.Add(id, session);
        return session;
    }

    private static async Task StartDownloadsAsync(ProfileSession session)
    {
        try { await session.Downloads.StartAsync(); }
        catch { /* Downloads shows persisted job status when opened. */ }
    }

    private async Task ReplaceProtectionAsync(LocalProfile profile, Verifier? next, string activeId, bool unlock)
    {
        if (secrets is null) throw new InvalidOperationException("Secure profile storage is unavailable.");
        var previous = await secrets.ReadAsync(profile.Id);
        if (next is null) secrets.Remove(profile.Id);
        else await secrets.WriteAsync(profile.Id, JsonSerializer.Serialize(next));
        try
        {
            await ApplyAsync(new(All.Select(item => item.Id == profile.Id ? item with { Locked = next is not null } : item).ToArray(),
                activeId), unlock);
        }
        catch
        {
            if (previous is null) secrets.Remove(profile.Id);
            else await secrets.WriteAsync(profile.Id, previous);
            throw;
        }
    }

    private async Task CheckSecretAsync(string id, string? value, bool recover)
    {
        var raw = secrets is null ? null : await secrets.ReadAsync(id);
        if (raw is null || JsonSerializer.Deserialize<Verifier>(raw) is not { } verifier)
            throw new InvalidOperationException("The secure verifier is unavailable. Use another profile or restore this device's app data.");
        if (verifier.RetryAfter > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Too many attempts. Wait 30 seconds and try again.");
        var candidate = recover ? value?.Trim().ToUpperInvariant() : value;
        if (candidate is { Length: > 0 and <= 64 } && Matches(candidate, recover ? verifier.Recovery : verifier.Pin))
        {
            if (verifier.Failures > 0)
                await secrets!.WriteAsync(id, JsonSerializer.Serialize(verifier with { Failures = 0, RetryAfter = default }));
            return;
        }

        var failures = verifier.Failures + 1;
        await secrets!.WriteAsync(id, JsonSerializer.Serialize(verifier with
        {
            Failures = failures >= 5 ? 0 : failures,
            RetryAfter = failures >= 5 ? DateTimeOffset.UtcNow.AddSeconds(30) : default
        }));
        throw new InvalidOperationException(recover ? "That recovery code is incorrect." : "That PIN is incorrect.");
    }

    private static string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(salt) + ":" + Convert.ToHexString(Derive(value, salt));
    }

    private static bool Matches(string value, string encoded)
    {
        var parts = encoded.Split(':');
        return parts.Length == 2 && CryptographicOperations.FixedTimeEquals(
            Derive(value, Convert.FromHexString(parts[0])), Convert.FromHexString(parts[1]));
    }

    private static byte[] Derive(string value, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(value, salt, 210_000, HashAlgorithmName.SHA256, 32);

    private sealed record Registry(LocalProfile[] Profiles, string ActiveId);
    private sealed record Verifier(string Pin, string Recovery, int Failures = 0, DateTimeOffset RetryAfter = default);
}
