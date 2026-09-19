using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioFollow;

record Target(string Name, string[] Processes, string Launch);
record Config(int DebounceMs, Target[] Targets);

sealed class Watcher : IMMNotificationClient, IDisposable
{
    readonly MMDeviceEnumerator _enumerator = new();
    readonly Config _config;
    readonly System.Threading.Timer _debounce;
    readonly object _gate = new();
    public volatile bool Paused;

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

        using var watcher = new Watcher(config);

        var pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) => watcher.Paused = pauseItem.Checked;

        var startupItem = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
        startupItem.Click += (_, _) =>
        {
            SetStartup(!startupItem.Checked);
            startupItem.Checked = IsStartupEnabled();
        };

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Exit();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([pauseItem, startupItem, new ToolStripSeparator(), exitItem]);

        using var tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = AppName,
            ContextMenuStrip = menu,
            Visible = true
        };

        Application.Run();
        tray.Visible = false;
    }

    static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enable) key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
