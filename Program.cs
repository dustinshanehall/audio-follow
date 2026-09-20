using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioFollow;

record Target(string Name, string[] Processes, string Launch, string? Resume = null);
record Config(
    int DebounceMs,
    Target[] Targets,
    bool ResumeAlbum = false,
    bool ResumePlayback = false,
    string Storefront = "us",
    bool Debug = false,
    bool StartWithWindows = true);

sealed class Watcher : IMMNotificationClient, IDisposable
{
    readonly MMDeviceEnumerator _enumerator = new();
    volatile Config _config;
    readonly System.Threading.Timer _debounce;
    readonly object _gate = new();
    public volatile bool Paused;

    public Config Config => _config;
    public void Update(Func<Config, Config> change) => _config = change(_config);

    public Watcher(Config config)
    {
        _config = config;
        _debounce = new System.Threading.Timer(_ => Restart(), null, Timeout.Infinite, Timeout.Infinite);
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || role != Role.Multimedia || Paused) return;
        _debounce.Change(_config.DebounceMs, Timeout.Infinite);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    void Restart()
    {
        lock (_gate)
        {
            foreach (var target in _config.Targets)
            {
                var running = target.Processes
                    .SelectMany(p => Process.GetProcessesByName(p))
                    .ToList();
                if (running.Count == 0) continue;

                NowPlaying? nowPlaying = null;
                if ((_config.ResumeAlbum || _config.ResumePlayback) && target.Resume == "appleMusic")
                {
                    nowPlaying = AppleMusicResume.Capture();
                    Log.Write(_config, $"capture: {(nowPlaying == null ? "null" : $"'{nowPlaying.Title}' | '{nowPlaying.ArtistAlbum}' | playing={nowPlaying.WasPlaying}")}");
                }

                foreach (var p in running)
                {
                    try { p.CloseMainWindow(); } catch { }
                }
                Thread.Sleep(1500);
                foreach (var p in running)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    p.Dispose();
                }
                Thread.Sleep(500);

                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", target.Launch) { UseShellExecute = false });
                }
                catch { }

                if (nowPlaying != null) AppleMusicResume.Resume(nowPlaying, _config);
            }
        }
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _debounce.Dispose();
        _enumerator.Dispose();
    }
}

static class Program
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "audio-follow";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\audio-follow", out bool isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();

        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        Config config;
        try
        {
            config = JsonSerializer.Deserialize<Config>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read {configPath}:\n{ex.Message}", AppName);
            return;
        }

        ApplyStartupSetting(config.StartWithWindows);

        using var watcher = new Watcher(config);

        var pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) => watcher.Paused = pauseItem.Checked;

        var startupItem = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
        startupItem.Click += (_, _) =>
        {
            var enable = !startupItem.Checked;
            SetStartup(enable);
            startupItem.Checked = IsStartupEnabled();
            SaveSetting(configPath, "startWithWindows", enable);
        };

        var resumeOff = new ToolStripMenuItem("Off");
        var resumeAlbum = new ToolStripMenuItem("Reopen album");
        var resumePlay = new ToolStripMenuItem("Reopen album and play");
        var resumeItems = new[] { resumeOff, resumeAlbum, resumePlay };

        void ShowResumeMode()
        {
            var c = watcher.Config;
            resumeOff.Checked = !c.ResumeAlbum && !c.ResumePlayback;
            resumePlay.Checked = c.ResumePlayback;
            resumeAlbum.Checked = c.ResumeAlbum && !c.ResumePlayback;
        }

        void SetResumeMode(bool album, bool play)
        {
            watcher.Update(c => c with { ResumeAlbum = album, ResumePlayback = play });
            SaveSetting(configPath, "resumeAlbum", album);
            SaveSetting(configPath, "resumePlayback", play);
            ShowResumeMode();
        }

        resumeOff.Click += (_, _) => SetResumeMode(false, false);
        resumeAlbum.Click += (_, _) => SetResumeMode(true, false);
        resumePlay.Click += (_, _) => SetResumeMode(true, true);
        ShowResumeMode();

        var resumeMenu = new ToolStripMenuItem("Resume after switch (Apple Music)");
        resumeMenu.DropDownItems.AddRange(resumeItems);

        var openConfigItem = new ToolStripMenuItem("Open config file");
        openConfigItem.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true }); }
            catch { }
        };

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Exit();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([pauseItem, resumeMenu, startupItem, openConfigItem, new ToolStripSeparator(), exitItem]);

        using var tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = AppName,
            ContextMenuStrip = menu,
            Visible = true
        };

        Application.Run();
        tray.Visible = false;
    }

    static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("app.ico");
            if (stream != null)
                return new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    // Applies the config setting at launch without clobbering a working registration
    // that points at another copy (e.g. the installed one while running a dev build).
    static void ApplyStartupSetting(bool enable)
    {
        if (!enable) { SetStartup(false); return; }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var existing = (key?.GetValue(AppName) as string)?.Trim('"');
        if (existing != null && File.Exists(existing)) return;
        SetStartup(true);
    }

    static void SaveSetting(string configPath, string key, bool value)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
            node[key] = value;
            File.WriteAllText(configPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enable) key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
