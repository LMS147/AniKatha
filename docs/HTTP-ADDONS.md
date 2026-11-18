# HTTP add-ons

AniKatha accepts an HTTPS base address or manifest.json. Configured paths and query strings travel to the same provider's resource endpoints. Existing manifests can omit anikathaProtocolVersion. The supported explicit version is the integer 1. The manifest's version field remains the provider's own release version. Refresh legacy installations to populate the stored manifest and capability display.

A catalogue declaration lists type, id, name, and optional extra entries named search and skip. Anime accepts anime and series. Manga accepts manga. The supported HTTP subset uses GET and JSON. Resource names may be strings or objects with name, types and idPrefixes. A second provider resolving another catalogue's unit must declare a matching ID prefix. Source order determines display order.

| Request relative to the manifest directory | Response |
|---|---|
| catalog/{type}/{catalogId}.json | metas array with id, type and name |
| catalog/{type}/{catalogId}/search={value}&skip={offset}.json | Same shape. Include only requested extras. Skip counts raw results. |
| meta/{type}/{titleId}.json | meta.videos array with each episode's id and title |
| stream/{type}/{episodeId}.json | streams array with name and direct HTTPS url |
| chapters/manga/{titleId}.json | chapters array with each chapter's id and title |
| pages/manga/{chapterId}.json | pages array of ordered HTTPS image URLs |

Chapter and page resources are AniKatha extensions. Samples are in examples/addons/story-sampler. Jikan supplies catalogue metadata. YouTube video IDs use the existing YouTube adapter. Progress keys for HTTP content include catalogue provider, media type, title, unit and source. Reinstalling the same provider URL restores access to its saved positions.
