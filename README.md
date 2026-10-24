<p align="center"><img src="packaging/assets/lumen-256.png" width="96" alt="Lumen logo"></p>

# Lumen

**A fast, native desktop client for Jellyfin** on Windows, macOS and Linux.

Lumen is a native app (Avalonia UI, no embedded browser) that plays your Jellyfin library with VLC. The goal is everything the Jellyfin web client can do, with smooth playback and a fraction of the memory a web-view client needs.

> **Status: early development.** Browsing, search and playback work against a real server; music, photos, Live TV, remote control and the admin dashboard are still to come. See [PLAN.md](PLAN.md) for the roadmap and feature checklist. Screenshots will be added once the design settles.

## Features

- **Connect and sign in:** http or https, password or Quick Connect, session restored on start, token kept in the OS credential store.
- **Home:** greeting and search, Continue watching, Next up, your libraries and a "Latest in" row per library.
- **Browse:** virtualized library grids with sort (name, date added, release date, rating, runtime) and filters (played, favourites, genres), remembered per library; search across movies, shows, episodes and more; favourites.
- **Details pages:** movies, episodes, series and seasons with metadata, cast, media info, watched and favourite toggles, play / resume / start over.
- **Player:** direct play with resume and progress sync; audio and subtitle tracks (embedded and external), subtitle delay, playback speed, chapters, volume, full screen.
- **Skip segments:** intro, recap, credits, preview and commercial buttons (or auto-skip) from the server's media segments, plus a next-episode countdown.
- **Transcoding:** quality menu in the player and in Settings; the server converts to HLS when you set a limit or a file cannot be played as it is, with an automatic fallback.
- **Hardware decoding** (VAAPI/VDPAU, D3D11/DXVA2, VideoToolbox through VLC), on by default.
- **Look:** violet and cream theme in dark, light or system mode, Inter font, restrained glass, Mica / vibrancy backdrop where the OS offers it.

Skip buttons need segments on the server, which come from a provider such as the Intro Skipper plugin; without segments nothing is shown.

## Using Lumen

1. Start Lumen and enter your server address: `192.168.1.10:8096`, `jellyfin.example.com` or a full `http://` / `https://` URL. Without `http(s)://`, Lumen tries https first, then http on Jellyfin's port 8096 (like the web client).
2. Sign in with your user name and password, or press **Use Quick Connect** and approve the code from a device where you are already signed in (Settings → Quick Connect).
3. Lumen remembers the server and keeps you signed in. The access token is stored in the OS credential store (Windows: encrypted with your Windows account; macOS: Keychain; Linux: the desktop keyring through `secret-tool`, or a file only your user can read).
4. Home shows your libraries, Continue watching and Next up. Click a library, show or season to open it, and a movie, episode or track to play it. Playback resumes where you left off and reports progress to the server.

### Keyboard shortcuts

| Key | Action |
|---|---|
| Space or K | Pause / play |
| ← / → | Back / forward (15 s by default, configurable in Settings) |
| ↑ / ↓ | Volume up / down |
| M | Mute |
| F | Full screen |
| Esc | Leave full screen |
| S | Skip the current segment (intro, recap, credits ...) |
| G / H | Subtitles earlier / later by 100 ms |
| [ / ] | Slower / faster |
| Ctrl+F or Ctrl+K | Search |
| Alt+← or the mouse back button | Go back |

### Mouse

Click the video to pause or resume, double-click it for full screen. A play button appears in the middle of the video while paused. The header and controls float over the video; they and the mouse cursor hide after a few seconds of playback without mouse movement (in a window and in full screen) and come back when you move the mouse.

### Settings

The gear at the bottom of the navigation pane opens Settings (changes apply and save immediately):

- **Playback:** skip segments per type (ask, auto-skip, off), auto-play next episode and countdown length, skip back / forward step.
- **Audio and subtitles:** preferred languages and subtitle mode (server default, always, only forced, off, only when the audio is in another language).
- **Video:** hardware decoding, streaming quality (maximum bitrate).
- **Appearance:** theme (dark, light, system).

