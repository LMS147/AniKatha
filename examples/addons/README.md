# AniKatha add-on test fixtures

Use these fixtures to test manifest installation on any AniKatha build that has **Profile → Content & discovery → Manage add-ons**. Each fixture is a static JSON file. The app can install, store, and remove manifests. Catalogue loading, video playback, and manga reading from add-ons require a separate implementation. The `manga` type here is fixture metadata for AniKatha.

| Fixture | Expected result |
|---|---|
| [Anime Demo](anime/manifest.json) | Adds **AniKatha Anime Demo** at version 1.0.0. |
| [Manga Demo](manga/manifest.json) | Adds **AniKatha Manga Demo** at version 1.0.0. |
| [Invalid Demo](invalid/manifest.json) | Shows **Manifest is missing id.** The list stays the same. |

1. Open a fixture on GitHub, select **Raw**, and copy the HTTPS URL ending in `/manifest.json`.
2. Open **Profile → Content & discovery → Manage add-ons**. Paste the URL and tap **Add source**. Repeat for the other valid fixture.
3. Close and reopen AniKatha. Both demo entries should still be listed.
4. Submit the same anime URL again. The list should still show one anime demo entry.
5. Submit the invalid URL. Check that the error appears and both valid entries remain.
6. Remove one entry and reopen the app. Only the other entry should remain.


## YouTube episode add-ons

Open **Profile → Manage add-ons** and add the public HTTPS URL of the [Gundam manifest](gundam-youtube/manifest.json). Its entry appears in **Anime → Episodes from add-ons** and on the matching AniList title.

Create your own by copying that manifest. Declare `stream` and `anime`, and add 1–200 entries to `anikathaYoutubeEpisodes`. Each entry has the AniList series ID, series name, episode number, episode title, and YouTube video ID. Use official uploads that allow embedding. Android uses the official YouTube player and keeps local resume timestamps per video. An **Open in YouTube** action covers episodes whose availability varies by device or region.

Update the JSON and reinstall the manifest to refresh its mappings.
