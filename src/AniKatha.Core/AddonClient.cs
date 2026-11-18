using System.Net;
using System.Net.Sockets;
using System.Text.Json;
namespace AniKatha.Core;
// Manifest inspection and validation before installation.
public sealed class AddonClient(HttpClient http)
{
 public static Uri ValidateUrl(string input)
 {
  var address = input.Trim();
  if (address.StartsWith("stremio://", StringComparison.OrdinalIgnoreCase))
   address = "https://" + address["stremio://".Length..];

  if (!Uri.TryCreate(address,UriKind.Absolute,out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !uri.AbsolutePath.EndsWith("/manifest.json",StringComparison.OrdinalIgnoreCase) || uri.IsLoopback || uri.HostNameType != UriHostNameType.Dns || uri.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException("Use a public HTTPS URL ending in /manifest.json, without credentials or query parameters.");
  if (uri.IsDefaultPort && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
      && uri.Segments.Length >= 6 && uri.Segments[3] == "blob/")
  {
   var segments = uri.Segments;
   return new Uri("https://raw.githubusercontent.com" +
    string.Concat(segments.Take(3)) + string.Concat(segments.Skip(4)));
  }
  return uri;
 }
 public async Task<InstalledAddon> InspectAsync(string input, CancellationToken token = default)
 {
  var uri = ValidateUrl(input);
  using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
  deadline.CancelAfter(TimeSpan.FromSeconds(20));
  try
  {
   var addresses = await Dns.GetHostAddressesAsync(uri.Host, deadline.Token);
   if (addresses.Length == 0 || addresses.Any(IsPrivate))
    throw new InvalidOperationException("Add-ons must use a public internet address.");

   using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
   if (response.StatusCode == HttpStatusCode.NotFound)
    throw new InvalidOperationException("Manifest not found (HTTP 404). Check the URL and make sure the file is public.");
   if ((int)response.StatusCode is >= 300 and < 400)
    throw new InvalidOperationException("This URL redirects. Paste the final HTTPS manifest URL.");
   if (!response.IsSuccessStatusCode)
    throw new InvalidOperationException($"The server returned HTTP {(int)response.StatusCode}. Check that the manifest is publicly accessible.");

   await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
   using var buffer = new MemoryStream();
   var bytes = new byte[8192];
   int count;
   while ((count = await stream.ReadAsync(bytes, deadline.Token)) > 0)
   {
    if (buffer.Length + count > 262144)
     throw new InvalidOperationException("Manifest is too large.");
    buffer.Write(bytes, 0, count);
   }

   // JSON exported by some editors starts with a UTF-8 byte order mark.
   var payload = buffer.ToArray().AsMemory();
   if (payload.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) payload = payload[3..];
   using var doc = JsonDocument.Parse(payload);
   return ReadManifest(uri, doc.RootElement);
  }
  catch (OperationCanceledException) when (!token.IsCancellationRequested)
  {
   throw new InvalidOperationException("The manifest request timed out. Check your connection and try again.");
  }
  catch (SocketException ex)
  {
   throw new InvalidOperationException("Could not resolve the add-on server. Check the hostname and your connection.", ex);
  }
  catch (HttpRequestException ex)
  {
   throw new InvalidOperationException("Could not connect to the add-on server over HTTPS. Check your connection and the server certificate.", ex);
  }
  catch (JsonException ex)
  {
   throw new InvalidOperationException("The server did not return valid JSON. Paste a direct link to the manifest file.", ex);
  }
 }

 private static InstalledAddon ReadManifest(Uri uri, JsonElement root)
 {
  if (root.ValueKind != JsonValueKind.Object)
   throw new InvalidOperationException("The manifest must be a JSON object.");
  string Required(string key) => root.TryGetProperty(key,out var p) && p.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()) ? p.GetString()! : throw new InvalidOperationException($"Manifest is missing {key}.");
  _=Required("id");
  if (!root.TryGetProperty("resources",out var resources) || resources.ValueKind!=JsonValueKind.Array || !root.TryGetProperty("types",out var types) || types.ValueKind!=JsonValueKind.Array)
   throw new InvalidOperationException("Manifest must declare resources and types.");
  string? episodes = null;
  if (root.TryGetProperty("anikathaYoutubeEpisodes", out var entries))
  {
   if (!HasValue(resources, "stream") || !HasValue(types, "anime"))
    throw new InvalidOperationException("YouTube episode add-ons must declare stream and anime.");
   _ = YouTubeEpisode.Read(entries);
   episodes = entries.GetRawText();
  }
  return new() { Url=uri.AbsoluteUri, YouTubeEpisodesJson=episodes, Name=Required("name"), Version=Required("version"),
   CatalogueProvider = root.TryGetProperty("anikathaProvider", out var provider) && provider.ValueKind == JsonValueKind.String && provider.GetString() == JikanCatalogue.Provider ? JikanCatalogue.Provider : null,
   Description=root.TryGetProperty("description",out var description)&&description.ValueKind==JsonValueKind.String ? description.GetString()! : "" };
 }
 static bool HasValue(JsonElement array, string expected) =>
  array.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == expected);

 static bool IsPrivate(IPAddress address)
 {
  if(address.IsIPv4MappedToIPv6) address=address.MapToIPv4();
  if(IPAddress.IsLoopback(address)||address.IsIPv6LinkLocal||address.IsIPv6SiteLocal||address.IsIPv6Multicast) return true;
  var b=address.GetAddressBytes();
  return b.Length==4 ? b[0] is 0 or 10 or 127 || b[0]>=224 || (b[0]==169&&b[1]==254) || (b[0]==172&&b[1]>=16&&b[1]<=31) || (b[0]==192&&b[1]==168) || (b[0]==100&&b[1]>=64&&b[1]<=127) : (b[0]&0xfe)==0xfc || address.Equals(IPAddress.IPv6Any);
 }
}
