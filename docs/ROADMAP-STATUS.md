# AniKatha roadmap

Baseline main 2f1a2e4. Separate worktree preserves codex/android-foreground-check at 45e9820. Read-only reference cmp-rewrite b66f772. MAUI Blazor Hybrid, SQLite. Item 21 disabled and item 37 design-only because both session switches were unspecified. Item 20 requires confirmation of an authorized sync mechanism.

Baseline: node --test scripts/*.test.mjs passed 6/6; python scripts/test_startup_checks.py passed 5/5. After installing SDK 10.0.100, the detached baseline passed the Release preview build and both .NET test projects. An initial combined build reported a failure without diagnostics after restore. Retrying the build with `-m:1` and the test executables with `--no-build` succeeded. Android CI installs the downloadable APK then exercises its WebView through scripts/android_startup.sh and scripts/check_webview.mjs.

| Number | Feature | Status | Existing behavior and gap | Affected files | Acceptance test |
|---|---|---|---|---|---|
| 1 | Anime search | Verified | Per-catalog forms replaced by the Anime submit action | Catalog, ManifestCatalog | Search once with two anime providers |
| 2 | Manga search | Verified | Manga submit routes only to manga catalogs and explains browse-only groups | Catalog, ManifestCatalog | Install three typed sources and inspect requests |
| 3 | Source filter | Verified | All Sources, AniList, Jikan when enabled, and compatible HTTP sources | Catalog | Disable selected provider and verify fallback |
| 4 | Standard filters | Verified | AniList accepts global filters. HTTP applies metadata filters to loaded items. Jikan shows an unsupported-filter notice. | Catalog, AddonResources | Filter mixed catalogs with missing metadata |
| 5 | Required extras | Verified | Declared fields and options are scoped to one catalogue and encoded | AddonResources, ManifestCatalog | Required choice isolates one catalog |
| 6 | Tab state | Verified | Queries, drafts, results, pagination and scroll are scoped per tab | Catalog | Restore scroll, draft, results and offset |
| 7 | Progressive results | Verified | Concurrent results, individual retries, and stale responses isolated | ManifestCatalog | Delay stale query and retry failed page |
| 8 | Rich details | Verified | Optional title metadata on search detail and provider Library routes | ManifestCatalog | Open with and without counts |
| 9 | Title mappings | Verified | Explicit per-unit provider mappings and declared prefix matching | AddonResources | Keep remakes distinct |
| 10 | Unit progress | Verified | Provider unit completion markers and Continue links use the existing precise keys | Resume, LocalStore | Continue specific unit |
| 11 | Source information | Verified | Quality, audio and translation labels with separate missing and failed states | ManifestSources | Show independent success and failure |
| 12 | Preferred source | Verified | Provider preference persisted by title, independent of playback choice | ManifestSources, LocalStore | Fallback retains preference |
| 13 | Recent sources | Verified | In-memory unit cache with refresh and expiry | ManifestSources | Refresh expired link |
| 14 | AniList connection | Implemented but unverified | Profile and Android OAuth wired. Fixture tests passed. Registered client ID and native flow still needed. | AniListClient, Profile | Connect, revoke and disconnect |
| 15 | List import | Verified | Paginated, idempotent two-media import passed against fixtures. | LocalStore, AniListClient | Repeat paginated import |
| 16 | Statuses and scores | Verified | Status and nullable scores round-tripped through fixtures. | Models, AniListClient | Round trip account score scale |
| 17 | Completed counts | Verified | Explicit Library count updates and reset passed. Provider-only unit counts stay local without an AniList mapping. | LocalStore | Partial unit leaves remote count intact |
| 18 | Offline queue | Verified | Edits stayed queued across SQLite reopen and retried after simulated network recovery. | LocalStore | Restart and replay newest edit |
| 19 | Sync conflicts | Verified | Field-level conflict, intentional reset, account isolation and revision acknowledgement checks passed. | LocalStore | Test reset and independent edits |
| 20 | Cross-device resume | Blocked | No authorized AniKatha sync backend found. Local precise positions remain intact. | Resume, LocalStore | Restore between two devices |
| 21 | Settings sync | Optional and pending | Disabled for this run because the session switch was unspecified | Profile | Requires explicit enablement and authorized backend |
| 22 | Playback speed | Verified | Playback speed has an immediate control and saved default. | WatchPreview | Restart restores default |
| 23 | Subtitle language | Implemented but unverified | Provider tracks select a preferred language unless overridden for the episode. | WatchPreview | Absent language and override |
| 24 | Subtitle appearance | Verified | WebView tracks support size, background and placement. YouTube embeds retain their own controls. | WatchPreview, episode-player.mjs | Check native and embedded support |
| 25 | Seek controls | Verified | Configured seek controls clamp and save positions. | episode-player.mjs | Clamp and save near boundaries |
| 26 | Next episode | Verified | Next and autoplay resolve the next unit in provider order. The final episode has no next action. | ManifestCatalog, WatchPreview | Repeated end event advances once |
| 27 | Reading modes | Verified | Vertical, LTR and RTL preserve logical page. | ReadMangaPreview | Keep logical page through mode changes |
| 28 | Reader display | Verified | Fit and zoom are independent of stored position. | ReadMangaPreview | Rotate without progress loss |
| 29 | Jump to page | Verified | Valid page jumps persist. Invalid input gets a range message. | ReadMangaPreview | Reject out-of-range page |
| 30 | Chapter navigation | Verified | Chapter navigation respects provider order and restores the previous page. | ManifestCatalog | Navigate decimal chapters in source order |
| 31 | Reader preferences | Verified | Global defaults and per-title overrides persist in existing settings. | ReadMangaPreview, LocalStore | Clear override |
| 32 | Chapter downloads | Verified | Ordered chapter files commit together after all pages succeed. | ReadMangaPreview | Interrupt and verify incomplete state |
| 33 | Video downloads | Verified | Explicit downloadable MP4 sources resolve by stable stream ID. Native playback is an emulator check. | ManifestSources | Play local file offline |
| 34 | Download queue | Verified | Queue resumes interrupted jobs on reopen and supports failure, cancellation and retries. | LocalStore | Restart and cancel |
| 35 | Offline library | Verified | Both types open from Library against local files and preserve the provider progress key. | Library | Open both types without network |
| 36 | Storage management | Verified | File accounting, path validation and deletion preserve titles and precise progress. | Library | Remove file and retain progress |
| 37 | Plug-in design | Design delivered | Reference package and lifecycle reviewed. A scoped runtime design is documented separately. | PluginRepository.kt (reference) | Review package lifecycle and isolation design |

## Phase 1 gate

Release preview and check projects built with zero warnings and errors. `dotnet run --project tests/AniKatha.Checks -c Release --no-build` and `dotnet run --project tests/AniKatha.UiChecks -c Release --no-build` passed. The fixture drove two anime types, a manga catalog, browse-only discovery, independent retry, stale responses, required parameters, a second page, tab restoration and a disabled source. The earlier player and reader fixture still passes. `node --test scripts/*.test.mjs` passed seven tests including a scroll-position check with a simulated window. `python scripts/test_startup_checks.py` passed five tests. SDK 10.0.100.

The Android emulator, physical Samsung and the APK upgrade gate require a device or the GitHub Actions installed-APK workflow. No device or adb executable was available locally. Actual WebView scroll restoration, live provider results and upgrade remain outstanding for this branch. Phase 1 introduces optional HTTP catalogue extras documented in HTTP-ADDONS.md and stores the selected source in the existing settings table under a key per media type. No SQLite migration is needed for this phase.

## Phase 2 gate

The rendered check opened provider titles from search and Library, verified an explicit mapping to a different episode ID, showed the direct stream while the alternate provider failed, reopened a preferred provider, refreshed cached sources and retained the chapter page after marking it complete. Two unrelated providers with the same local title ID stayed separate. The Node media test verified that a repeated end event saves the existing timestamp and records provider episode completion once. Live playback and upgrade on an installed APK still require the Android workflow or a device. Completion and source preferences use the existing AppSetting table. No SQLite migration. Optional metadata, source mappings, stream details and expiry are documented in HTTP-ADDONS.md.

## Phase 3 gate

The account fixture exercised OAuth callback state validation, two-media pagination and repeat import, decimal and unset scores, a restart of the local store with pending edits, reconnection, concurrent queue revisions, a remote status edit alongside a local progress reset, revocation and disconnect. Both exact positions remained intact. No schema migration. Credential storage on Android uses SecureStorage. Account sync records use scoped keys in the existing settings table.

Live authorization and mutations require a registered public AniList client ID, the matching redirect, and an Android device or emulator signed into a test AniList account. The public .NET tests use simulated GraphQL replies. No authorized service was found for cross-device exact positions or settings. See ANILIST-SYNC.md.

## Phase 4 gate

The Release UI check built with zero warnings and errors. Rendered fixtures exercised autoplay transition to the next episode, last-unit boundaries, decimal chapter order, exact resume, reader jumps, fit and zoom, RTL, vertical page callbacks, reopened title preferences, and clearing an override. JavaScript tests covered speed, bounds, position saves, cue placement, and a duplicate completion event. The app uses the existing settings table and progress keys. Device verification of native HTML video, rendered subtitle appearance, preferred language selection and vertical scrolling remains open. YouTube embeds retain provider-controlled playback and subtitle settings.

## Phase 5 gate

The rendered .NET fixture exercised cancelled and damaged jobs, recovery from an interrupted SQLite queue, duplicate handling, ordered chapter files, native-independent local asset paths, no provider requests when opening offline items, and preserved library records and exact positions after file removal. It simulated lost connectivity with a fixture.

The Android workflow now makes a fresh install, tests the download sampler online, enters airplane mode to load the saved manga page and seek the downloaded video, reinstalls the pre-roadmap APK to seed data, then upgrades in place without clearing that seeded data. The workflow records device and upgrade outcomes when run. The downloads, queue, and preferences use the existing AppSetting table and app-private files. No schema migration. Download availability fields are documented in HTTP-ADDONS.md. Plug-in design is in PROVIDER-PLUGINS-DESIGN.md.
