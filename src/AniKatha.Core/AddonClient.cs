using System.Net;
using System.Text.Json;
namespace AniKatha.Core;
// Manifest metadata only. Remote JavaScript is never evaluated in the application WebView.
public sealed class AddonClient(HttpClient http)
{
 public static Uri ValidateUrl(string input)
 {
  if (!Uri.TryCreate(input.Trim(),UriKind.Absolute,out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !uri.AbsolutePath.EndsWith("/manifest.json",StringComparison.OrdinalIgnoreCase) || uri.IsLoopback || uri.HostNameType != UriHostNameType.Dns || uri.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException("Use a public HTTPS URL ending in /manifest.json, without credentials or query parameters.");
  return uri;
 }
 public async Task<InstalledAddon> InspectAsync(string input, CancellationToken token = default)
 {
  var uri=ValidateUrl(input);
  var addresses=await Dns.GetHostAddressesAsync(uri.Host,token);
  if (addresses.Length==0 || addresses.Any(IsPrivate)) throw new InvalidOperationException("Add-ons must use a public internet address.");
  using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
  response.EnsureSuccessStatusCode();
  // Limit the actual body, not just the optional Content-Length header.
  await using var stream=await response.Content.ReadAsStreamAsync(token);
  using var buffer=new MemoryStream();
  var bytes=new byte[8192]; int count;
  while ((count=await stream.ReadAsync(bytes,token))>0) { if(buffer.Length+count>262144) throw new InvalidOperationException("Manifest is too large."); await buffer.WriteAsync(bytes.AsMemory(0,count),token); }
  using var doc=JsonDocument.Parse(buffer.ToArray()); var root=doc.RootElement;
  string Required(string key) => root.TryGetProperty(key,out var p) && p.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()) ? p.GetString()! : throw new InvalidOperationException($"Manifest is missing {key}.");
  _=Required("id");
  if (!root.TryGetProperty("resources",out var resources) || resources.ValueKind!=JsonValueKind.Array || !root.TryGetProperty("types",out var types) || types.ValueKind!=JsonValueKind.Array)
   throw new InvalidOperationException("Manifest must declare resources and types.");
  return new() { Url=uri.AbsoluteUri, Name=Required("name"), Version=Required("version"), Description=root.TryGetProperty("description",out var description)&&description.ValueKind==JsonValueKind.String ? description.GetString()! : "" };
 }
 static bool IsPrivate(IPAddress address)
 {
  if(address.IsIPv4MappedToIPv6) address=address.MapToIPv4();
  if(IPAddress.IsLoopback(address)||address.IsIPv6LinkLocal||address.IsIPv6SiteLocal||address.IsIPv6Multicast) return true;
  var b=address.GetAddressBytes();
  return b.Length==4 ? b[0] is 0 or 10 or 127 || b[0]>=224 || (b[0]==169&&b[1]==254) || (b[0]==172&&b[1]>=16&&b[1]<=31) || (b[0]==192&&b[1]==168) || (b[0]==100&&b[1]>=64&&b[1]<=127) : (b[0]&0xfe)==0xfc || address.Equals(IPAddress.IPv6Any);
 }
}
