using System.Globalization;
using System.Text.Json;
using SQLite;

namespace AniKatha.Core;

public sealed record LibraryBackup(int Version, SavedTitle[] Library, BackupHistory[] History)
{
    public const int CurrentVersion = 1;
    public const int MaxBytes = 16 * 1024 * 1024;
}

public sealed record BackupHistory(ResumeEntry Entry, double? Seconds, int? Page, bool Completed, double? UnitLength);
public sealed record BackupMerge(int Anime, int Manga, int History, int Checkpoints, int Skipped);

public interface IBackupExport
{
    Task ShareAsync(string json);
}

public sealed partial class LocalStore
{
    public async Task<string> ExportLibraryBackupAsync()
    {
        await ReadyAsync();
        string json = "";
        await db.RunInTransactionAsync(connection =>
        {
            var library = connection.Table<SavedTitle>().ToArray().Where(PortableTitle).ToArray();
            foreach (var item in library) item.Poster = PortablePoster(item.Poster);
            var history = connection.Table<ResumeEntry>().ToArray().Where(PortableHistory).Select(entry =>
            {
                entry.Poster = PortablePoster(entry.Poster);
                var key = ResumeEntry.Key(entry.Origin, entry.Type, entry.TitleId, entry.UnitId, entry.Source);
                var seconds = ReadBackupNumber(connection, $"playback:{key}");
                var page = ReadBackupNumber(connection, $"reading:{key}");
                return new BackupHistory(entry, seconds, page is >= 1 and <= int.MaxValue ? (int)page.Value : null,
                    connection.Find<AppSetting>($"completed:{key}")?.Value == "true",
                    ReadBackupNumber(connection, $"unit-length:{key}"));
            }).ToArray();
            json = JsonSerializer.Serialize(new LibraryBackup(LibraryBackup.CurrentVersion, library, history));
        });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > LibraryBackup.MaxBytes)
            throw new InvalidDataException("This Library backup exceeds the 16 MB format limit.");
        return json;
    }

    public static async Task<LibraryBackup> ReadLibraryBackupAsync(Stream file)
    {
        if (file.CanSeek && file.Length > LibraryBackup.MaxBytes)
            throw new InvalidDataException("Choose an AniKatha JSON file under 16 MB.");
        var backup = await JsonSerializer.DeserializeAsync<LibraryBackup>(file);
        ValidateBackup(backup);
        return backup!;
    }

    public async Task<BackupMerge> MergeLibraryBackupAsync(LibraryBackup backup, bool apply = false)
    {
        ValidateBackup(backup);
        await ReadyAsync();
        int anime = 0, manga = 0, history = 0, checkpoints = 0, skipped = 0;
        await db.RunInTransactionAsync(connection =>
        {
            var saved = connection.Table<SavedTitle>().ToArray();
            var catalogIds = saved.Select(item => item.Id).ToHashSet();
            var providerIds = saved.Where(item => item.ProviderKey is not null)
                .Select(item => (item.ProviderKey, item.ResourceType, item.ExternalId)).ToHashSet();
            var nextProviderId = Math.Min(0, catalogIds.DefaultIfEmpty().Min());
            foreach (var item in backup.Library)
            {
                var isNew = item.ProviderKey is null ? catalogIds.Add(item.Id) :
                    providerIds.Add((item.ProviderKey, item.ResourceType, item.ExternalId));
                if (!isNew) { skipped++; continue; }
                if (item.Kind == "ANIME") anime++; else manga++;
                if (apply)
                {
                    connection.Insert(new SavedTitle
                    {
                        Id = item.ProviderKey is null ? item.Id : checked(--nextProviderId), Kind = item.Kind,
                        Title = item.Title, Poster = PortablePoster(item.Poster), IsFavourite = item.IsFavourite,
                        ProviderKey = item.ProviderKey, ExternalId = item.ExternalId, ResourceType = item.ResourceType,
                        Status = item.Status, Progress = item.Progress, UpdatedAt = item.UpdatedAt
                    });
                }
            }

            foreach (var unit in backup.History)
            {
                var entry = unit.Entry;
                if (connection.Find<ResumeEntry>(entry.Id) is null)
                {
                    history++;
                    if (apply) connection.Insert(new ResumeEntry
                    {
                        Id = entry.Id, Origin = entry.Origin, Source = entry.Source, Type = entry.Type, TitleId = entry.TitleId,
                        UnitId = entry.UnitId, Title = entry.Title, Label = entry.Label, Poster = PortablePoster(entry.Poster),
                        Opened = entry.Opened
                    });
                }
                else skipped++;
                var key = ResumeEntry.Key(entry.Origin, entry.Type, entry.TitleId, entry.UnitId, entry.Source);
                AddCheckpoint($"playback:{key}", unit.Seconds?.ToString("R", CultureInfo.InvariantCulture));
                AddCheckpoint($"reading:{key}", unit.Page?.ToString(CultureInfo.InvariantCulture));
                AddCheckpoint($"completed:{key}", unit.Completed ? "true" : null);
                AddCheckpoint($"unit-length:{key}", unit.UnitLength?.ToString("R", CultureInfo.InvariantCulture));
            }

            void AddCheckpoint(string key, string? value)
            {
                if (value is null) return;
                if (connection.Find<AppSetting>(key) is not null) { skipped++; return; }
                checkpoints++;
                if (apply) connection.Insert(new AppSetting { Key = key, Value = value });
            }
        });
        return new(anime, manga, history, checkpoints, skipped);
    }

    private static void ValidateBackup(LibraryBackup? value)
    {
        if (value is not { Version: LibraryBackup.CurrentVersion, Library: not null, History: not null })
            throw new InvalidDataException("Choose a version 1 AniKatha JSON backup.");
        if (value.Library.Any(item => item is null || !PortableTitle(item) || item.Progress < 0 ||
                item.Status is not ("PLANNING" or "CURRENT" or "COMPLETED" or "PAUSED" or "DROPPED" or "REPEATING")) ||
            value.History.Any(unit => unit?.Entry is null || !PortableHistory(unit.Entry) || unit.Seconds < 0 ||
                unit.Seconds is double seconds && !double.IsFinite(seconds) || unit.Page < 1 ||
                unit.UnitLength is double length && (!double.IsFinite(length) || length <= 0)) ||
            value.History.Select(unit => unit.Entry.Id).Distinct(StringComparer.Ordinal).Count() != value.History.Length)
            throw new InvalidDataException("The backup contains invalid Library or history entries.");
    }

    private static bool PortableTitle(SavedTitle item) => item.Kind is ("ANIME" or "MANGA") &&
        item.Title is { Length: > 0 and <= 1000 } && (item.ProviderKey is null ? item.Id > 0 :
            IsProvider(item.ProviderKey) && IsResource(item.ResourceType) && IsPortableId(item.ExternalId));

    private static bool PortableHistory(ResumeEntry entry)
    {
        if (!IsProvider(entry.Origin) || !IsProvider(entry.Source) || !IsResource(entry.Type) ||
            !IsPortableId(entry.TitleId) || !IsPortableId(entry.UnitId) ||
            entry.Title is not { Length: > 0 and <= 1000 } || entry.Label is not { Length: <= 1000 }) return false;
        var key = ResumeEntry.Key(entry.Origin, entry.Type, entry.TitleId, entry.UnitId, entry.Source);
        return entry.Id == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
    }

    private static bool IsProvider(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool IsResource(string? value) => value is "anime" or "series" or "manga";
    private static bool IsPortableId(string? value) => value is { Length: > 0 and <= 512 } && !value.Any(char.IsControl) &&
        !value.Contains("://", StringComparison.Ordinal) && !value.Contains('%') && !value.Contains('?') && !value.Contains('#');
    private static string? PortablePoster(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0 ? value : null;

    private static double? ReadBackupNumber(SQLiteConnection connection, string key) =>
        double.TryParse(connection.Find<AppSetting>(key)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) && value >= 0 ? value : null;
}
