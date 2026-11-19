# AniKatha roadmap

Baseline main 2f1a2e4. Separate worktree preserves codex/android-foreground-check at 45e9820. Read-only reference cmp-rewrite b66f772. MAUI Blazor Hybrid, SQLite. Item 21 disabled and item 37 design-only because both session switches were unspecified. Item 20 requires confirmation of an authorized sync mechanism.

Baseline: node --test scripts/*.test.mjs passed 6/6; python scripts/test_startup_checks.py passed 5/5. Local dotnet build could not launch because SDK 10 was absent. Android CI installs the downloadable APK then exercises its WebView through scripts/android_startup.sh and scripts/check_webview.mjs.

| Number | Feature | Status | Existing behavior and gap | Affected files | Acceptance test |
|---|---|---|---|---|---|
| 1 | Anime search | Implemented but unverified | Per-catalog forms replaced by the Anime submit action | Catalog, ManifestCatalog | Search once with two anime providers |
| 2 | Manga search | Implemented but unverified | Manga submit routes only to manga catalogs and explains browse-only groups | Catalog, ManifestCatalog | Install three typed sources and inspect requests |
| 3 | Source filter | Pending | Built-in selector exists; HTTP targeting pending | Catalog | Disable selected provider and verify fallback |
| 4 | Standard filters | Pending | AniList filters exist; HTTP applicability pending | Catalog, AddonResources | Filter mixed catalogs with missing metadata |
| 5 | Required extras | Pending | Only search and skip parsed | AddonResources, ManifestCatalog | Required choice isolates one catalog |
| 6 | Tab state | Pending | State lives in component | Catalog | Restore scroll, draft, results and offset |
| 7 | Progressive results | Pending | Per-group loading exists; tab-level races need tests | ManifestCatalog | Delay stale query and retry failed page |
| 8 | Rich details | Pending | Unit lists exist; optional metadata presentation pending | ManifestCatalog | Open with and without counts |
| 9 | Title mappings | Pending | Provider identities exist; trusted mapping pending | AddonResources | Keep remakes distinct |
| 10 | Unit progress | Pending | Precise resume exists; completion markers pending | Resume, LocalStore | Continue specific unit |
| 11 | Source information | Pending | Grouped sources exist; quality and language presentation pending | ManifestSources | Show independent success and failure |
| 12 | Preferred source | Pending | Manual selection exists; preference pending | ManifestSources, LocalStore | Fallback retains preference |
| 13 | Recent sources | Pending | Request cache pending | ManifestSources | Refresh expired link |
| 14 | AniList connection | Pending | Public metadata client; account authorization pending | AniListClient, Profile | Connect, revoke and disconnect |
| 15 | List import | Pending | Local library; account import pending | LocalStore, AniListClient | Repeat paginated import |
| 16 | Statuses and scores | Pending | Account field mapping pending | Models, AniListClient | Round trip account score scale |
| 17 | Completed counts | Pending | Local progress exists; account updates pending | LocalStore | Partial unit leaves remote count intact |
| 18 | Offline queue | Pending | Durable account queue pending | LocalStore | Restart and replay newest edit |
| 19 | Sync conflicts | Pending | Conflict handling pending | LocalStore | Test reset and independent edits |
| 20 | Cross-device resume | Pending | Local resume exists; authorized backend must be established | Resume, LocalStore | Restore between two devices |
| 21 | Settings sync | Optional and pending | Optional; disabled because no selection was made | Profile | Requires explicit enablement and authorized backend |
| 22 | Playback speed | Pending | Persistent speed control pending | WatchPreview | Restart restores default |
| 23 | Subtitle language | Pending | Manual tracks exist; preferred language pending | WatchPreview | Absent language and override |
| 24 | Subtitle appearance | Pending | Track styling pending | WatchPreview, episode-player.mjs | Check native and embedded support |
| 25 | Seek controls | Pending | Configurable jumps pending | episode-player.mjs | Clamp and save near boundaries |
| 26 | Next episode | Pending | Provider order available; transition pending | ManifestCatalog, WatchPreview | Repeated end event advances once |
| 27 | Reading modes | Pending | Paged reader exists; mode switching pending | ReadMangaPreview | Keep logical page through mode changes |
| 28 | Reader display | Pending | Zoom and fit controls pending | ReadMangaPreview | Rotate without progress loss |
| 29 | Jump to page | Pending | Next page exists; direct jump pending | ReadMangaPreview | Reject out-of-range page |
| 30 | Chapter navigation | Pending | Unit selection exists; adjacent chapter navigation pending | ManifestCatalog | Navigate decimal chapters in source order |
| 31 | Reader preferences | Pending | Title-specific overrides pending | ReadMangaPreview, LocalStore | Clear override |
| 32 | Chapter downloads | Pending | Bundled sample exists; provider download pending | ReadMangaPreview | Interrupt and verify incomplete state |
| 33 | Video downloads | Pending | Stream selection exists; downloadable-file capability pending | ManifestSources | Play local file offline |
| 34 | Download queue | Pending | Persistent queue pending | LocalStore | Restart and cancel |
| 35 | Offline library | Pending | Local library exists; downloaded-item launch pending | Library | Open both types without network |
| 36 | Storage management | Pending | Downloaded-file accounting pending | Library | Remove file and retain progress |
| 37 | Plug-in design | Optional and pending | Separate runtime in reference; design-only chosen to preserve HTTP architecture | PluginRepository.kt (reference) | Review package lifecycle and isolation design |
