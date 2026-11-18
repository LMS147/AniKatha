using System.Globalization;
using SQLite;
namespace AniKatha.Core;
public sealed class LocalStore
{
 readonly SQLiteAsyncConnection db;
 readonly Lazy<Task> initialized;
 public LocalStore(string path)
 {
  SQLitePCL.Batteries_V2.Init();
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  db = new(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
  initialized = new(InitializeAsync);
 }
 async Task InitializeAsync() {
  await db.CreateTableAsync<SavedTitle>(); await db.CreateTableAsync<InstalledAddon>(); await db.CreateTableAsync<AppSetting>();
 }
 public async Task<List<SavedTitle>> GetLibraryAsync() { await initialized.Value; return await db.Table<SavedTitle>().OrderByDescending(x=>x.UpdatedAt).ToListAsync(); }
 public async Task SaveAsync(SavedTitle title) { await initialized.Value; title.UpdatedAt=DateTime.UtcNow; await db.InsertOrReplaceAsync(title); }
 public Task SaveAsync(Media title) => SaveAsync(new SavedTitle { Id=title.Id, Kind=title.Kind, Title=title.Title, Poster=title.Poster });
 public async Task RemoveAsync(int id) { await initialized.Value; await db.DeleteAsync<SavedTitle>(id); }
 public async Task<List<InstalledAddon>> GetAddonsAsync() { await initialized.Value; return await db.Table<InstalledAddon>().ToListAsync(); }
 public async Task SaveAddonAsync(InstalledAddon addon) { await initialized.Value; await db.InsertOrReplaceAsync(addon); }
 public async Task RemoveAddonAsync(string url) { await initialized.Value; await db.DeleteAsync<InstalledAddon>(url); }
 public async Task<string?> GetSettingAsync(string key) { await initialized.Value; return (await db.FindAsync<AppSetting>(key))?.Value; }
 public async Task SetSettingAsync(string key,string value) { await initialized.Value; await db.InsertOrReplaceAsync(new AppSetting { Key=key, Value=value }); }
 public async Task<double> GetPlaybackPositionAsync(string episodeKey)
 {
  var value = await GetSettingAsync($"playback:{episodeKey}");
  return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
   && double.IsFinite(seconds) && seconds >= 0 ? seconds : 0;
 }
 public Task SavePlaybackPositionAsync(string episodeKey, double seconds)
 {
  if (!double.IsFinite(seconds) || seconds < 0)
   throw new ArgumentOutOfRangeException(nameof(seconds));
  return SetSettingAsync($"playback:{episodeKey}", seconds.ToString("R", CultureInfo.InvariantCulture));
 }
}
