# Testing notes

## Features since M1: manual checks

Most of this was only driven on Linux (virtual display and a real server). Items marked **[Win/macOS]** have never run on those systems and need a hands-on check. Report OK / not OK, the OS version and what you saw.

### Windows and macOS only

1. **[Win/macOS] Backdrop.** Windows 11: the window background shows Mica; Windows 10 and macOS: acrylic / vibrancy blur or a plain solid background, never a see-through or black window. Check dark and light theme (Settings), and that text stays readable.
2. **[Win/macOS] Icons.** After installing, check the new violet monogram in the installer, Start menu / Applications, taskbar / Dock, window title bar, and `.ico` / `.icns` in Explorer / Finder at small and large sizes. The 16-32 px versions should still read clearly.
3. **[Win/macOS] Click-to-pause and play button.** Click the video once: it pauses/resumes (not twice, not on a drag). Double-click: full screen toggles and the video does not pause/resume again. While paused, the round play button shows in the centre and clicking it resumes. Try with the primary mouse button swapped to the right (Windows) and with a trackpad tap (macOS). Video clicks use polled button state (`GetAsyncKeyState` / `CGEventSourceButtonState`) because the video is a native window.
4. **[Win/macOS] Overlay and glass.** Header, controls and menus stay visible over/next to the video, also after resize, maximise and full screen. Controls appear on mouse move in full screen and hide after a pause; Esc leaves full screen.
5. **[Win/macOS] Hardware decoding.** Settings → Video: on, play a 4K HEVC / H.264 file and check CPU/GPU use (Task Manager / Activity Monitor) and RAM. Turn it off, play again: the picture is the same and CPU use goes up. Note any green or corrupt frames.

### All systems

6. **Transcoding and quality.** In the player, pick a lower quality (e.g. 4 Mbps): playback restarts at the same position, the state reads "Transcoding ...", picture and sound are fine. Back to Original: "Direct play". Set a limit in Settings and play something new. With transcoding disallowed for the user on the server, a clear message appears.
7. **HLS seeking.** While transcoding, seek forward, backward, to the very start and near the end (click the bar and use ← / →). Playback resumes at the right place each time, no long freeze.
8. **Subtitle sync while transcoding.** Transcode a video with text subtitles. The subtitles must match the speech (the HLS timeline starts about 10 s off, Lumen compensates). Change the subtitle delay with G / H, switch tracks, seek, and re-check. Picture subtitles (PGS, DVD) are burned in by the server and should also match.
9. **Tracks.** Audio and subtitle menus list the right tracks; the preferred languages in Settings are picked on start. Switching audio while transcoding restarts the stream at the same position.
10. **Speed, chapters, skip.** `[` / `]` change speed; the chapter list jumps; ± step keys use the Settings value. With a segment provider on the server: the "Skip ..." button shows during the segment, S skips it, auto-skip works per type.
11. **Next episode.** Near the end of an episode the countdown shows; Cancel keeps you on the credits, Next plays the next episode immediately; auto-play can be turned off.
12. **Details, search, favourites.** Open a movie, an episode and a series (season selector, episode cards). Toggle watched and favourite and check the web client. Ctrl+F / Ctrl+K opens Search; results group by kind. Favourites refresh on Back. Home "Latest in" rows match the web client.
13. **Library sort and filter.** Change sort and filters, leave and return: they are remembered per library.
14. **Settings.** Every option applies without restart and survives a restart. Theme dark / light / system switches live.
15. **Mouse and keys.** Alt+← and the mouse back button go back (and leave full screen first); all shortcuts in the README work.

Manual test notes per milestone. Automated coverage is listed first so manual testing can focus on what CI cannot see.

## M1 (first part): connect, sign in, browse, play

### Automated

- Unit tests: server address candidates (scheme/port rules), Authorization header, JSON mapping in both PascalCase and camelCase (Jellyfin uses both), library queries, session persistence and restore, revoked tokens, secret file store (DPAPI on Windows, 0600 elsewhere), settings recovery from a corrupt file, LRU cache, token redaction in player logs.
- End-to-end smoke test against a real server (not in CI, it needs a server and an account):
  ```
  LUMEN_SMOKE_USER=name LUMEN_SMOKE_PASSWORD=secret Lumen --smoke-server https://your.server
  ```
  Connects, signs in (without replacing your saved session), loads Home and a library, plays the first video it finds through the real player page for a few seconds, reports progress and signs out. Prints `SMOKE: PASS ...` or `SMOKE: FAIL ...`.

### What was verified while building it

