# Provider plug-in design

Roadmap item 37 uses Design Only. Reference revision `b66f7725ca08f0455e5097797253a2d7ac432f84` on `cmp-rewrite`.

## Reference behavior

In `composeApp/src/fullCommonMain/kotlin/com/nuvio/app/features/plugins`, `PluginManifestParser.kt` reads repository manifests containing executable scraper entries. `PluginRepository.kt` fetches platform-compatible entry files, scopes stored code by profile and repository URL, and preserves user enablement on refresh when the manifest still permits it. It handles install, duplicate detection, refresh, enablement and removal independently of HTTP add-ons.

`runtime/PluginRuntime.kt`, `runtime/js/JsBindings.kt` and `runtime/host/HostApiRegistry.kt` create a QuickJS execution context, wrap a CommonJS-style module, bind a set of host functions, call the exported or global `getStreams` function and translate returned objects. The reference entry point uses `getStreams(tmdbId, mediaType, season, episode)`. It also supports plug-in settings and bounds concurrent executions and execution time.

## AniKatha interface

| Area | Proposed behavior |
|---|---|
| Package | Separate versioned repository manifest declaring package ID, release version, media types, resource capabilities, entry file, content hash and permitted HTTPS origins. Preserve a validated release until an update completes. |
| Anime | A `resolveEpisode` entry point taking an immutable request with media type, provider title and unit IDs, and optional verified external mappings. Use the existing stream and subtitle result contracts after validation. |
| Manga | A `resolveChapter` entry point with title and chapter IDs. Return ordered HTTPS pages using the existing reader contract. Avoid code execution in the reader. |
| Runtime | Run script in a disposable engine on a restricted process boundary. Set finite runtime, memory, response size, request count and concurrency. Cancel an in-flight call on disable, removal or navigation. A failure affects only that provider. |
| Host API | Expose a validated HTTPS fetch broker limited to declared approved origins. Resolve and check destinations, including redirects, and bound response size. No filesystem, process spawning, native APIs, WebView DOM, local database or access to accounts and other providers' data. Settings are namespaced per package. |
| Lifecycle | Validate package and consented permissions before activation. Stage updates atomically. Retain the previous working package after a failed refresh. Disable cancels active jobs and excludes results. Removal clears cached code and settings while keeping AniKatha history, library and progress. |

AniKatha can adopt the lifecycle and grouped source results. Packages written for Nuvio would require an explicit adapter and a matching host capability set. The TMDB-based entry point cannot safely infer corresponding anime episodes or manga chapters. Compatibility is a separate testable decision.

## Acceptance if implementation is enabled

Use controlled fixtures to test install, duplicate detection, disable while running, invalid update rollback, removal, repeated cancellation, an infinite loop hitting its resource limit, rejected private-network requests, and a failed provider alongside a successful provider. Confirm that an existing HTTP add-on still works and that plugin code cannot read another profile's credentials. Verify the runtime on the actual Android build where it would run.
