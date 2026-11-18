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
  await db.CreateTableAsync<ResumeEntry>(); await db.CreateTableAsync<SavedTitle>(); await db.CreateTableAsync<InstalledAddon>(); await db.CreateTableAsync<AppSetting>();
 }
 public async Task RememberAsync(ResumeEntry entry)
 {
  await initialized.Value;
  entry.Opened=DateTime.UtcNow;
  await db.InsertOrReplaceAsync(entry);
 }
 public async Task<List<ResumeEntry>> HistoryAsync()
 {
  await initialized.Value;
  return await db.Table<ResumeEntry>().OrderByDescending(x=>x.Opened).ToListAsync();
 }
 public async Task ForgetAsync(string id)
 {
  await initialized.Value;
  await db.DeleteAsync<ResumeEntry>(id);
 }
 public async Task<List<SavedTitle>> GetLibraryAsync() { await initialized.Value; return await db.Table<SavedTitle>().OrderByDescending(x=>x.UpdatedAt).ToListAsync(); }
 public async Task SaveAsync(SavedTitle title) { await initialized.Value; title.UpdatedAt=DateTime.UtcNow; await db.InsertOrReplaceAsync(title); }
 public async Task SaveProviderTitleAsync(string providerKey,string type,string externalId,string title)
 {
  await initialized.Value;
  await db.RunInTransactionAsync(connection =>
  {
   if(connection.Table<SavedTitle>().Where(x=>x.ProviderKey==providerKey && x.ResourceType==type && x.ExternalId==externalId).Count()>0) return;
   var id = connection.ExecuteScalar<int>("SELECT MIN(COALESCE(MIN(Id),0),0)-1 FROM SavedTitle");
   connection.Insert(new SavedTitle { Id=id, Kind=type=="manga"?"MANGA":"ANIME", Title=title, ProviderKey=providerKey,
    ExternalId=externalId, ResourceType=type, UpdatedAt=DateTime.UtcNow });
  });
 }
 public Task SaveAsync(Media title) => SaveAsync(new SavedTitle { Id=title.Id, Kind=title.Kind, Title=title.Title, Poster=title.Poster });
 public async Task RemoveAsync(int id) { await initialized.Value; await db.DeleteAsync<SavedTitle>(id); }
 public async Task<List<InstalledAddon>> GetAddonsAsync(bool includeDisabled = false)
 {
  await initialized.Value;
  var rows = await db.QueryAsync<InstalledAddon>("SELECT * FROM InstalledAddon ORDER BY COALESCE(SortOrder,0), rowid");
  return includeDisabled ? rows : rows.Where(row => !row.Disabled).ToList();
 }
 public async Task SaveAddonAsync(InstalledAddon addon)
 {
  await initialized.Value;
  await db.RunInTransactionAsync(connection =>
  {
   if (connection.Find<InstalledAddon>(addon.Url) != null)
    throw new InvalidOperationException("This source is already installed. Use Refresh to update it.");
   addon.SortOrder = connection.ExecuteScalar<int>("SELECT COALESCE(MAX(SortOrder),0)+1 FROM InstalledAddon");
   connection.Insert(addon);
  });
 }
 public async Task RefreshAddonAsync(InstalledAddon addon)
 {
  await initialized.Value;
  await db.RunInTransactionAsync(connection =>
  {
   var current = connection.Find<InstalledAddon>(addon.Url) ?? throw new InvalidOperationException("Source was removed.");
   addon.SortOrder = current.SortOrder;
   addon.Disabled = current.Disabled;
   connection.Update(addon);
  });
 }
 public async Task SetAddonDisabledAsync(string url, bool disabled)
 {
  await initialized.Value;
  await db.ExecuteAsync("UPDATE InstalledAddon SET Disabled=? WHERE Url=?",disabled,url);
 }
 public async Task MoveAddonAsync(string url, int direction)
 {
  await initialized.Value;
  await db.RunInTransactionAsync(connection =>
  {
   var rows = connection.Query<InstalledAddon>("SELECT * FROM InstalledAddon ORDER BY COALESCE(SortOrder,0), rowid");
   var index = rows.FindIndex(row => row.Url == url);
   var target = index + Math.Sign(direction);
   if (index < 0 || target < 0 || target >= rows.Count) return;
   (rows[index],rows[target]) = (rows[target],rows[index]);
   for (int i=0; i<rows.Count; i++)
    connection.Execute("UPDATE InstalledAddon SET SortOrder=? WHERE Url=?",i+1,rows[i].Url);
  });
 }
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
 public async Task<int> GetReadingPageAsync(string chapterKey)
 {
  var value = await GetSettingAsync($"reading:{chapterKey}");
  return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var page)
   && page >= 1 ? page : 1;
 }
 public Task SaveReadingPageAsync(string chapterKey, int page)
 {
  ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
  return SetSettingAsync($"reading:{chapterKey}", page.ToString(CultureInfo.InvariantCulture));
 }
}
