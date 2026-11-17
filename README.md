# AniKatha

Anime and manga, one story at a time. An Android-first .NET MAUI Blazor Hybrid app with five tabs: **Home, Anime, Manga, Library, Profile**.

## Foundation 0.1

Implemented:
- Original dark interface inspired by the supplied Nuvio screenshots, with floating five-tab navigation, responsive poster grids, featured artwork, and grouped settings.
- Live, non-adult AniList discovery/search with genre, year, season (anime only), format, sort, pagination, title details, and visible failure/retry states.
- SQLite saved titles, per-title status and episode/chapter counts, saved add-on manifests, and accent selection. Reopen/reload to apply the saved accent.
- Public HTTPS manifest validation, installation and removal. **Manifest management is implemented; provider execution is not.**
- Android project, shared Razor components, desktop-browser development preview, offline integration checks, and GitHub Actions debug APK build.

Not implemented yet:
- AniList OAuth/account sync, exact playback/page positions, native video player, chapter reader, downloads, background notifications, watch parties, and cloud accounts.
- Playback sources for Anikoto, chapter/page sources for Comix or MangaDex, Stremio resource execution, or JavaScript plugin execution. These need independently tested provider integrations; adding a manifest does not enable playback in this foundation.
- iOS/Windows packaging. The current mobile target is Android 7+; shared code can be extended to other MAUI platforms later.

No backend, paid hosting, API key, or database credentials are required for this foundation. Internet is needed for live discovery; saved library editing works offline. AniList is a metadata/tracking service, not a video or manga page host. Its terms and request limits apply.

## Run on Android

Install .NET SDK 10 with the MAUI Android workload, Android SDK and JDK 17 (or the SDK-required compatible JDK), then connect an Android device with USB debugging enabled.

```sh
dotnet workload install maui-android
dotnet build src/AniKatha.Mobile/AniKatha.Mobile.csproj -t:Run -f net10.0-android
```

Or open `AniKatha.slnx` in a compatible Visual Studio installation, select **AniKatha.Mobile** as startup project, and choose your Android device. The application ID starts as `co.zyma.anikatha`; finalise it before store publication.

## Browser development preview

The same Razor UI can be developed without the Android workload:

```sh
dotnet run --project src/AniKatha.Preview --urls http://localhost:5080
```

Open http://localhost:5080. This is a local developer preview, **not a hosted multi-user service**: it uses a shared development SQLite file. Do not expose it publicly. Bind to loopback as above. Native playback/device behaviour must be verified in the MAUI app.

## Checks and APK

```sh
dotnet build src/AniKatha.Preview -c Release
dotnet run --project tests/AniKatha.Checks -c Release
```

GitHub Actions builds the shared UI, runs offline persistence/API handling checks, and builds an Android debug APK. Download **AniKatha-android-debug** from a successful workflow's artifacts. This is a development APK, not a store-signed release.

## Structure

| Project | Responsibility |
|---|---|
| `src/AniKatha.Mobile` | MAUI Android shell and device storage registration |
| `src/AniKatha.UI` | Shared Razor pages, navigation, CSS, and details |
| `src/AniKatha.Core` | AniList client, SQLite models/store, manifest inspection |
| `src/AniKatha.Preview` | Local interactive browser development host |
| `tests/AniKatha.Checks` | Offline integration checks without a test-framework dependency |

See [architecture and next steps](docs/ARCHITECTURE.md).

## Licence

GPL-3.0, as selected when this repository was created. This foundation contains original implementation; Nuvio source and branding have not been copied. Third-party dependencies retain their licences.
