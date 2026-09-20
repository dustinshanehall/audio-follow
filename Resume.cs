using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Media.Control;

namespace AudioFollow;

record NowPlaying(string Title, string ArtistAlbum, bool WasPlaying);

static class AppleMusicResume
{
    const string AumidPrefix = "AppleInc.AppleMusicWin_";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static NowPlaying? Capture()
    {
        try
        {
            var mgr = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
            var session = mgr.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId.StartsWith(AumidPrefix, StringComparison.OrdinalIgnoreCase));
            if (session == null) return null;

            var status = session.GetPlaybackInfo().PlaybackStatus;
            if (status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) return null;

            var props = session.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(props.Title)) return null;

            var artistAlbum = Regex.Replace(props.Artist ?? "", @"\s[—–]\s", " - ");
            return new NowPlaying(props.Title, artistAlbum,
                status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        }
        catch { return null; }
    }

    public static void Resume(NowPlaying np, Config config)
    {
        try
        {
            var window = WaitForWindow(TimeSpan.FromSeconds(25));
            Log.Write(config, $"window: {(window == null ? "not found" : "found")}");
            if (window == null) return;

            var url = ResolveUrl(np, config.Storefront);
            Log.Write(config, $"url: {url ?? "no match"}");
            if (url == null) return;

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Log.Write(config, "link opened");

            if (config.ResumePlayback && np.WasPlaying)
                Log.Write(config, $"play: {(PlayTrack(np.Title) ? "clicked" : "row not found")}");
        }
        catch (Exception ex) { Log.Write(config, $"error: {ex}"); }
    }

    static string? ResolveUrl(NowPlaying np, string storefront)
    {
        // The search endpoint ranks unpredictably and can omit tracks, so find the album
        // first and then read the album's full track list.
        foreach (var (artist, album) in SplitArtistAlbum(np.ArtistAlbum))
        {
            var collectionId = FindCollectionId(artist, album, storefront);
            if (collectionId == null) continue;

            foreach (var r in Query($"lookup?id={collectionId}&entity=song&country={storefront}"))
            {
                if (r.GetProperty("wrapperType").GetString() != "track") continue;
                var name = r.GetProperty("trackName").GetString() ?? "";
                if (!name.Equals(np.Title, StringComparison.OrdinalIgnoreCase)) continue;
                var link = r.GetProperty("trackViewUrl").GetString();
                if (link != null) return ToMusicScheme(link);
            }
        }
        return null;
    }

    static IEnumerable<(string Artist, string Album)> SplitArtistAlbum(string artistAlbum)
    {
        const string sep = " - ";
        for (var i = artistAlbum.IndexOf(sep, StringComparison.Ordinal); i >= 0;
             i = artistAlbum.IndexOf(sep, i + 1, StringComparison.Ordinal))
            yield return (artistAlbum[..i], artistAlbum[(i + sep.Length)..]);
    }

    static long? FindCollectionId(string artist, string album, string storefront)
    {
        var queries = new[]
        {
            $"search?term={Uri.EscapeDataString(album)}&entity=song&limit=200&country={storefront}",
            $"search?term={Uri.EscapeDataString(artist)}&entity=album&attribute=artistTerm&limit=200&country={storefront}",
        };
        foreach (var q in queries)
        {
            foreach (var r in Query(q))
            {
                if (!r.TryGetProperty("collectionName", out var c) || !r.TryGetProperty("artistName", out var a)) continue;
                if (!string.Equals(c.GetString(), album, StringComparison.OrdinalIgnoreCase)) continue;
                var artistName = a.GetString() ?? "";
                if (!artist.StartsWith(artistName, StringComparison.OrdinalIgnoreCase) &&
                    !artistName.StartsWith(artist, StringComparison.OrdinalIgnoreCase)) continue;
                if (r.TryGetProperty("collectionId", out var id)) return id.GetInt64();
            }
        }
        return null;
    }

    static List<JsonElement> Query(string path)
    {
        using var doc = JsonDocument.Parse(Http.GetStringAsync("https://itunes.apple.com/" + path).GetAwaiter().GetResult());
        return doc.RootElement.GetProperty("results").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    static string ToMusicScheme(string httpsUrl) =>
        Regex.Replace(Regex.Replace(httpsUrl, "^https://", "music://"), @"&uo=\d+", "");

    static AutomationElement? WaitForWindow(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var proc = Process.GetProcessesByName("AppleMusic").FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (proc != null)
            {
                Thread.Sleep(1500);
                return AutomationElement.FromHandle(proc.MainWindowHandle);
            }
            Thread.Sleep(500);
        }
        return null;
    }

    static bool PlayTrack(string title)
    {
        var proc = Process.GetProcessesByName("AppleMusic").FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (proc == null) return false;
        var window = AutomationElement.FromHandle(proc.MainWindowHandle);

        var rowName = new Regex($"^Track \\d+ {Regex.Escape(title)} ", RegexOptions.IgnoreCase);
        var listItems = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);

        AutomationElement? row = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline && row == null)
        {
            row = window.FindAll(TreeScope.Descendants, listItems)
                .Cast<AutomationElement>()
                .FirstOrDefault(e => rowName.IsMatch(e.Current.Name));
            if (row == null) Thread.Sleep(500);
        }
        if (row == null) return false;

        if (row.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll))
            ((ScrollItemPattern)scroll).ScrollIntoView();
        Thread.Sleep(400);

        var rect = row.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Height <= 0) return false;
        DoubleClick(proc.MainWindowHandle, (int)(rect.X + 200), (int)(rect.Y + rect.Height / 2));
        return true;
    }

    static void DoubleClick(IntPtr hwnd, int x, int y)
    {
        GetCursorPos(out var old);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        Thread.Sleep(300);

        SetCursorPos(x, y);
        Thread.Sleep(150);
        for (var i = 0; i < 2; i++)
        {
            mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(80);
        }
        Thread.Sleep(300);
        SetCursorPos(old.X, old.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
}
