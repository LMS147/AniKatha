using AniKatha.Core;
using System.Net;
using System.Text;
// Offline integration checks: real SQLite persistence and captured upstream responses.
var folder=Path.Combine(Path.GetTempPath(),"anikatha-check-"+Guid.NewGuid());Directory.CreateDirectory(folder);
var path=Path.Combine(folder,"checks.db3");
var store=new LocalStore(path);
await Task.WhenAll(Enumerable.Range(1,8).Select(i=>store.SaveAsync(new SavedTitle {Id=i,Title=$"Story {i}",Kind=i%2==0?"MANGA":"ANIME",Progress=i})));
var reopened=new LocalStore(path);
Assert((await reopened.GetLibraryAsync()).Count==8,"Library must persist after reopening");
var saved=(await reopened.GetLibraryAsync()).First(x=>x.Id==1);saved.Progress=7;await reopened.SaveAsync(saved);
Assert((await store.GetLibraryAsync()).First(x=>x.Id==1).Progress==7,"Progress must persist");
await store.RemoveAsync(1);Assert((await store.GetLibraryAsync()).Count==7,"Removal must persist");
await store.SetSettingAsync("accent","#ffa3c7");Assert(await reopened.GetSettingAsync("accent")=="#ffa3c7","Theme must persist");
await store.SaveAddonAsync(new(){Url="https://example.com/manifest.json",Name="Example",Version="1.0.0"});
Assert((await reopened.GetAddonsAsync()).Count==1,"Add-on must persist");await store.RemoveAddonAsync("https://example.com/manifest.json");Assert((await reopened.GetAddonsAsync()).Count==0,"Add-on removal must persist");
foreach(var invalid in new[]{"javascript:alert(1)","http://example.com/manifest.json","https://user:password@example.com/manifest.json","https://localhost/manifest.json","https://127.0.0.1/manifest.json","https://example.com/manifest.json?token=secret"}) {
 try {AddonClient.ValidateUrl(invalid);throw new Exception("Unsafe URL accepted: "+invalid);}catch(InvalidOperationException){}
}
var handler=new StubHandler();var api=new AniListClient(new HttpClient(handler));
var result=await api.SearchAsync(new("MANGA","Story",Year:2020));
Assert(result.Items.Count==1&&result.Items[0].Title=="Story"&&result.Items[0].Description==" A & B "&&result.Items[0].Year is null,"Nullable metadata and descriptions must parse safely");
Assert(handler.Body!.Contains("MANGA")&&handler.Body.Contains("Story"),"Search variables must reach upstream");
handler.Response="{\"errors\":[{\"message\":\"temporarily unavailable\"}]}";
try{await api.SearchAsync(new());throw new Exception("GraphQL errors must not look like empty results");}catch(InvalidOperationException){}
handler.Status=HttpStatusCode.TooManyRequests;
try{await api.SearchAsync(new());throw new Exception("Rate limit must surface");}catch(InvalidOperationException){}
Console.WriteLine("PASS: SQLite persistence, progress, settings, add-ons, URL validation, metadata parsing, GraphQL errors, and rate limiting.");
static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
sealed class StubHandler:HttpMessageHandler {
 public string? Body;public HttpStatusCode Status=HttpStatusCode.OK;
 public string Response="""{"data":{"Page":{"pageInfo":{"hasNextPage":false},"media":[{"id":42,"type":"MANGA","title":{"userPreferred":"Story"},"coverImage":{"large":null},"bannerImage":null,"description":"<b>A &amp; B</b>","startDate":{"year":null},"format":"MANGA","averageScore":null,"episodes":null,"chapters":null,"genres":["Drama"]}]}}}""";
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){Body=await request.Content!.ReadAsStringAsync(cancellationToken);return new(Status){Content=new StringContent(Response,Encoding.UTF8,"application/json")};}
}
