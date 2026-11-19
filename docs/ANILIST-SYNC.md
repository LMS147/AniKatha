# AniList sign-in

Android builds use AniList's client-side token flow through the system browser. Register AniKatha in AniList's developer settings and set its redirect URI to `co.zyma.anikatha://anilist/callback`. Supply the public client ID with `-p:AniListClientId=<id>` when building AniKatha.Mobile. The ID is public. Android SecureStorage holds the authorization token. The desktop preview runs without account authorization.

AniList uses the redirect URL saved in the registered client's settings for this flow. If the browser shows `unsupported_grant_type`, use an APK with the registered-redirect fix and confirm the callback above is saved for the same client ID used in the build.

## Build a sign-in-enabled APK on GitHub

1. Copy the public **Client ID** from [AniList developer settings](https://anilist.co/settings/developer).
2. In the AniKatha repository, open **Settings → Secrets and variables → Actions → Variables**. Create a repository variable named `ANILIST_CLIENT_ID` with that numeric ID.
3. Open **Actions → Build AniKatha → Run workflow** and choose a branch containing this workflow. Download the `AniKatha-android-debug` artifact from the completed run and install the APK.
4. Open **Profile → Connect AniList**. After approval, import your library and test a status, score, or completed count change against AniList.

The fresh-install emulator check verifies whether Profile exposes the sign-in button for the APK's configuration. The browser approval and account changes require a live AniList account.

On a configured Android APK, open Profile, choose Connect AniList, approve access in the browser, and check the displayed account. Test cancelling authorization, disconnecting, and connecting again. Existing local library and resume records stay on the device. AniList access tokens expire without a renewal token, so reconnect when access expires.

The episode and chapter counts in Library correspond to completed units. The Library controls can edit or reset them. Provider-only titles and partial watch or reading positions stay local until there is a verified AniList title and unit mapping.

Local edits are queued by account, AniList media ID and field. The latest explicit edit to a field wins when it conflicts with an AniList edit to that field, including a deliberate reset to zero. Import updates fields independently from the last observed AniList state. A pending edit protects its field from being overwritten by imports. Successful requests clear only the revision they acknowledge. Reconnection retries pending changes for the linked account. A score queued in a previous AniList scale needs to be re-entered on the new scale. Local resume records are independent of the list and queue.

## Complete units from an installed add-on

Add-ons supply the video or manga pages. AniList tracks completed episode and chapter counts. Connect AniList and import your lists, then select an imported title in **Library → Find Sources**. AniKatha checks enabled HTTP add-ons for an explicit title match. Select an episode or chapter and open a source. Watching an episode to the end or choosing **Mark chapter read** on the chapter's last page can update the count. Opening, seeking, and saving partial positions only affect local resume.

Use **Profile → Tracking → Automatically sync completed add-on episodes and chapters** to control automatic updates. The switch starts enabled for a linked account. Disabling it stops new automatic updates. Previously queued edits can still sync. AniKatha records an out-of-order mapped completion locally and advances AniList through the highest consecutive completed count once the gap is filled. A manual progress edit takes priority and clears the recorded automatic gap history for that title. Automatic requests send the progress field and preserve the app's explicit status and score editing. The embedded YouTube flow continues to use manual Library progress.

Mapped units opened or queued for download with this version save the mapping locally. Resume and offline playback can use it. Open older history entries through a mapped source to record the mapping for future completions. Timestamps and page positions stay on the device. The queue survives restart and retries through Android reconnect or **Profile → Sync pending changes**. If AniList already has a higher count or a reset below the last known count, AniKatha keeps AniList's count.

## Cross-device resume and settings

The app has no authorized AniKatha service for exact position or settings sync. AniList completed counts cannot represent a media timestamp or a manga page. Item 20 needs an approved authenticated backend or account service, a per-account store keyed by stable media, title, unit, and provider identities, server-acknowledged record versions, and a two-device test. Sync positions with a versioned conflict policy that retains deliberate seeks or resets. The existing local records remain usable offline.

Item 21 is disabled for this session. If enabled later, the same service would need per-setting versioning and deletion records so older devices cannot recreate deleted preferences. Strip private provider configuration and credentials from synced settings.
