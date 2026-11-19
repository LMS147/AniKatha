# AniList tracking UI

The baseline was AniKatha main at `4b38663`. Account tracking uses the existing AniList OAuth flow and profile-scoped outbox.

**Profile → AniList** shows the linked account and its avatar or initials. Imports report counts only after reading both lists. The import timestamp records a finished import or refresh. Earlier imported profiles can see Refresh lists without a fabricated timestamp. Pending changes are separate from import. Disconnect removes the link and leaves Library and exact resume records on the device.

**Library → Edit tracking** opens a page for changing status, completed count, and the linked account's score. Blank clears a score. The current score transport represents unset as zero and the editor does not offer a separate zero rating. A lower completed count requires confirmation. Provider-only titles can still edit local status and completed count. The title page and discovery details link to the same editor. Continue Watching, Continue Reading and Find Sources stay independent of completed-count editing.

The editor saves changed fields and their outbox rows in one transaction. A separate acknowledgement marker appears only when the current queued revision is acknowledged. Pending edits take precedence over an earlier confirmation. Import and acknowledgement metadata use existing AppSetting storage and require no table migration. Existing local-only edits do not become retroactive queue entries.

The player and reader continue using verified add-on-to-AniList mappings. Completion feedback describes the device save, pending queue, confirmation, or missing match. Partial playback time and reading page changes stay local.

GitHub Actions captures the disconnected account and local editor from the Android APK. The linked account and pending screenshots use deterministic UI fixtures and AniKatha's stylesheet. The test account shown in those screenshots is synthetic. A physical device and an authorized account are needed to verify the live OAuth callback and network transition end to end.
