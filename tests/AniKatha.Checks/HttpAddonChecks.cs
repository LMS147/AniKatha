using System.Net;
using AniKatha.Core;

internal static class HttpAddonChecks
{
 public static async Task RunAsync()
 {
  using var http=new HttpClient(new Responses());
  var client=new AddonClient(http,(_,_)=>Task.FromResult(new[]{IPAddress.Parse("192.0.2.1")}));
  foreach(var kind in new[]{"anime","manga","both"})
  {
   var addon=await client.InspectAsync($"https://fixture.example/{kind}?config=value");
   var source=new AddonResources(addon);
   Check(source.Catalogs("ANIME").Any()==(kind!="manga"),"Anime routing");
   Check(source.Catalogs("MANGA").Any()==(kind!="anime"),"Manga routing");
   var type=kind=="manga"?"manga":"anime";
   var uri=source.Resource("catalog",type,"catalog/id","search=a%26b&skip=2");
   Check(uri.AbsoluteUri.Contains("catalog%2Fid/search=a%26b&skip=2.json?config=value"),"Configured request and escaping");
   var result=await client.FetchJsonAsync(uri,default);
   Check(AddonResources.Items(result,"metas").Single().GetProperty("id").GetString()=="title:1","Catalogue response");
   Check(source.Supports("stream",type,"unit:2") && !source.Supports("stream",type,"title:1"),"Unit prefix filtering");
  }
  foreach(var invalid in new[]{"malformed","unsupported","missing","unavailable"})
  {
   try { await client.InspectAsync($"https://fixture.example/{invalid}"); throw new Exception("Accepted "+invalid); }
   catch(InvalidOperationException) {}
  }
  Console.WriteLine("PASS: HTTP add-on install, invalid responses, type routing, query preservation, resource IDs and prefixes.");
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 sealed class Responses : HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
  {
   var path=request.RequestUri!.AbsolutePath;
   var scope=path.Split('/')[1];
   string[] types=scope=="both"?["anime","manga"]:[scope];
   var data=System.Text.Json.JsonSerializer.Serialize(new {
    id="fixture",version="1",name="fixture",types, resources=new object[]{"catalog",new{name="stream",idPrefixes=new[]{"unit:"}}},
    catalogs=types.Select(type=>new {type,id="stories",extra=new[]{new{name="search"},new{name="skip"}}})
   });
   var response=new HttpResponseMessage(HttpStatusCode.OK);
   if(path.Contains("/catalog/"))data="""{"metas":[{"id":"title:1"}]}""";
   if(scope=="malformed")data="[";
   if(scope=="missing")data="{}";
   if(scope=="unsupported")data=data.Insert(1,"\"anikathaProtocolVersion\":2,");
   if(scope=="unavailable")response.StatusCode=HttpStatusCode.ServiceUnavailable;
   response.Content=new StringContent(data);
   return Task.FromResult(response);
  }
 }
}
