# AniKatha Library backup

Profile → Sync → Data management exports AniKatha JSON version 1. It includes
Anime and Manga Library entries, local statuses and counters, favourites, and
add-on history with exact video timestamps, reading pages, completion, and
available unit lengths. History is portable for add-ons with stable resource
IDs. Reinstall the same add-on on the destination profile to reopen those units.
Poster links with query strings are excluded.

The backup excludes AniList credentials and queued edits, provider
configuration, PINs, profile details, app preferences, downloaded files, and
AniList account-specific scores. Import is local and does not send edits to
AniList. It previews additions, validates before writing, and merges in a
single transaction. Existing entries and checkpoint values take precedence.
Reimporting the same file skips records already present.
