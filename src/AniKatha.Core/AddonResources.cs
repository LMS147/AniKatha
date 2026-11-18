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
  if (!Contains(manifest,"types",type)) return false;
  return Items(manifest,"resources").Any(rule =>
  {
   var simple = rule.ValueKind==JsonValueKind.String;
   if ((simple ? rule.GetString() : Field(rule,"name"))!=resource) return false;
   if (!simple && rule.TryGetProperty("types",out _) && !Contains(rule,"types",type)) return false;
   var scope = !simple && rule.TryGetProperty("idPrefixes",out _) ? rule : manifest;
   return !scope.TryGetProperty("idPrefixes",out _) || Items(scope,"idPrefixes").Any(x=>x.ValueKind==JsonValueKind.String && id.StartsWith(x.GetString()!,StringComparison.Ordinal));
  });
 }
 public bool HasPrefix(string resource,string type,string id) =>
  Items(manifest,"resources").Any(rule =>
  {
   bool simple=rule.ValueKind==JsonValueKind.String;
   if ((simple?rule.GetString():Field(rule,"name"))!=resource) return false;
   if (!simple && rule.TryGetProperty("types",out _) && !Contains(rule,"types",type)) return false;
   var scope=!simple && rule.TryGetProperty("idPrefixes",out _)?rule:manifest;
   return Items(scope,"idPrefixes").Any(x=>x.ValueKind==JsonValueKind.String && x.GetString() is { Length:>0 } prefix && id.StartsWith(prefix,StringComparison.Ordinal));
  });
 public IEnumerable<AddonCatalog> Catalogs(string kind)
 {
  foreach(var c in Items(manifest,"catalogs"))
  {
   var type=Field(c,"type"); var id=Field(c,"id");
   if ((kind=="MANGA" ? type!="manga" : type is not ("anime" or "series")) || id=="" || !Contains(manifest,"types",type)) continue;
   var extras=Items(c,"extra").Select(x=>Field(x,"name")).ToList();
   yield return new(type,id,Field(c,"name"),extras.Contains("search"),extras.Contains("skip"));
  }
 }
 public Uri Resource(string resource,string type,string id,string? extra=null)
 {
  if (resource is not ("catalog" or "meta" or "stream" or "chapters" or "pages") || type is not ("anime" or "series" or "manga") || string.IsNullOrWhiteSpace(id) || id is "." or "..")
   throw new InvalidOperationException("Invalid resource request.");
  var manifestUri=AddonClient.ValidateUrl(addon.Url);
  var path=manifestUri.GetLeftPart(UriPartial.Path)[..^"manifest.json".Length];
  return new Uri(path+resource+"/"+type+"/"+Uri.EscapeDataString(id)+(extra==null?"":"/"+extra)+".json"+manifestUri.Query);
 }
 public static bool MediaUrl(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) &&
  uri.Scheme=="https" && uri.UserInfo.Length==0;
}
