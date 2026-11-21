using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DesktopIsland.Core;

public static class AppInstaller
{
    public static async Task<string> InstallAsync()
    {
        string source=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string destination=AppPaths.InstalledDirectory;
        string executable=Path.Combine(destination,"DesktopIsland.exe");
        if(!source.Equals(destination,StringComparison.OrdinalIgnoreCase))
        {
            var names=JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(Path.Combine(source,"app-files.json")))??throw new InvalidDataException("Missing package manifest");
            await LocalBridge.SendAsync(new Notice("__island_control","quit"));
            foreach(var process in Process.GetProcessesByName("DesktopIsland"))
                using(process)if(process.Id!=Environment.ProcessId&&!process.WaitForExit(5000))throw new IOException("请先退出正在运行的 Desktop Island。 ");
            Directory.CreateDirectory(destination);
            foreach(var name in names.Append("app-files.json"))
            {
                var from=SafeChild(source,name);var to=SafeChild(destination,name);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from,to,true);
            }
        }
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Programs)})
        {
            var helper=new ProcessStartInfo(Path.Combine(destination,"tools","DesktopIsland.Shortcut.exe")){UseShellExecute=false,CreateNoWindow=true};
            helper.ArgumentList.Add(executable);helper.ArgumentList.Add(Path.Combine(folder,"Desktop Island.lnk"));
            using var link=Process.Start(helper)!;await link.WaitForExitAsync();if(link.ExitCode!=0)throw new IOException("无法创建快捷方式");
        }
        using(var app=Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\DesktopIsland"))
        {app.SetValue("DisplayName","Desktop Island");app.SetValue("IconUri",executable);}
        using(var uninstall=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopIsland"))
        {
            uninstall.SetValue("DisplayName","Desktop Island");uninstall.SetValue("DisplayVersion",typeof(AppInstaller).Assembly.GetName().Version?.ToString(3)??"1.3.0");
            uninstall.SetValue("Publisher","Desktop Island contributors");uninstall.SetValue("DisplayIcon",executable);uninstall.SetValue("InstallLocation",destination);
            uninstall.SetValue("UninstallString",'"'+Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe")+"\" -NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(destination,"scripts","uninstall.ps1")+'"');
            uninstall.SetValue("NoModify",1,RegistryValueKind.DWord);uninstall.SetValue("NoRepair",1,RegistryValueKind.DWord);
        }
        if(AppPaths.AutoStart){using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");run.SetValue("DesktopIsland",'"'+executable+'"');}
        return executable;
    }
    public static string SafeChild(string root,string name)
    {
        var path=Path.GetFullPath(Path.Combine(root,name));
        if(Path.IsPathRooted(name)||!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid package path");
        return path;
    }
}
