# audio-follow

Tiny Windows tray app that restarts apps which ignore audio output changes, like Apple Music. Event-driven, ~0% idle CPU.

## The problem

Some Windows apps, notably the Apple Music app, keep playing through the old device after you change your default audio output. Windows' per-app output routing doesn't move them either. The only fix is to relaunch the app.

## What it does

audio-follow subscribes to Windows' default-output-device change notification. It does no polling, so it uses no CPU while idle. When the default playback device changes, it waits a short debounce period (Bluetooth devices often fire several events in a row), then closes and relaunches each configured app that is currently running. Apps that aren't running are left alone.

## Configuration

Edit `config.json` next to the executable:

```json
{
  "debounceMs": 2500,
  "targets": [
    {
      "name": "Apple Music",
      "processes": ["AppleMusic", "AMPLibraryAgent"],
      "launch": "shell:AppsFolder\\AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"
    }
  ]
}
```

- `processes`: process names (without `.exe`) to close before relaunching.
- `launch`: what to start afterwards. For Store apps use `shell:AppsFolder\<AppUserModelID>` (find IDs with `Get-StartApps` in PowerShell). For regular apps use the path to the `.exe`.

## Resuming what was playing (Apple Music, optional)

Apple Music restores nothing after a relaunch: no queue, no song. Two optional settings, both off by default, can bring back what was playing. They only apply to targets with `"resume": "appleMusic"`.

- `resumeAlbum`: reopens the album page of the song that was playing. No UI automation and nothing is played.
- `resumePlayback`: does the above, then double-clicks the song's row to start it from the beginning. This uses Windows UI Automation and simulated mouse input: it briefly brings Apple Music to the front and moves the mouse pointer, so don't click in the window while it runs. It only plays if a song was playing (not paused) before the switch.

How it works: the song title and "artist - album" are read from the Windows media controls before the app is closed. After the relaunch, the album is located through Apple's public iTunes Search API and opened with a `music://` link. Enabling either setting therefore sends the artist, album and song title to Apple's API. If any step fails, the app is simply left as it launched.

`storefront` selects the country catalog used for the lookup. Set `debug` to `true` to write `audio-follow.log` next to the executable.

## Install

Requires the .NET 10 SDK on Windows 10 (19041) or later. No prebuilt binaries are provided; build from source:

```
pwsh tools\install.ps1
```

This publishes to `%LOCALAPPDATA%\audio-follow`, keeps any existing `config.json` there, and starts the app. Re-run it to update. `pwsh tools\install.ps1 -Uninstall` stops the app and removes the startup entry and the folder.

To just build and run from the source folder instead:

```
dotnet build -c Release
bin\Release\net10.0-windows10.0.19041.0\audio-follow.exe
```

## Start at login

`startWithWindows` (default `true`) registers the app to start at login. On launch, the app registers itself only if no working registration exists, so running a build from another folder won't override an installed copy. The tray menu's "Start with Windows" toggle changes the registration and updates this setting in `config.json`.

## Tray menu

Right-click the tray icon for Pause, Settings and Exit. Double-clicking the icon opens Settings.

The Settings dialog covers the resume mode (Off / Reopen album / Reopen album and play), start at login, the storefront used for song lookup, the wait after a device change, and the debug log. Changes apply immediately when you click OK and are saved to `config.json`. The target apps list is edited in the config file (use "Edit config file…" in the dialog) and needs a restart of the app.

## Limitations

- Playback restarts from the beginning of the song; the position is not restored.
- It reacts to the default playback device (Multimedia role) only.
- Resume depends on the names Apple Music exposes to UI Automation and may break after app updates.
