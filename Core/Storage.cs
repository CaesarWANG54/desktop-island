using System.IO;
using System.Text.Json;

namespace DesktopIsland.Core;

public static class Storage
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopIsland");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static T Read<T>(string name, T fallback)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(Root, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, Json) ?? fallback;
        }
        catch { return fallback; }
    }
    public static void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            if(File.Exists(path))File.Replace(temporary,path,null,true);
            else File.Move(temporary,path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    // Live telemetry can miss one sample; an unavailable file must never close the island.
    public static bool TryWrite<T>(string name, T value)
    {
        try { Write(name, value); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}\n");
        }
        catch { }
    }
}

public class IslandSettings
{
    public bool SetupCompleted { get; set; }
    public string MusicSource { get; set; } = "auto";
    public bool FpsEnabled { get; set; }
    public bool CodexEnabled { get; set; }
    public double TopOffset { get; set; } = 12;
    public bool AutomaticGameMode { get; set; } = true;
    public bool GameClickThrough { get; set; } = true;
    public bool NotificationsEnabled { get; set; }
    public bool TimerSound { get; set; } = true;
    public bool OnlineLyrics { get; set; } = true;
    public double LyricOffsetSeconds { get; set; }
    public int HoverDelayMs { get; set; } = 200;
    public List<WeatherPlace> WeatherPlaces { get; set; } = [];
    public int SelectedWeatherPlace { get; set; }
    public string CodexHome { get; set; } = Environment.GetEnvironmentVariable("CODEX_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public List<string> GameProcesses { get; set; } = ["CrimsonDesert", "CrimsonDesert-Win64-Shipping", "BlackDesert64", "Cyberpunk2077", "eldenring", "b1-Win64-Shipping", "GTA5", "rdr2", "cs2", "VALORANT-Win64-Shipping", "Overwatch", "League of Legends", "StarRail", "YuanShen"];
}
