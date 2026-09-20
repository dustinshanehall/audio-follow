# audio-follow

Windows tray app (C#, .NET 10, WinForms + WPF UI Automation) that restarts apps which ignore default audio output changes, such as the Apple Music app. Optionally restores what was playing afterwards. See `README.md` for user-facing docs.

## Layout

- `Program.cs`: config records (`Config`, `Target`), `Watcher` (Core Audio default-device notifications via NAudio, debounce, kill and relaunch), tray icon and menu, startup registration (HKCU Run key), config saving.
- `Resume.cs`: Apple Music resume. Captures now-playing from the Windows media session (SMTC), resolves an album link through the iTunes Search API, opens it with `music://`, and optionally double-clicks the track row through UI Automation.
- `SettingsForm.cs`: settings dialog, built in code (no designer), follows system light/dark mode, tooltips on every setting.
- `Log.cs`: opt-in debug log (`debug` in config) written next to the exe.
- `config.json`: defaults shipped with the app. Runtime copy lives next to the installed exe.
- `tools/install.ps1`: publishes to `%LOCALAPPDATA%\audio-follow`, keeps the installed `config.json`, restarts the app. `-Uninstall` removes it.
- `tools/generate-icon.ps1`: renders `Assets/app.ico` and `Assets/app-preview.png` with System.Drawing.

## Build and run

```
dotnet build -c Release
pwsh tools\install.ps1
```

Build output is `bin\Release\net10.0-windows10.0.19041.0\`. The build does not overwrite a config.json in the output folder that is newer than the source one, so copy it explicitly when testing defaults. Stop the running instance before rebuilding (it locks the dll); `install.ps1` does this.

## Design decisions

- Event-driven, no polling: idle CPU is ~0. Only the Multimedia-role render default is watched.
- Debounce after a device change (default 2500 ms) because Bluetooth devices fire several events.
- Resume is opt-in and split in two: `resumeAlbum` (no UI automation) and `resumePlayback` (double-click, moves the mouse pointer). Both send song/artist/album to Apple's public search API.
- Playback resumes from the start of the song only; SMTC position is unreliable.
- Startup: `startWithWindows` defaults to true; launch only registers if no working registration already exists, so a dev build never overrides an installed copy. The tray/Settings toggle is authoritative.
- No prebuilt binaries in the repo; users build from source.

## Gotchas learned the hard way

- Apple Music's per-app output routing in Windows' volume mixer does NOT move a playing stream; a relaunch is the only fix.
- SMTC `Artist` for Apple Music is "artist — album" with an EM DASH; it is normalized to " - " in `Capture`.
- The iTunes Search API is fuzzy and omits tracks from multi-word queries. Reliable path: find the album id (song search by album name, then artist album search), then `lookup?id=...&entity=song` and pick the track.
- `music://` links only navigate; search-term links are ignored. Track links (`.../album/x/ID?i=TRACKID`) open the album page.
- The SMTC Play command is accepted but does nothing until a queue exists. Invoking a track row (InvokePattern) and Enter both fail to play; a real double-click works.
- UI Automation names: track rows are `ListItem` named like `Track 4 Title 3 minutes, 2 seconds`. The transport Play button is disabled until something is queued.
- `UseWPF` disables some implicit usings (`System.IO`, `System.Net.Http`); add them explicitly.
- Windows PowerShell 5.1 (not pwsh 7) is needed to call WinRT media APIs from scripts.
- Store apps launch with `shell:AppsFolder\<AppUserModelID>`; find IDs with `Get-StartApps`.

## Conventions

- Keep the repo free of personal information and machine-specific paths.
- Do not use "Apple" in project or repo names (trademark); "for Apple Music" in descriptions is fine.
- Comments only where the reason is non-obvious.
- Commit author is Dustin Hall. Commit messages end with the Claude co-author trailer.
- Manual test for the core behavior: play a song, switch the default output device, wait ~15 s, confirm audio follows and (if enabled) the song resumes. Turn on `debug` to see each step in `audio-follow.log`.
