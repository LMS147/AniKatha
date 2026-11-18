# Jikan Anime & Manga

This manifest activates AniKatha's Jikan catalogue adapter. The app requests anime and manga metadata directly from the public Jikan API. The manifest itself is a static JSON file and needs no separate server.

1. Open `manifest.json` on GitHub and copy its **Raw** URL.
2. In a build containing the Jikan adapter, open **Profile → Manage add-ons**, paste the URL and choose **Add source**.
3. Open **Anime** or **Manga** and select **Jikan Anime & Manga** under **Source**.
4. Verify title cards and covers. Search for an anime such as **Cowboy Bebop** or manga such as **Yotsuba&!**. Select **Load more** to check pagination when available.
5. Remove the add-on and return to either screen. Its source option should disappear.

Jikan is an unofficial MyAnimeList API. This integration supports popular titles, search, and pagination. It supplies metadata only, so streaming, chapters, and library tracking are separate features. AniKatha's adapter extension is specific to AniKatha.
