using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AniKatha.Core;

public sealed record CatalogExtra(string Name, bool Required, IReadOnlyList<string> Options);
public sealed record AddonCatalog(string Type,string Id,string Name,bool Search,bool Skip)
{
 public IReadOnlyList<CatalogExtra> Extras { get; init; } = [];
 public string Format { get; init; } = "";
 public string Ranking { get; init; } = "";
}

public sealed class AddonResources(InstalledAddon addon)
{
 public InstalledAddon Addon => addon;
 public string ProviderKey { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(addon.Url)));
 readonly JsonElement manifest = addon.ManifestJson is null ? default : JsonSerializer.Deserialize<JsonElement>(addon.ManifestJson);
 public static IEnumerable<JsonElement> Items(JsonElement node,string key) =>
  node.ValueKind==JsonValueKind.Object && node.TryGetProperty(key,out var array) && array.ValueKind==JsonValueKind.Array ? array.EnumerateArray().ToArray() : [];
 public static string Field(JsonElement node,string key) =>
  node.ValueKind==JsonValueKind.Object && node.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString()! : "";
 static bool Contains(JsonElement node,string key,string value) =>
  Items(node,key).Any(item=>item.ValueKind==JsonValueKind.String && item.GetString()==value);
 public string[] DisplayCapabilities()
 {
  var labels = new List<string>();
  var anime = new[] { "anime", "series" };
  var jikan = Addon.CatalogueProvider == JikanCatalogue.Provider;
  var youtube = Addon.YouTubeEpisodesJson is not null;
  var catalogues = Catalogs("ANIME").Concat(Catalogs("MANGA")).ToList();
  var resourceNames = new[] { "catalog", "meta", "stream", "subtitles", "chapters", "pages" };
  bool Offers(string resource, string type) => MatchingRules(resource, type).Any();
  var streams = youtube || anime.Any(type => Offers("stream", type));
  var pages = Offers("pages", "manga");
  if (jikan || youtube || anime.Any(type => resourceNames.Any(resource => Offers(resource, type)))) labels.Add("Anime");
  if (jikan || resourceNames.Any(resource => Offers(resource, "manga"))) labels.Add("Manga");
  if (jikan || catalogues.Count > 0) labels.Add("Catalog");
  if (jikan || catalogues.Any(item => item.Search)) labels.Add("Search");
  if (streams) labels.Add(youtube ? "YouTube playback" : "Playback");
  if (pages) labels.Add("Chapter pages");
  if (!streams && !pages && labels.Count > 0) labels.Add("Metadata only");
  return labels.Count > 0 ? labels.ToArray() : ["Refresh to load capabilities"];
 }

 public bool Supports(string resource,string type,string id)
 {
  return MatchingRules(resource,type).Any(prefixes=>prefixes.Count==0 || MatchesPrefix(prefixes,id));
 }
 public bool HasPrefix(string resource,string type,string id) =>
  MatchingRules(resource,type).Any(prefixes=>MatchesPrefix(prefixes,id));
 static bool MatchesPrefix(List<string> prefixes,string id) => prefixes.Any(prefix=>id.StartsWith(prefix,StringComparison.Ordinal));
 IEnumerable<List<string>> MatchingRules(string resource,string type)
 {
  foreach(var rule in Items(manifest,"resources"))
  {
   var simple=rule.ValueKind==JsonValueKind.String;
   if ((simple?rule.GetString():Field(rule,"name"))!=resource) continue;
   var scope=Items(rule,"types").Any()?rule:manifest;
   if (!Contains(scope,"types",type)) continue;
   var prefixes=Items(rule,"idPrefixes").Any()?rule:manifest;
   yield return Items(prefixes,"idPrefixes").Where(x=>x.ValueKind==JsonValueKind.String && x.GetString()!.Length>0)
    .Select(x=>x.GetString()!).ToList();
  }
 }
 public IEnumerable<AddonCatalog> Catalogs(string kind)
 {
  var seen = new HashSet<(string Type, string Id)>();
  foreach(var c in Items(manifest,"catalogs"))
  {
   var type=Field(c,"type"); var id=Field(c,"id");
   if ((kind=="MANGA" ? type!="manga" : type is not ("anime" or "series")) || id=="" || !MatchingRules("catalog",type).Any()) continue;
   if (!seen.Add((type, id))) continue;
   var extras = Items(c, "extra")
    .Select(x => new CatalogExtra(Field(x, "name"),
     x.ValueKind == JsonValueKind.Object && x.TryGetProperty("isRequired", out var required) && required.ValueKind == JsonValueKind.True,
     Items(x, "options").Where(option => option.ValueKind == JsonValueKind.String)
      .Select(option => option.GetString()!).Where(option => option.Length > 0).Distinct().ToArray()))
    .Where(x => x.Name.Length > 0).DistinctBy(x => x.Name).ToList();
   yield return new(type, id, Field(c,"name") is {Length:>0} name ? name : id,
    extras.Any(x => x.Name == "search"), extras.Any(x => x.Name == "skip")) { Extras = extras, Format = Field(c, "format"), Ranking = Field(c, "ranking") };
  }
 }
 public IEnumerable<(AddonResources Provider, string UnitId)> ResolveUnitProviders(
  string type, JsonElement unit, IEnumerable<AddonResources> providers)
 {
  var resource = type == "manga" ? "pages" : "stream";
  var unitId = Field(unit, "id");
  foreach (var provider in providers)
  {
   if (provider.ProviderKey == ProviderKey)
   {
    if (provider.Supports(resource, type, unitId)) yield return (provider, unitId);
    continue;
   }
   if (provider.HasPrefix(resource, type, unitId))
   {
    yield return (provider, unitId);
    continue;
   }
   var mappings = Items(unit, "sourceMappings")
    .Where(mapping => Field(mapping, "providerKey") == provider.ProviderKey)
    .Select(mapping => Field(mapping, "unitId"))
    .Where(id => provider.HasPrefix(resource, type, id)).Distinct(StringComparer.Ordinal).Take(2).ToList();
   if (mappings.Count == 1) yield return (provider, mappings[0]);
  }
 }
 public Uri Resource(string resource,string type,string id,string? extra=null)
 {
  if (resource is not ("catalog" or "meta" or "stream" or "chapters" or "pages" or "subtitles") || type is not ("anime" or "series" or "manga") || string.IsNullOrWhiteSpace(id) || id is "." or "..")
   throw new InvalidOperationException("Invalid resource request.");
  var manifestUri=AddonClient.ValidateUrl(addon.Url);
  var path=manifestUri.GetLeftPart(UriPartial.Path)[..^"manifest.json".Length];
  return new Uri(path+resource+"/"+type+"/"+Uri.EscapeDataString(id)+(extra==null?"":"/"+extra)+".json"+manifestUri.Query);
 }
 public static bool MediaUrl(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) &&
  uri.Scheme=="https" && uri.UserInfo.Length==0;
}
