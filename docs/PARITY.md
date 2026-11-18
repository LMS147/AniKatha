# Reference and verification checklist

Reference: LMS147/NuvioMobile, cmp-rewrite, b66f7725ca08f0455e5097797253a2d7ac432f84. Kotlin filenames below are under composeApp/src/commonMain/kotlin/com/nuvio/app/features. Target baseline: LMS147/AniKatha main at 6219f90, with the earlier YouTube adapter c96e7cb carried forward. The preview compiled and the SQLite checks passed before edits.

| Feature | Reference | AniKatha implementation | Status |
|---|---|---|---|
| Manage installed HTTP providers | addons/AddonsScreen.kt, AddonRepository.kt | Profile, AddonClient, LocalStore. Validate, reject duplicate, enable, disable, refresh, remove, reorder. Cloud profile sync still open. | Partial |
| Manifest transport and capabilities | addons/AddonManifestParser.kt, AddonModels.kt, AddonTransportUrls.kt | AddonResources and HTTP-ADDONS.md. Resource ID prefix filtering, configuration paths and queries, bounded HTTPS requests. | Partial |
| Browse and search | catalog/CatalogRepository.kt | Catalog and ManifestCatalog. Independent typed catalogue sections with declared search/skip, cancellation and deduplication. Cross-catalogue search and required extra handling still open. | Partial |
| Title and unit lookup | details/MetaDetailsRepository.kt | ManifestCatalog loads videos or chapters using the catalogue title ID. Metadata enrichment and library integration for HTTP items still open. | Partial |
| Progressive source groups | streams/StreamsRepository.kt, StreamSearchSessions.kt | ManifestSources fetches each provider independently. Each group shows loading, empty, error/retry or results. Cross-screen session caching still open. | Partial |
| Anime playback and reading | player/PlayerScreen.kt | WatchPreview and ReadMangaPreview accept resolved URLs and provider-scoped progress keys. Ordered page reading is an AniKatha extension. Subtitle selection and advanced controls still open. | Partial |
| Discovery and resume | home/HomeScreen.kt | Home provides trending and saved lists. A provider-aware Continue Watching/Reading shelf is outstanding. | Partial |
| Library and favourites | library/LibraryRepository.kt | LocalStore and Library track AniList titles and manual unit counts. Provider-aware favourites and history still outstanding. | Partial |
| Downloads | downloads/DownloadsRepository.kt | A bundled chapter is available offline. User-initiated downloads, storage quotas and cancellation remain open. | Missing |
| Settings | settings/SettingsScreen.kt | Profile stores local preferences. Full playback/reader settings remain open. | Partial |
| JavaScript plugins | plugins/PluginRepository.kt | A separate execution system from HTTP manifests. No plugin runtime in AniKatha. | Missing |
| Video subtitles for still manga pages | player/PlayerScreen.kt | Still pages have no timed audio or subtitle track. | Inapplicable |
