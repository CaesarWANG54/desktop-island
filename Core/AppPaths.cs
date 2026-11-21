using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DesktopIsland.Core;

public static class AppPaths
{
    public static string InstalledDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","DesktopIsland");
    public static string Executable=>Path.Combine(AppContext.BaseDirectory,"DesktopIsland.exe");
    public static string CollectorDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DesktopIsland","FpsCollector",FpsProtocol.UserSid);
    public static string CollectorExecutable=>Path.Combine(CollectorDirectory,"DesktopIsland.FpsCollector.exe");
    public static bool CollectorConfigured
    {
        get
        {
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(CollectorDirectory,"client.json")));
                return File.Exists(CollectorExecutable)&&string.Equals(doc.RootElement.GetProperty("ClientPath").GetString(),Executable,StringComparison.OrdinalIgnoreCase)
                    &&doc.RootElement.TryGetProperty("Version",out var version)&&version.GetString()==typeof(AppPaths).Assembly.GetName().Version?.ToString(3);
            }
            catch{return false;}
        }
    }
    public static bool AutoStart
    {
        get{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");return key?.GetValue("DesktopIsland") is string;}
        set
        {
            using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if(value)key.SetValue("DesktopIsland",'"'+Executable+'"');else key.DeleteValue("DesktopIsland",false);
        }
    }
}
