using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIsland.Core;

public static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder buffer, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int count);
    public delegate bool EnumWindowProc(IntPtr hwnd, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static string Title(IntPtr hwnd) { var b = new StringBuilder(512); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
    public static string Class(IntPtr hwnd) { var b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
    public static void Topmost(IntPtr hwnd) => SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
    public static void ClickThrough(IntPtr hwnd, bool enabled)
    {
        var ex = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, enabled ? ex | 0x20 : ex & ~0x20);
    }
    public static ImageSource? Icon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32,32));
            image.Freeze(); return image;
        }
        catch { return null; }
    }
    public static void FocusApp(string app)
    {
        if (app.Equals("Codex",StringComparison.OrdinalIgnoreCase))
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true }); return; } catch { }
        }
        var name = app.Contains("codex", StringComparison.OrdinalIgnoreCase) ? "Codex" :
            app.Contains("网易", StringComparison.OrdinalIgnoreCase) ? "cloudmusic" : app;
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p) { if (p.MainWindowHandle == IntPtr.Zero) continue; ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); return; }
            }
        }
        catch { }
    }
}

public sealed record ForegroundApp(int Pid, string Name, bool Fullscreen);
public sealed class GameDetector
{
    private IntPtr _lastWindow;
    private uint _lastPid;
    private string _name = "";
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "Codex", "chrome", "msedge", "firefox", "explorer", "ApplicationFrameHost", "vlc", "PotPlayerMini64", "mpv", "cloudmusic", "QQMusic", "dwm", "WindowsTerminal", "devenv", "Code", "ShellExperienceHost", "DesktopIsland" };
    public ForegroundApp GetForeground()
    {
        var hwnd = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (hwnd != _lastWindow||pid!=_lastPid)
        {
            _lastWindow = hwnd;_lastPid=pid;
            try { using var p = Process.GetProcessById((int)pid); _name = p.ProcessName; } catch { _name = ""; }
        }
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
        Native.GetWindowRect(hwnd, out var r);
        var full = r.Left <= screen.Left + 2 && r.Top <= screen.Top + 2 && r.Right >= screen.Right - 2 && r.Bottom >= screen.Bottom - 2;
        return new ForegroundApp((int)pid, _name, full);
    }
    public static bool IsGame(ForegroundApp app, IslandSettings settings)
    {
        if (app.Pid <= 0 || string.IsNullOrEmpty(app.Name)) return false;
        if (settings.GameProcesses.Any(n => string.Equals(System.IO.Path.GetFileNameWithoutExtension(n), app.Name, StringComparison.OrdinalIgnoreCase))) return true;
        return app.Fullscreen && !Excluded.Contains(app.Name) && !app.Name.Contains("browser", StringComparison.OrdinalIgnoreCase);
    }
}
