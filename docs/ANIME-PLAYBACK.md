# Anime player behavior

The baseline was AniKatha main at `85ceada`. The read-only NuvioMobile comparison used `cmp-rewrite` at `b66f772`.

## Entry points

- Anime and imported or provider Library titles resolve episode sources through `ManifestCatalog` and `ManifestSources`. Continue Watching resolves the saved provider and episode through `Resume`. These paths open `WatchPreview` for direct video. The player stores an exact timestamp under the existing provider episode key.
- Downloads use the existing key and player. Android uses a local Blob for seekable playback in the WebView. A download does not fetch remote subtitle tracks while offline.
- The YouTube add-on opens a dedicated mobile page with its own embedded playback controls and timestamp. Use Library for manual tracking of embedded sources. The AniKatha subtitle and audio controls apply to direct playback.
- The direct player checkpoints before switching to an equivalent source, starting the next episode, or handing off to another app. Equivalent source choices come from the existing provider identity and unit matching logic. Manual completion is available for direct episodes and uses the existing AniList rules.

## Playback and tracks

Speed uses the saved default when a player opens. A manual speed change is immediate. The skip interval is stored separately and seeks are clamped. Autoplay uses the provider episode order and an ended callback is accepted once. Streams that expire can be requested again by stable stream ID, exact URL, or unambiguous name. Retry and source switching retain the saved timestamp.

The subtitle picker shows source labels, languages and roles. Preferences match an exact language before a base language and favor a full track over signs or forced tracks for the same match. An unmatched subtitle preference falls back to Off. An unmatched audio preference leaves the engine's current selection in place. Manual track choices and Off are retained during a player session when providers add tracks later. An individual subtitle request can fail while playback and the previous working track remain available. The app only lists embedded audio and text tracks the runtime exposes.

Text size and background use WebVTT cue styles. The external subtitle renderer can also change placement and shift timing earlier or later in 250 ms steps. Reset offset restores the selected track's original timing. Profile can reset the three appearance defaults. The offset is scoped to the selected external track in the current player. Embedded timing and audio delay require engine access to samples or cues.

## Platform support and verification limits

NuvioMobile's `PlayerEngine.android.kt` selects Media3 or libmpv, enumerates native tracks, and applies engine-specific timing. Its Android PiP manager and now-playing service own activity and audio lifecycle. The external-player contract interprets result data only for supported receivers. Subtitle AutoSync compares add-on cues with embedded timing captured from a Media3 extractor.

AniKatha uses WebView picture-in-picture and media-session hooks only when the browser exposes them. A native Android PiP or notification session, controlled background playback, Media3 or libmpv playback, audio delay, and subtitle autosync need a verified native player integration. AutoSync also needs readable timed embedded cues for the selected video. Generic Android handoff supports HTTPS video links and preserves AniKatha's existing checkpoint. Manual completion is available because a generic receiver does not report trustworthy progress.
