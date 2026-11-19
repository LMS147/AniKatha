# AniList sign-in

Android builds use AniList's client-side token flow through the system browser. Register AniKatha in AniList's developer settings and set its redirect URI to `co.zyma.anikatha://anilist/callback`. Supply the public client ID with `-p:AniListClientId=<id>` when building AniKatha.Mobile. The ID is public. Android SecureStorage holds the authorization token. The desktop preview runs without account authorization.

On a configured Android APK, open Profile, choose Connect AniList, approve access in the browser, and check the displayed account. Test cancelling authorization, disconnecting, and connecting again. Existing local library and resume records stay on the device. AniList access tokens expire without a renewal token, so reconnect when access expires.

The episode and chapter counts in Library correspond to completed units. The Library controls can edit or reset them. Provider-only titles and partial watch or reading positions stay local until there is a verified AniList title and unit mapping.

Local edits are queued by account, AniList media ID and field. The latest explicit edit to a field wins when it conflicts with an AniList edit to that field, including a deliberate reset to zero. Import updates fields independently from the last observed AniList state. A pending edit protects its field from being overwritten by imports. Successful requests clear only the revision they acknowledge. Reconnection retries pending changes for the linked account. A score queued in a previous AniList scale needs to be re-entered on the new scale. Local resume records are independent of the list and queue.
