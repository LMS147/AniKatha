using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AniKatha.Core;

public sealed record AddonCatalog(string Type,string Id,string Name,bool Search,bool Skip);
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
  foreach(var c in Items(manifest,"catalogs"))
  {
   var type=Field(c,"type"); var id=Field(c,"id");
   if ((kind=="MANGA" ? type!="manga" : type is not ("anime" or "series")) || id=="" || !MatchingRules("catalog",type).Any()) continue;
   var extras=Items(c,"extra").Select(x=>Field(x,"name")).ToList();
   yield return new(type,id,Field(c,"name") is {Length:>0} name ? name : id,extras.Contains("search"),extras.Contains("skip"));
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
