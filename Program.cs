using System.IO;
using System.Text.Json;
using System.Windows;
using DesktopIsland.Core;
using DesktopIsland.UI;

namespace DesktopIsland;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Contains("--install"))
        {
            var setup=new Application();
            setup.Startup+=async (_,_)=>
            {
                try{var path=await AppInstaller.InstallAsync();System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});}
                catch(Exception e){MessageBox.Show("安装未完成。请完整解压下载包、退出 Desktop Island 后重试。\n"+e.Message,"Desktop Island");}
                setup.Shutdown();
            };
            return setup.Run();
        }
        if (args.Length >= 2 && args[0] == "--control")
            return LocalBridge.SendAsync(new Notice("__island_control", args[1])).GetAwaiter().GetResult() ? 0 : 2;
        if (args.Contains("--toast-probe")&&args.Contains("--diagnostics"))
        {
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>灵动岛通知测试</text><text>本机通知通道验证</text></binding></visual></toast>");
            Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier("DesktopIsland").Show(new Windows.UI.Notifications.ToastNotification(xml));
            return 0;
        }
        if (args.Contains("--probe")&&args.Contains("--diagnostics"))
        {
            var app = new Application();
            app.Startup += async (_, _) =>
            {
                var output = new Dictionary<string, object?>();
                try {
                    var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                    output["notificationAccess"] = listener.GetAccessStatus().ToString();
                    var notifications = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
                    output["notificationCount"] = notifications.Count;
                }
                catch (Exception e) { output["notificationError"] = e.ToString(); }
                try
                {
                    var m = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    output["mediaSessions"] = m.GetSessions().Select(s => s.SourceAppUserModelId).ToArray();
                }
                catch (Exception e) { output["mediaError"] = e.Message; }
                Storage.Write("probe.json", output); app.Shutdown();
            };
            return app.Run();
        }
        if (args.Length >= 3 && args[0] == "--notify")
            return LocalBridge.SendAsync(new Notice(args[1], args[2], args.Length > 3 ? args[3] : "")).GetAwaiter().GetResult() ? 0 : 2;
        if (args.Length >= 2 && args[0] == "--notify-json")
        {
            try
            {
                using var doc = JsonDocument.Parse(args[1]);
                var r = doc.RootElement;
                if (r.TryGetProperty("type", out var t) && t.GetString() == "agent-turn-complete")
                    return LocalBridge.SendAsync(new Notice("Codex", "任务已完成", "点击回到 Codex")).GetAwaiter().GetResult() ? 0 : 2;
                var notice = JsonSerializer.Deserialize<Notice>(args[1], Storage.Json);
                return notice != null && LocalBridge.SendAsync(notice).GetAwaiter().GetResult() ? 0 : 2;
            }
            catch { return 2; }
        }
        using var mutex = new Mutex(true, @"Local\DesktopIsland.Singleton.v1."+FpsProtocol.UserSid, out var first);
        if (!first) return 0;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) => { Storage.Log("Unhandled: " + e.Exception); application.Shutdown(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Storage.Log("Fatal: " + e.ExceptionObject);
        application.Startup += (_, _) =>
        {
            var window = new IslandWindow(args.Contains("--diagnostics"));
            application.MainWindow=window; window.Show();
        };
        return application.Run();
    }
}
