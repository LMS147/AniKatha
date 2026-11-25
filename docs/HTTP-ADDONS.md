# HTTP add-ons

AniKatha accepts an HTTPS base address or manifest.json. Configured paths and query strings travel to the same provider's resource endpoints. Existing manifests can omit anikathaProtocolVersion. The supported explicit version is the integer 1. The manifest's version field remains the provider's own release version. Refresh legacy installations to populate the stored manifest and capability display.

A catalogue declaration lists type, id, name, and optional extra entries named search and skip. Anime accepts anime and series. Manga accepts manga. The supported HTTP subset uses GET and JSON. Resource names may be strings or objects with name, types and idPrefixes. A second provider resolving another catalogue's unit must declare a matching ID prefix. Source order determines display order.

| Request relative to the manifest directory | Response |
|---|---|
| catalog/{type}/{catalogId}.json | metas array with id, type and name |
| catalog/{type}/{catalogId}/search={value}&skip={offset}.json | Same shape. Include only requested extras. Skip counts raw results. |
| meta/{type}/{titleId}.json | meta.videos array with each episode's id and title |
| stream/{type}/{episodeId}.json | streams array with name and direct HTTPS url |
| chapters/manga/{titleId}.json | chapters array with each chapter's id and title |
| pages/manga/{chapterId}.json | pages array of ordered HTTPS image URLs |

Chapter and page resources are AniKatha extensions. Samples are in examples/addons/story-sampler. Jikan supplies catalogue metadata. YouTube video IDs use the existing YouTube adapter. Progress keys for HTTP content include catalogue provider, media type, title, unit and source. Reinstalling the same provider URL restores access to its saved positions.

A stream can include a subtitles array of objects with lang and url. A provider declaring a subtitles resource can also return that array from subtitles/{type}/{episodeId}.json. The player accepts HTTPS WebVTT and SRT tracks. Local SQLite history retains provider, title, unit and source identities when a provider is disabled or removed.

Catalogues may declare provider-specific extras as objects in the same `extra` array. An entry has a `name`, optional Boolean `isRequired`, and optional string array `options`. AniKatha displays one value per field and sends only explicitly declared, selected fields. Each name and value is percent-encoded in the extras path segment. A required field delays its own catalogue until supplied. HTTP discovery filters for genre, year, season and format can also narrow the loaded page using the optional item fields `genres` (string array), `year`, `season`, and `format`.

Optional title metadata can appear on a catalogue item, in the `meta` response object, or in an optional `meta` object alongside the `chapters` array. Detail metadata takes precedence over catalogue items when present. Recognized fields include `name`, `poster` (HTTPS), `description` or `synopsis`, `genres`, `status`, `episodeCount` and `chapterCount`. Omit an unknown count. The returned episode or chapter array can be incomplete.

Different providers may identify the same episode or chapter differently. A unit in a `videos` or `chapters` array may supply `sourceMappings`, an array of objects with a target `providerKey` and that provider's `unitId`. The provider key is the uppercase SHA-256 hex digest of the target's normalized manifest URL. This is an explicit mapping for the selected title's unit. AniKatha requires the target provider to declare a matching resource type and ID prefix and only accepts an unambiguous mapping for that provider. The target's own ID is used in the resource request. The catalogue title retains its original provider and ID. AniKatha never matches by display title or position in the list.

Stream entries may supply `quality`, `audioLanguage`, and `subtitles` for the source picker. Manga page responses may supply a `language` alongside the ordered pages. A 404 for a stream or page resource represents an unavailable unit. Timeouts and other request failures show a retry action.

Successful unit sources can be reused in memory for up to two minutes. A JSON response or stream object may supply an earlier `expiresAt` as an ISO 8601 timestamp with offset. Refresh sources bypasses the stored result. Failed video playback invalidates the cached result for the unit.

Anime downloads require an explicit `downloadable: true`, a stable nonempty stream `id`, and a direct HTTPS MP4 file. The app recognizes MP4 through `mediaType: "video/mp4"` or a URL ending in `.mp4` before its query. It verifies the file signature during download. On retry, it resolves the same stream ID again so expired URLs can be replaced. Manga downloads use the existing ordered page resource and support JPEG, PNG, WebP, GIF and AVIF. Chapter files are published together after every page succeeds.

The Story Sampler includes a small download compatibility clip made from Big Buck Bunny © 2008 Blender Foundation, licensed CC BY 3.0. The fixture is a resized, eight-second excerpt with audio removed and H.264 Baseline encoding. Its separate stream ID lets the Android check use a device-friendly MP4 while retaining the original trailer.

## Optional AniList progress mapping

An add-on can map a title and individual units to AniList. The existing resource URLs and manifest version stay the same. Put a positive integer `anilistId` in the originating title's `meta` object and a positive integer `anilistProgress` on each mapped episode or chapter. For example, using illustrative IDs:

```json
{"meta":{"anilistId":12345,"videos":[{"id":"provider-episode-3","title":"Episode 3","anilistProgress":3}]}}
```

```json
{"meta":{"anilistId":67890},"chapters":[{"id":"provider-chapter-6","title":"Chapter 6","anilistProgress":6}]}
```

The add-on author should verify each title and unit against the AniList release, including the season or edition. Omit the fields when the match is uncertain or when a special has no equivalent completed count. The originating catalogue supplies the mapping even when another provider resolves the stream or pages. Existing add-ons without the fields still support playback, reading, and local resume.

For **Library → Find Sources**, a provider can advertise the `anilist:` ID prefix on the appropriate `meta` or `chapters` resource, then accept IDs such as `anilist:12345` on that resource. AniKatha also checks searchable catalogs for candidates found by name or an explicit ID in the catalogue response. In each case the title's detail response must confirm the exact AniList ID before the source is offered. Provider IDs remain the IDs used for subsequent unit requests.

For external anime subtitles, send HTTPS WebVTT or SubRip URLs using the existing subtitle resource or inline stream tracks. An entry can include `label` and `role` alongside `lang` and `url`. Use roles such as `full`, `signs-and-songs`, `forced`, `captions` or `sdh` when the source identifies that role. AniKatha keeps existing requests to that URL, including its query parameters. The Android **Open in external player** action sends the selected stream URL to a video app. It does not attach subtitle URLs or custom extras to the generic Android intent.

### Optional Browse Manga shelf metadata

AniKatha reads the following optional fields on catalogs of type `manga`:

- `format`: the catalog's documented scope, such as `Manga`, `Manhwa`, or `Light Novel`. An explicit `format` on a result takes precedence over the catalog's format. A `Novel` is not treated as a `Light Novel`.
- `ranking`: `popularity` for catalogs already sorted from most to least popular or `score` for catalogs containing only titles with community ratings, sorted from highest to lowest. The order and scope must apply across pages.

A `metas` entry can supply `popularity` (a nonnegative integer, with larger numbers meaning more popular) and `communityScore` (a community rating on a 1–100 scale). A catalog without a declared ranking can qualify individual entries through these fields. AniKatha sorts those entries within the catalog and breaks ties by title ID. Send a personal rating through the Library, never as `communityScore`. Title text, catalog names, and language do not classify or rank results. Unclassified titles can still appear in provider browsing and supported search. AniKatha does not combine rankings from different catalogs into a single rank.

The discovery page makes one initial request per browseable catalog and reuses it across shelves. It does not guess required provider-specific filter values. A catalog supporting `skip` should use the number of raw entries returned so far as its offset, including entries that do not qualify for a shelf.
