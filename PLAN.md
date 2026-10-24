# Lumen: a native Jellyfin desktop client (Windows, macOS, Linux): build plan

App name: **Lumen**. Use it for the solution/namespace, window title, installer names, bundle id (e.g. `app.lumen.client`, adjust if a domain is chosen), icons and the repo. Do not use the word "Jellyfin" in the product name; describe it as "a desktop client for Jellyfin".

## Goal
A fast, native desktop client for Jellyfin with **feature parity with Jellyfin web**, on **Windows, macOS and Linux**. Modern UI, VLC as the playback engine, low memory use, and easy to download and install.
Reason for existing: the official web-view based clients (JMP, Jellyfin web) lag during playback and use ~2 GB RAM.

## Scope
Everything the Jellyfin web client can do, delivered in phases (see Milestones). The "Parity checklist" below is the source of truth for "done". Keep it updated: tick items off, and add anything missing that you notice in the web client.

Only documented exception: **third-party plugin configuration pages** are HTML served by the plugins themselves and cannot be native. For those (and only those), provide an "Open in browser" link. Everything else must be native.

## Targets (hard requirements)
- Idle browsing: under 250 MB RAM. Playing 1080p/4K direct play: under 400 MB (excluding VLC's own video buffers).
- UI stays smooth (no stutter) while video is playing.
- No embedded browser engine (no WebView2, CEF, Electron, Qt WebEngine).
- Memory targets must hold as features are added; check them at the end of every milestone.
- Same app, same features, on all three operating systems. OS-specific code only where unavoidable (packaging, media keys, window chrome), behind interfaces.

## Stack (decided)
- Language: C#, **.NET 10 (LTS)**
  - Changed from .NET 8 in M0: .NET 8 support ends on 10 Nov 2026, and current FluentAvalonia releases (2.5+) only ship .NET 10 builds. .NET 10 is the current LTS (supported until Nov 2028).
- UI: **Avalonia UI 11.3** (cross-platform, native rendering, no browser engine) with **FluentAvalonia 2.5** for a modern Fluent look, dark theme.
  - Replaces the earlier WPF choice, because WPF is Windows-only.
  - Stay on Avalonia 11.3 (not 12) until LibVLCSharp.Avalonia supports Avalonia 12; LibVLCSharp.Avalonia 3.10 targets 11.3.
  - Package versions are pinned in `Directory.Packages.props`.
- Playback: **LibVLCSharp** + **LibVLCSharp.Avalonia** (video and audio)
  - Windows: `VideoLAN.LibVLC.Windows` NuGet (bundled)
  - macOS: `VideoLAN.LibVLC.Mac` NuGet (bundled in the .app)
  - Linux: system libvlc for the .deb (declared as a package dependency: `libvlc5`, `vlc-plugin-base`, `vlc-plugin-video-output`; the X11/OpenGL video outputs are in the last one); bundled libvlc + plugins for the AppImage
- Jellyfin API: ~~official **Jellyfin.Sdk** NuGet package~~ **plain HttpClient + System.Text.Json** (`src/Jellyfin/JellyfinClient.cs`), using the same endpoints as jellyfin-web/JMP; WebSocket for real-time features
  - Changed in M1: Jellyfin.Sdk pulls in the Kiota runtime and a very large generated client (startup time and memory, against the memory targets), its versions track one server release, and the app needs a few dozen endpoints. A thin hand-written client parses only the fields it uses, tolerates both JSON casings Jellyfin returns, and falls back to older routes (e.g. `Users/{id}/Views`) so older servers keep working. Revisit if the endpoint count grows large.
- MVVM: CommunityToolkit.Mvvm; DI via Microsoft.Extensions.DependencyInjection
- Settings/token storage: JSON in the per-user app data folder (Windows `%LOCALAPPDATA%`, macOS `~/Library/Application Support`, Linux `~/.config`), auth tokens stored in the OS credential store (Windows DPAPI/Credential Manager, macOS Keychain, Linux libsecret) behind an `ISecretStore` interface, with an encrypted-file fallback
- Logging: Serilog to a rolling file
- Additional packages (ask first): an EPUB/PDF/comic reader component for Books; keep it lightweight.

## Architecture
```
src/
  App/              Avalonia shell, DI setup, navigation
  Core/             Models, interfaces (IJellyfinService, IPlayerService, IImageCache, ISettings, ISecretStore, ISessionService)
  Jellyfin/         API wrapper, auth, WebSocket, playback reporting (added in M1)
  Player/           LibVLC wrapper (video + audio), tracks, queue, state
  UI/               Views + ViewModels per feature area
  Platform/         OS-specific implementations (secret store, media keys, autostart)
packaging/          Per-OS packaging scripts and metadata (see Distribution)
.github/workflows/  CI: build, test, package, release
```
Rules:
- ViewModels never touch the SDK or LibVLC directly; they go through interfaces.
- All network I/O is async with cancellation. Never block the UI thread.
- The player is behind `IPlayerService` so the backend can be swapped (e.g. to libmpv) later.
- No Windows-only APIs outside `Platform/`.

## Memory discipline (important)
- Virtualize every list/grid. No giant non-virtualized wrap panels.
- Request images from the server at display size (`maxWidth`/`maxHeight`/`quality`), decode to display size, bounded in-memory LRU (about 100 MB cap) plus disk cache.
- Dispose LibVLC `Media`/`MediaPlayer` on stop. One `LibVLC` instance for the app lifetime.
- Release page ViewModels and image sources on navigation away.
- Heavy features (photo viewer, reader, TV guide) must stream/page their data, not load everything.

## Distribution (installers)
Goal: a non-technical user opens the download page, clicks their OS, installs in a couple of clicks, and the app starts. Release artifacts are built **by CI (GitHub Actions), not locally**, because each OS must be built on its own runner (macOS builds need a macOS runner).

| OS | Artifact | Tooling |
|---|---|---|
| Windows | `Setup.exe` installer (Start menu shortcut, uninstaller, per-user install, no admin rights needed) + optional portable `.zip` | Inno Setup (or Velopack) |
| macOS | `.dmg` containing the `.app` (drag to Applications); separate Apple Silicon (arm64) and Intel (x64) builds, or one universal build | `create-dmg`, `codesign`, `notarytool` |
| Linux | `.deb` (Debian/Ubuntu/Mint) and `.AppImage` (any distro, no install) | `nfpm` for .deb; `appimagetool` for AppImage |
| Linux (later, optional) | `.rpm`, Flatpak | `nfpm`, flatpak-builder |

Requirements:
- `.deb` declares dependencies on libvlc (`vlc` / `libvlc5`) so `apt install ./app.deb` pulls them in automatically.
- AppImage bundles libvlc and its plugins so it runs without VLC installed.
- Self-contained publish (`dotnet publish --self-contained`) so users do **not** need to install .NET.
- Each installer includes app icon, version number, desktop entry (Linux), file/URL associations not required.
- One GitHub Releases page per version, containing all artifacts with clear names, e.g. `Lumen-1.0.0-win-x64-setup.exe`, `Lumen-1.0.0-macos-arm64.dmg`, `Lumen-1.0.0-linux-amd64.deb`, `Lumen-1.0.0-linux-x86_64.AppImage`. Include SHA256 checksums.
- A simple download page (GitHub Pages or the repo README) that detects the visitor's OS and shows one big download button, plus links to the other platforms.
- Auto-update (later milestone): check GitHub Releases on launch and offer the update. Velopack is the preferred library if it covers all three OSes; otherwise a simple "new version available" notice that links to the download page.

Code signing (decide before first public release):
- **Windows:** unsigned installers trigger a SmartScreen warning. Signing needs a certificate (paid, or Azure Trusted Signing / SignPath for open-source). Unsigned is acceptable for personal use.
- **macOS:** unsigned or un-notarized apps are blocked by Gatekeeper ("app is damaged / cannot be opened"). Proper distribution needs an Apple Developer account (about $99/year) for signing and notarization. Without it, users must right-click, then Open. Document this in the README if unsigned.
- **Linux:** no signing needed.

## Parity checklist (feature inventory)

**Accounts and connection**
- [ ] Server connect/validate, multiple saved servers, server discovery on LAN — *done: connect/validate (http + https, scheme/port probing like the web client), recent servers. Missing: LAN discovery.*
- [ ] Login, user selection screen with avatars, Quick Connect (authorize and use), manual login, forgot password flow — *done: manual login, public user list with avatars, Quick Connect (use). Missing: authorizing other devices, forgot password.*
- [ ] Switch user / sign out, per-user saved profiles — *done: sign out, session restore with the token in the OS credential store. Missing: switching between saved users.*

**Home and navigation**
- [ ] Home sections: Continue Watching, Next Up, Latest per library, Live TV, My Media; user-customizable section order/visibility — *done: My Media, Continue Watching, Next Up, Latest in each library; Home refreshes in place when navigated back to, and keeps its scroll position. Missing: Live TV row, section order/visibility.*
- [ ] Library views and navigation drawer, favourites, search with suggestions — *done: navigation items for Search and Favourites; search as you type (300 ms pause, one request per kind, Ctrl+F/Ctrl+K), results grouped by kind; favourites grouped by kind, refreshed on Back. Missing: search suggestions, people/artist results.*
- [ ] Collections, playlists (create/edit/reorder/add to), genres, studios, tags, years, people (actor/director pages)

**Library types**
- [ ] Movies, TV shows (seasons/episodes), Music (albums, artists, genres), Music videos, Home videos, Books/audiobooks, Photos, Mixed content, Live TV
- [ ] Sorting, filtering (unwatched, favourites, genre, year, rating, HD/4K/HDR, etc.), alphabet jump, view modes (poster, thumb, list), grouping — *done: library sort (name, date added, release date, rating, runtime; both directions) and filters (played/unplayed, favourites, genres), remembered per library in the settings. Missing: year, HD/4K/HDR filters, alphabet jump, view modes, grouping.*

**Item details**
- [ ] Metadata, cast and crew, trailers/special features, similar items, chapters, external links, media info (streams), versions picker — *done: movie, episode and series/season pages with metadata, cast, director/writers, media info (streams); seasons and episode lists; Missing: trailers/special features, similar items, chapters list, external links, versions picker.*
- [ ] Watched/favourite toggles, rating, play/resume/shuffle, add to playlist/collection, copy stream URL — *done: watched and favourite toggles, play/resume/start over, series Play/Resume next episode. Missing: rating, shuffle, playlists/collections, copy stream URL.*

**Video playback**
- [ ] Direct play, direct stream and transcoding fallback (HLS), quality/bitrate selection, hardware decoding (D3D11 on Windows, VideoToolbox on macOS, VAAPI on Linux) — *done: direct play (static stream); hardware decoding on/off in Settings (libvlc `avcodec-hw` per media, default on; libvlc picks the API: VAAPI/VDPAU, D3D11/DXVA2, VideoToolbox). Missing: transcoding/HLS, bitrate selection, choosing a specific API.*
- [ ] Audio and subtitle track selection (embedded, external, burn-in via server), subtitle delay/size/appearance, remote subtitle search and download — *done: track menus, subtitle delay, external subtitle files; preferred audio and subtitle language and subtitle mode (server default / always / only forced / off / only when the audio is in another language) in Settings, with the server's defaults as fallback. Missing: burn-in, size/appearance, remote search.*
- [ ] Chapters, trickplay thumbnails on the seek bar, next-episode autoplay with countdown, resume, playback speed
- [ ] **Skip segments** via the server's Media Segments API (`/MediaSegments/{itemId}`), one on-screen "Skip ..." button per segment type: Intro ("Skip Intro"), Recap ("Skip Recap", i.e. "previously on..."), Outro/Credits ("Skip Credits" / jump to next episode), Preview, Commercial. Per-type setting in Playback settings: Ask / Auto-skip / Off (done, in Settings). Button appears when playback enters the segment and hides when it ends. Hide gracefully when the server has no segments for an item or is too old to have the API. Segments are created by server-side providers (e.g. the Intro Skipper plugin); the client only reads them, so document in the README that a provider plugin is needed.
- [ ] Playback progress reporting, watched marking, fullscreen, picture-in-picture, keyboard shortcuts, media keys, stats overlay — *done: progress reporting (start/10 s/pause/stop), watched on finish, resume, fullscreen, keyboard shortcuts.*
- [ ] Multiple versions, audio normalization

**Music playback**
- [ ] Persistent bottom player bar, queue (add/reorder/clear), shuffle/repeat, gapless where VLC allows, lyrics (synced), now-playing screen, instant mix/radio, OS media controls integration

**Photos and books**
- [ ] Photo grid and viewer with slideshow, zoom/rotate; EPUB/PDF/comic (CBZ/CBR) reader, audiobook playback

**Live TV and DVR**
- [ ] Channel list, TV guide (EPG grid, virtualized), program details, live playback with timeshift, recordings, timers and series timers

**Remote control and SyncPlay**
- [ ] Appear as a controllable session (WebSocket), accept play/pause/seek/volume commands from other clients
- [ ] Control other sessions (cast to another device), SyncPlay (create/join group, synced playback)

**User settings (everything in the web "Settings" area)**
- [ ] Profile (name, password, image), Display (theme, language, date/time, item counts, backdrops, theme songs/videos), Home layout, Playback (default tracks, auto-play next, max bitrate), Subtitles, Quick Connect, Controls/keyboard, Parental/Access views — *done: Settings page (navigation footer gear), applied and saved immediately: skip segments per type, auto-play next episode and countdown length, skip back/forward step, preferred audio/subtitle language and subtitle mode, hardware decoding, theme (dark / light / system). Missing: profile, language, date/time, item counts, backdrops, theme songs, home layout, max bitrate, subtitle appearance, Quick Connect authorize, key bindings, server-synced preferences.*

**Admin dashboard (native, permission-gated by the user's admin flag)**
- [ ] Dashboard overview: active sessions, activity, server info, restart/shutdown, scan libraries
- [ ] Users (create/edit/delete, policies, parental controls, library access, devices), API keys, devices
- [ ] Libraries (add/edit/remove, paths, metadata/image fetcher settings, scan, refresh metadata)
- [ ] Playback settings: transcoding, hardware acceleration, trickplay, resume, streaming, subtitles
- [ ] Networking, scheduled tasks, plugins (catalog, install/uninstall/enable, repositories), logs viewer, activity log, branding, notifications, DLNA, Live TV tuners/guide providers/recording settings, metadata editor, identify/fix match, image editor, subtitle editor
- [ ] Backup/restore if the server version exposes it

## Progress

- **M0 (done, pending manual checks on Windows/macOS, see TESTING.md):** solution layout, Avalonia + FluentAvalonia shell, `IPlayerService` with the LibVLC backend and a playback test page (with an overlay-over-video probe), per-OS paths and libvlc loading in `Platform/`, Serilog logging, CI producing `.exe` + portable `.zip`, `.dmg` (arm64 and x64), `.deb` and `.AppImage`, smoke tests on all three OSes, release job with SHA-256 checksums, download page in `docs/`.
  - Lessons: LibVLCSharp asks for the unversioned `libvlc` on Linux (only present with the `-dev` package), so `Platform/LibVlcLocator` maps it to `libvlc.so.5`; disposing LibVLC from Avalonia's `Exit` event can race the native video surface and crash, so services are disposed after the UI lifetime ends. The AppImage must bundle `libva`/`libvdpau` (Ubuntu's libavcodec links them, so without them VLC has no video decoder); CI fails if the AppImage misses any library other than the host GL/EGL stack and PulseAudio. GitHub's macOS runners have no GPU, so CI cannot check video output on macOS (manual check).

- Memory note from real-server testing: software-decoding 4K HEVC 10-bit takes ~1.2 GB (VLC frame buffers, excluded from the target but worth reducing); hardware decoding and `--avcodec-threads` tuning belong to M3. On Linux the app trims the native heap after playback (`Platform/NativeMemory`).
- **M1 + early M2/M3 (in progress, pulled forward so the app can be tested against a real server):** connect (http/https), sign in (password, Quick Connect), session restore with OS credential stores (Windows DPAPI, macOS Keychain, Linux Secret Service with a 0600-file fallback), Home (My Media, Continue Watching, Next Up), virtualized library grids with a bounded image cache, folder navigation (library → show → season → episode, album → track), direct play with resume and progress reporting, back-stack navigation, `--smoke-server` end-to-end test.

## Milestones
Each milestone ends with: builds clean on all three OSes (via CI), app runs, manual test notes in `TESTING.md`, parity checklist ticked, memory targets verified, a git commit.

**M0: Foundations + packaging early.** Solution layout, Avalonia hello-world with FluentAvalonia theme, LibVLC playing a test file on all three OSes, **CI pipeline that produces all four installers (.exe, .dmg, .deb, .AppImage) from the very first version**, and a README with download links. Doing this first means packaging problems (especially libvlc bundling) are found early, not at the end.
**M1: Skeleton + accounts.** Shell, navigation; connect/login/Quick Connect/user switching; secure token storage per OS.
**M2: Browse.** Home sections, library views, filters/sorting, details pages, search, favourites, collections, playlists, people/genre pages.
**M3: Video playback core.** Everything in "Video playback": direct play first, then transcode fallback, tracks, subtitles, chapters, trickplay, skip intro, progress reporting.
**M4: Music, photos, books.** Music player + queue + lyrics, photo viewer, book/comic reader, audiobooks, OS media keys.
**M5: Sessions.** WebSocket, remote control, cast to other devices, SyncPlay.
**M6: Live TV and DVR.** Channels, guide, live playback, recordings and timers.
**M7: User settings.** All of the settings area, display preferences synced with the server where the web does.
**M8: Admin dashboard.** Users, libraries, playback, plugins, tasks, logs, metadata/image/subtitle editors. Split into two or three sub-sessions if needed.
**M9: Polish and ship.** Skeleton loaders, error/offline states, window state, accessibility, localization scaffolding, auto-update, code signing/notarization (if accounts exist), download page, performance pass.

M3 comes before M4-M8 because playback is the main reason the app exists; M8 is the biggest and safest to do last.

## Instructions for Claude Code
- Work one milestone at a time (split big ones). Before starting, restate the plan in 5 lines, then implement.
- Run `dotnet build` after every meaningful change and fix errors before moving on. Run `dotnet test` where tests exist.
- You can build and test only on the OS you are running on. For the other two, rely on the CI workflow and tell me exactly which manual checks I (or a tester) must do on those OSes.
- To find out exactly what the web client does for a feature, check the jellyfin-web source (github.com/jellyfin/jellyfin-web) and the server API docs (the server's own `/api-docs/swagger`). Match its behaviour and endpoints.
- Add unit tests for non-UI logic (API mapping, playback decision logic, cache eviction, queue handling).
- You cannot see the UI. When a visual or playback behaviour needs checking, stop and give me a short numbered checklist to verify manually, and wait for my report.
- Keep dependencies minimal. Ask before adding any NuGet package not listed above.
- Never hard-code server addresses, credentials or tokens; never log tokens.
- Keep all OS-specific code in `Platform/` or `packaging/`.
- Commit after each milestone with a clear message. Keep the parity checklist in this file current.
- If a decision in this plan turns out to be wrong, say why and propose an alternative instead of working around it silently.

## Known risks
- **Very large project.** Comparable to a full client. Expect many sessions. Keep each session small and verified.
- **LibVLC across OSes is the biggest technical risk.** Avalonia's video view uses a native child window, which has overlay ("airspace") limitations for controls drawn over video; LibVLCSharp.Avalonia supports overlay content, but test it on all three OSes in M0. macOS and Wayland-based Linux have had rough edges. Bundling libvlc inside the AppImage and the .app is fiddly. If this proves unworkable, fall back to libmpv behind `IPlayerService`.
- **Linux cannot be manually tested** (the owner only has Windows and Mac). Mitigation: CI builds and runs automated smoke tests on an Ubuntu runner (app launches headless via Xvfb, libvlc loads, unit tests pass, .deb installs and the AppImage starts), Linux-specific code is kept minimal and isolated in `Platform/`, and Linux is labelled "experimental / community-tested" in the README until someone verifies playback. Do not claim Linux works beyond what CI proves.
- VLC is weaker than mpv for HDR tone-mapping and fancy ASS subtitles.
- Jellyfin.Sdk changes between versions; pin the version and test against the target server version.
- Some features depend on server version or plugins (media segments, trickplay, lyrics, SyncPlay); detect and hide gracefully.
- Plugin config pages are web-only (see Scope).
- Unsigned macOS and Windows builds will show security warnings (see Distribution).

## Open questions
1. Which Jellyfin server version are you running? (Determines which APIs exist.)
2. Do you have, or will you get, an Apple Developer account (for macOS signing) and a Windows signing certificate? If not, we ship unsigned with clear instructions.
3. Testing: owner has Windows and Mac only; Linux is covered by CI smoke tests (see Known risks).
4. Is HDR important? (Affects whether to consider libmpv.)
5. ~~App name~~ Decided: Lumen. Before the first public release, check for naming conflicts (GitHub, app stores, trademarks); "Lumen" is a common name (e.g. Laravel Lumen, Lumen Technologies). If it clashes, rename in one place (see top of this file).
