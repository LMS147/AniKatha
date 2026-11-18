# Architecture and development stages

## Current boundary

MAUI hosts the Razor UI in BlazorWebView. Pages call the shared AniList client and SQLite store through dependency injection. AniList provides title metadata; SQLite stores user-selected titles and settings, not a bulk catalogue mirror. Saved episode/chapter counts currently remain local and never imply an upstream sync.

Only Android is packaged now. The browser preview reuses the UI for development; its database is local to the development host. No cloud account system is present.

## Content sources

Keep metadata and content access independent. An AniList media ID is not a source episode/chapter ID. Future providers must implement search, title lookup, explicit ID mapping, and episode/chapter resolution. Ambiguous mappings require user selection rather than silently playing the wrong season.

URL-based add-ons need a protocol client for catalog/meta/stream/subtitles. Manga needs its own versioned chapter/page contract. Installed Jikan manifests enable the built-in Jikan catalogue adapter. Other manifests can be saved, but generic add-on resources are not executed. JavaScript plugin repositories are a distinct capability, not automatically compatible with URL add-ons. Never evaluate provider JavaScript inside the UI WebView or pass it AniList credentials.

Manifest inspection rejects non-HTTPS addresses, credentials, query/fragment tokens, IP literals, and common private DNS results, and disables redirects. It bounds response bodies to 256 KiB. This is a desktop/mobile client baseline, not a complete server-side SSRF boundary: DNS rebinding and further reserved address ranges require additional enforcement before exposing it as a hosted multi-user API.

The Big Buck Bunny sample streams a fixed HTTPS MP4 in the app WebView. Its playback position uses an episode-specific SQLite key, separate from library progress. The player saves periodically and on pause, seek, or exit. Completing the trailer clears its resume position.

## Next milestones

1. AniList OAuth through system browser/deep link with state validation; use platform secure storage for access tokens. Choose the supported client flow after verifying current AniList documentation; never embed a client secret. Handle expiry, disconnection, rate limits, queued updates and conflict resolution.
2. A tested provider for one playable episode and one readable chapter using supported, permitted access. Demonstrate correct AniList ID mapping. Add a bounded URL add-on resource client and contract fixtures.
3. Extend the sample player to provider episodes and build the manga reader. Save chapter/page locally and separately update completed-unit counts on AniList. Test rotation, lifecycle/resume, subtitle/audio selection, right-to-left and scrolling reading, and offline failures on device.
4. Downloads and notifications only after provider capabilities and mobile background restrictions are established.
5. Optional cloud sync and watch parties require separately designed accounts/backend; they are not provided by SQLite or AniList general storage.

## UX

Navigation is Home / Anime / Manga / Library / Profile. Anime and Manga have separate search and discovery filters. Anime and Manga are separate routes that pass their media type to a shared catalogue component. The source selector offers AniList and installed Jikan catalogues. If Jikan is unreachable on the first page, the app tries the same search on AniList before showing the bundled starter list. Manga uses publication year rather than anime broadcast season.

UI text is rendered as text, never raw upstream HTML. Artwork is loaded from AniList-provided remote URLs. Non-adult filtering reduces exposure but is not a full content moderation guarantee.

## Validation

The offline checks use a real temporary SQLite database and captured HTTP responses to verify reopening persistence, concurrent initialisation, progress edits, settings, add-on install/removal, unsafe manifest URLs, nullable AniList fields, GraphQL errors and rate limiting. CI separately compiles Razor UI and the MAUI Android shell.

An Android build alone cannot establish playback support or validate a device experience. Run the APK on a physical Android device before calling a release ready.

## Android standalone install regression (0.1.1)

The original Debug artifact did not explicitly disable fast deployment. Downloaded APKs must carry their managed assemblies, so `EmbedAssembliesIntoApk` is now true. App page resolution is deferred until `CreateWindow` instead of constructing the page before the Application constructor runs. SQLite service construction is lazy. The Android manifest explicitly selects the generated app icon.

CI inspects the APK for managed assemblies, its Blazor host page, and phone/emulator architectures, then installs the same APK on an API 35 emulator without IDE deployment. It verifies that the app stays alive, remains foreground, has no package crash entry, and exposes navigation after the first request timeout window. Foreground parsing supports Android 15 topResumedActivity and older resumed-activity labels; it does not accept historical/background package mentions. Navigation is inspected through the Debug WebView DevTools DOM because UIAutomator can return an empty text hierarchy even when the app visibly renders. Startup logs and a screenshot are uploaded even on failure. This checks startup on an emulator; the user's Samsung crash cannot be conclusively matched without its logcat trace.
