# Testing notes

Manual test notes per milestone. Automated coverage is listed first so manual testing can focus on what CI cannot see.

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