Playback is direct play by default (the server sends the original file and VLC plays it), which covers almost every format. A quality limit (player menu or Settings), or a file that does not play, makes the server transcode to HLS instead; that needs transcoding to be allowed for your user on the server. HTTPS servers need a certificate the OS trusts (e.g. Let's Encrypt).

Lumen is not affiliated with the Jellyfin project.

## Download

Get the latest version from the **[download page](https://zupermini09.github.io/Lumen/)** or the **[Releases page](https://github.com/Zupermini09/Lumen/releases/latest)**.

| OS | File | How to install |
|---|---|---|
| Windows 10/11 (64-bit) | `Lumen-<version>-win-x64-setup.exe` | Run it. Installs for your user only, no admin rights needed. A portable `.zip` is also available. |
| macOS 13+ Apple Silicon | `Lumen-<version>-macos-arm64.dmg` | Open it and drag Lumen to Applications. |
| macOS 13+ Intel | `Lumen-<version>-macos-x64.dmg` | Same as above. |
| Linux (Debian, Ubuntu, Mint) | `Lumen-<version>-linux-amd64.deb` | `sudo apt install ./Lumen-<version>-linux-amd64.deb` (pulls in VLC's libraries automatically). |
| Linux (any distribution) | `Lumen-<version>-linux-x86_64.AppImage` | `chmod +x Lumen-*.AppImage` then run it. VLC is included. |

Each release includes `SHA256SUMS.txt` to verify downloads.

### Security warnings on first launch (unsigned builds)

The installers are not code-signed yet, so the operating system warns you the first time:

- **Windows:** SmartScreen says "Windows protected your PC". Click **More info**, then **Run anyway**.
- **macOS:** Gatekeeper says the app "cannot be opened" or "is damaged". In Applications, **right-click Lumen → Open**, then confirm. If macOS still refuses, open *System Settings → Privacy & Security* and click **Open Anyway**, or run `xattr -dr com.apple.quarantine /Applications/Lumen.app` in Terminal.
- **Linux:** no warning.

### Linux support

Linux builds are **experimental**: they are built and automatically tested in CI (the app starts, VLC loads and a test video plays under a virtual display, the `.deb` installs on a clean Ubuntu and the AppImage runs without VLC installed), but they have not been tested by hand on a real desktop yet. Reports are welcome.

## Building from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download). On Linux, also VLC's libraries (`sudo apt install libvlc5 vlc-plugin-base vlc-plugin-video-output`). Windows and macOS builds bundle VLC via NuGet.

```sh
dotnet build Lumen.sln
dotnet test Lumen.sln
dotnet run --project src/App
```

Installers are built by CI (`.github/workflows/build.yml`); every push produces them as workflow artifacts, and pushing a tag like `v0.1.0` publishes a GitHub Release. The scripts in `packaging/` can also be run locally on the matching OS.

### Project layout

```
src/
  App/        Composition root: startup, DI, logging, smoke-test mode
  Core/       Models and interfaces shared by all layers (no UI or native dependencies)
  Jellyfin/   Jellyfin API client, session persistence, image downloads
  Player/     LibVLC playback backend (behind IPlayerService)
  UI/         Views and ViewModels
  Platform/   OS-specific code (paths, native library setup, credential stores, settings)
tests/        Unit tests and smoke-test media
packaging/    Installer scripts (Windows Inno Setup, macOS .app/.dmg, Linux .deb/AppImage)
docs/         Download page (GitHub Pages)
```

### Publishing the download page

In the repository settings, under *Pages*, set the source to **Deploy from a branch**, branch `main`, folder `/docs`.

## License

Lumen is free software: you can redistribute it and/or modify it under the terms of the [GNU General Public License v3](LICENSE).

Lumen uses [LibVLC](https://www.videolan.org/vlc/libvlc.html) (LGPL-2.1-or-later), [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) (LGPL-2.1-or-later), [Avalonia](https://avaloniaui.net/) (MIT) and [FluentAvalonia](https://github.com/amwx/FluentAvalonia) (MIT).
