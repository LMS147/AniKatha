# AniList sign-in

Android builds use AniList's client-side token flow through the system browser. Register AniKatha in AniList's developer settings and set its redirect URI to `co.zyma.anikatha://anilist/callback`. Supply the public client ID with `-p:AniListClientId=<id>` when building AniKatha.Mobile. The ID is public. Android SecureStorage holds the authorization token. The desktop preview runs without account authorization.

## Build a sign-in-enabled APK on GitHub

1. Copy the public **Client ID** from [AniList developer settings](https://anilist.co/settings/developer).
2. In the AniKatha repository, open **Settings → Secrets and variables → Actions → Variables**. Create a repository variable named `ANILIST_CLIENT_ID` with that numeric ID.
3. Open **Actions → Build AniKatha → Run workflow** and choose a branch containing this workflow. Download the `AniKatha-android-debug` artifact from the completed run and install the APK.
4. Open **Profile → Connect AniList**. After approval, import your library and test a status, score, or completed count change against AniList.

The fresh-install emulator check verifies whether Profile exposes the sign-in button for the APK's configuration. The browser approval and account changes require a live AniList account.

On a configured Android APK, open Profile, choose Connect AniList, approve access in the browser, and check the displayed account. Test cancelling authorization, disconnecting, and connecting again. Existing local library and resume records stay on the device. AniList access tokens expire without a renewal token, so reconnect when access expires.

The episode and chapter counts in Library correspond to completed units. The Library controls can edit or reset them. Provider-only titles and partial watch or reading positions stay local until there is a verified AniList title and unit mapping.

Local edits are queued by account, AniList media ID and field. The latest explicit edit to a field wins when it conflicts with an AniList edit to that field, including a deliberate reset to zero. Import updates fields independently from the last observed AniList state. A pending edit protects its field from being overwritten by imports. Successful requests clear only the revision they acknowledge. Reconnection retries pending changes for the linked account. A score queued in a previous AniList scale needs to be re-entered on the new scale. Local resume records are independent of the list and queue.

## Cross-device resume and settings

The app has no authorized AniKatha service for exact position or settings sync. AniList completed counts cannot represent a media timestamp or a manga page. Item 20 needs an approved authenticated backend or account service, a per-account store keyed by stable media, title, unit, and provider identities, server-acknowledged record versions, and a two-device test. Sync positions with a versioned conflict policy that retains deliberate seeks or resets. The existing local records remain usable offline.

Item 21 is disabled for this session. If enabled later, the same service would need per-setting versioning and deletion records so older devices cannot recreate deleted preferences. Strip private provider configuration and credentials from synced settings.
