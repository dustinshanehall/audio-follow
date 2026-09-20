using System.IO;

namespace AudioFollow;

static class Log
{
    static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "audio-follow.log");

    public static void Write(Config config, string message)
    {
        if (!config.Debug) return;
        try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch { }
    }
}
