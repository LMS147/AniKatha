# Local profiles, appearance, and sources

The baseline was AniKatha `main` at `8943777`. The read-only NuvioMobile comparison used `cmp-rewrite` at `b66f772`. Nuvio's shared `ProfileRepository`, `ProfileSelectionScreen`, `ProfileEditScreen` and `AddonRepository` group profile selection, PIN gating and source ownership. Its Android storage implementations use app-private preferences. AniKatha keeps a small profile registry alongside its existing SQLite-backed app data and uses MAUI SecureStorage for Android PIN verifiers and AniList credentials.

## Ownership and upgrade

- Opening this update creates the original **Story explorer** profile. It keeps the existing `anikatha.db3`, `offline` folder, and `anilist-credential` secure storage key in place. The versioned registry is stored in `profiles.db3` and can reopen the original data without copying rows, renaming files, or changing existing resume keys.
- New profiles own their own SQLite database, source installations and order, browsing settings, download queue and files, Library, history, playback and reader settings, resume positions, and AniList outbox. A separately named secure credential belongs to each linked profile. The profile's stable ID survives a rename.
- Switching rebuilds the routed UI and drops the old catalog and source caches. The old player and reader are disposed as on page navigation. Download and account operations that already started keep their original session and may finish against that profile. Reconnect starts account sync for the currently open profile. The media file provider checks the profile in the offline URL against the open profile.
- Switch away from a profile and remove its PIN before deleting it. At least one profile must remain. Deleting the original profile also deletes the data from the earlier installation. The confirmation explains which data, files, and pending updates are removed. The next startup recovers or finishes an interrupted removal.
- Android's optional local PIN uses salted PBKDF2 verifiers in MAUI SecureStorage with a stored retry delay. It gates access when opening a protected profile or restarting AniKatha. The random recovery code is shown at setup or change. A valid recovery code removes the PIN. If the verifier cannot be read, choose another profile. The PIN is an app access gate. It does not encrypt the profile's SQLite or download files separately from Android's app storage.

## Screens

**Profile** links to the switcher, Appearance, Sources, AniList, and Playback & Reader. The hub labels an AniList connection separately from the local identity. Appearance previews edits on a sample card. Save applies the selected theme, accent and poster density to the whole app. Cancel restores the previous settings, and Reset places the defaults in the preview.

**Sources** validates the manifest and shows a capability preview before installation. Each source supports enablement, refresh, reordering and confirmed removal. A source replacement URL is treated as a new provider, which preserves existing history identities. Removing a source leaves its existing Library, progress and download entries available. Source management does not display the full saved manifest URLs. Query configuration is sent only through the existing add-on transport.

## Optional extensions

| Extension | Reference behavior | Dependency before exposing it |
|---|---|---|
| Media servers | Nuvio's `ServerRepository` uses a profile-scoped connection and credential store. Jellyfin and Emby adapters supply authenticated library and playback requests. | A supported AniKatha adapter with separated credentials, a test server with licensed sample anime, and end-to-end verification of catalog, episode resolution, playback and resume. A connection form alone is insufficient. |
| JavaScript plug-ins | The full Nuvio build loads repository manifests and runs QuickJS through an explicit host API. It resolves streams using a TMDB-based interface. | A sandboxed Android runtime with bounded execution, memory and origin-restricted network access, plus verified anime episode and manga chapter adapters. See [the provider design](PROVIDER-PLUGINS-DESIGN.md). |
| Debrid | Nuvio's `DebridSettingsRepository` and `DirectDebridResolver` configure accounts and resolve provider links. | A selected provider, supported account/auth flow and authorized test content to verify provider errors and playback. |
| P2P | Nuvio's Android `P2pStreamingEngine` owns torrent lifecycle and the playback stream. | An approved Android engine and authorized test fixture for cancellation, network/storage limits and seeking. |
| Cross-device profiles and settings | Local AniList tracking does not store AniKatha profile settings. | An authorized profile sync backend, two clients, and an agreed conflict and deletion model for settings. Secure credentials and PINs stay on the device. |