- The request/response formats were checked against a real Jellyfin 10.11.11 server (sign-in, library views, items, Quick Connect enabled).
- The UI flow was then driven on a virtual display against a local stand-in that replays those responses: connect (http first for :8096), wrong password message, sign in, Home (libraries, Continue watching with progress bar, Next up), a 120-item library grid (virtualized: only visible posters are downloaded), show → season → episodes, playback with start/stop reports, restart restores the session, sign out removes the token, Quick Connect sign-in.
- **Real server (Jellyfin 12.1.0 behind an https tunnel, real library):**
  - `--smoke-server`: PASS (server found over https, signed in, libraries loaded, a movie played with video output, progress reported, signed out).
  - Driven through the UI: login page with the server's users and avatars, sign in, Home with library images, a 106-movie library with posters (virtualized, smooth scrolling, ~285 MB), playback of a 4K HEVC 10-bit Dolby Vision movie, seek, Back, the movie appeared in Continue watching with its thumbnail and 41 % progress, app restart restored the session, resume continued at the saved position, sign out revoked the token.
  - Tokens are redacted in the log (`api_key=***`).
  - Memory on this test machine (no GPU, so 4K HEVC is decoded in software): ~200–400 MB browsing, ~1.2 GB while software-decoding 4K HEVC (VLC frame buffers), back to ~420 MB after stopping. Before the fix below it stayed at ~700 MB after stopping: on Linux, glibc kept libvlc's freed buffers, so the app now calls `malloc_trim` after playback ends. With hardware decoding (a real GPU) playback memory should be far lower: please note the RAM readout in step 10.
- Plain-http servers were checked against a real Jellyfin 10.11 server during development; the LAN address could not be reached from the cloud test machine, so step 1's LAN check is manual.

### Manual checklist

1. Connect with `https://your.domain` and, at home, with the LAN address (`http://192.168.x.x:8096`). Both should reach the sign-in page showing the server name and version.
2. Sign in with a wrong password (clear error), then the right one. Try Quick Connect once.
3. Home: libraries, Continue watching and Next up match the web client. Posters load.
4. Open a movie library with many items and scroll fast: smooth, posters fill in, RAM readout stays reasonable.
5. Open a show → season → episode and play. Check picture, sound, seek, pause, volume, full screen (F / double-click), Esc.
6. Stop halfway (Back). The item shows in Continue watching in Lumen *and* in the web client at about the same position. Play it again: it resumes there; "Start over" restarts.
7. Play an episode to the end: it is marked watched (check mark) in Lumen and the web client.
8. Play a music track.
9. Close and reopen Lumen: it opens straight on Home without signing in. Sign out: back to the connect screen; reopening asks to sign in.
10. Note the RAM readout idle on Home and while playing 1080p / 4K.

## M0: Foundations + packaging

### Automated (CI, every push)

- Unit tests on Windows, macOS and Linux (`dotnet test`).
- Smoke test on every OS: the published app starts, loads libvlc, opens the main window and plays `tests/media/smoke-6s.mp4` for at least 2 seconds with a real video output, then exits (`Lumen --smoke-test --smoke-play <file>`).
  - Windows: published `win-x64` folder. macOS: the signed `Lumen.app` for arm64 and x64. Linux: published folder with system libvlc (Ubuntu 22.04, under Xvfb).
- Linux install test on a clean Ubuntu 24.04 runner: the AppImage plays the test video with **no VLC installed**, then the `.deb` installs via `apt` (pulling in libvlc) and plays it.

Verified locally while building M0 (Ubuntu 24.04, Xvfb): smoke test passes from the build output, the installed `.deb` (with only the runtime libvlc packages, no `-dev`) and the AppImage with all system libvlc packages removed. RAM while playing a 720p test video in software rendering: about 335 MB including VLC's buffers; idle with the window open: about 200 MB.

### Manual checklist (Windows and macOS)

Download the artifacts from the latest CI run (Actions → Build → artifacts `windows`, `macos-arm64` / `macos-x64`) or from a release.

1. **Install.** Windows: run the setup `.exe` (expect the SmartScreen warning: More info → Run anyway); it should not ask for admin rights, should offer to launch Lumen, and add a Start menu entry. macOS: open the `.dmg`, drag Lumen to Applications, right-click → Open the first time.
2. **Launch.** The window opens with a dark theme, the Lumen icon in the title bar/taskbar/Dock, a left navigation pane ("Playback test", "About") and a RAM readout at the bottom right.
3. **Play a local video.** On "Playback test", click Browse… and pick a 1080p (and, if you have one, a 4K) video file. It should play with sound.
4. **Overlay.** While the video plays, the label "Overlay test: this label should stay visible over the video" must be visible on top of the video, at its top-left corner. Also check it after resizing the window and after maximizing.
5. **Controls.** Pause/resume, Stop, drag the seek bar, change volume, mute. Time labels update.
6. **Smoothness.** While the video plays, move/resize the window and hover the navigation pane: the UI should not stutter and the video should not freeze.
7. **Memory.** Note the RAM readout: idle (before playing) and during 1080p/4K playback. Targets: under 250 MB idle, under 400 MB while playing (VLC's own video buffers excluded, so somewhat higher values during 4K playback are expected).
8. **Stream URL.** Paste an `http(s)` URL of a video into the box and press Play.
9. **About page.** Shows the version, the LibVLC version, runtime info and the log folder.
10. **Close and uninstall.** Closing the window quits the app cleanly (no crash dialog). Windows: uninstall via Settings → Apps; macOS: drag Lumen.app to the Bin.

Please report for each step: OK / not OK (+ what happened), the OS version and the RAM numbers from step 7. Logs are in `%LOCALAPPDATA%\Lumen\logs` (Windows) and `~/Library/Logs/Lumen` (macOS).

### Known limitations in M0

- No Jellyfin connection yet (M1).
- Hardware decoding uses VLC's defaults; explicit D3D11 / VideoToolbox / VAAPI selection comes with M3.
