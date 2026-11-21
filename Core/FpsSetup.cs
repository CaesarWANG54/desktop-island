using System.Diagnostics;
using System.IO;

namespace DesktopIsland.Core;

public static class FpsSetup
{
    public static async Task<string?> InstallAsync()
    {
        var script=Path.Combine(AppContext.BaseDirectory,"scripts","install-fps-task.ps1");
        var source=Path.Combine(AppContext.BaseDirectory,"collector");
        if(!File.Exists(script)||!File.Exists(Path.Combine(source,"sha256.json")))return "当前目录缺少帧率组件，请完整解压下载包后重试。";
        try
        {
            var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"))
                {UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-Source",source,"-UserSid",FpsProtocol.UserSid,"-ClientPath",AppPaths.Executable})info.ArgumentList.Add(arg);
            using var process=Process.Start(info)!;
            await process.WaitForExitAsync();
            return process.ExitCode==0&&AppPaths.CollectorConfigured?null:"帧率配置未完成。请使用当前 Windows 管理员账户重试；其他功能仍可使用。";
        }
        catch(System.ComponentModel.Win32Exception e) when(e.NativeErrorCode==1223){return "已取消帧率授权，稍后可在设置中启用。";}
        catch(Exception e){Storage.Log("FPS setup: "+e.GetType().Name);return "帧率配置暂不可用，请完整解压后重试。";}
    }
}
