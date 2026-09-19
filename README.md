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

## Build and run

Requires the .NET 10 SDK.

```
dotnet build -c Release
bin\Release\net10.0-windows\audio-follow.exe
```

The tray icon menu offers Pause, Start with Windows, and Exit.

## Limitations

- The app is relaunched, not resumed: the queue and playback position are lost.
- It reacts to the default playback device (Multimedia role) only.
