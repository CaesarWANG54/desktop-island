// Adapted from kadrxy/Dynamic-Island's MIT notification-listener flow.
using System.Windows.Media.Imaging;
using System.IO;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DesktopIsland.Core;

public sealed class NotificationService : IDisposable
{
    private UserNotificationListener? _listener;
    private readonly HashSet<uint> _seen = [];
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _busy;
    private bool _primed;
    public string Status { get; private set; } = "正在连接通知";
    public event Action<Notice>? Received;
    public async Task StartAsync(bool request = false)
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = request ? await _listener.RequestAccessAsync() : _listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed) { Status = "需要允许访问 Windows 通知"; return; }
            if (!_timer.IsEnabled) { _timer.Tick -= OnTick; _timer.Tick += OnTick; _timer.Start(); }
            await PollAsync();
        }
        catch (Exception e) { Status = "Windows 通知暂不可用"; Storage.Log("Notification init: " + e.GetType().Name); }
    }
    private async void OnTick(object? sender, EventArgs e) => await PollAsync();
    private async Task PollAsync()
    {
        if (_busy || _listener == null) return;
        _busy = true;
        try
        {
            if (_listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { Status = "通知访问未授权"; return; }
            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            Status = "已连接 Windows 通知";
            if (!_primed) { foreach (var n in notifications) _seen.Add(n.Id); _primed = true; return; }
            foreach (var n in notifications)
            {
                if (!_seen.Add(n.Id)) continue;
                try
                {
                    var texts = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    if (texts == null || texts.Count == 0) continue;
                    var title = texts[0].Text;
                    var body = string.Join(" · ", texts.Skip(1).Select(t => t.Text));
                    var app = n.AppInfo.DisplayInfo.DisplayName;
                    // Codex has a direct completion signal; avoid duplicating its system banner.
                    if (app.Contains("Codex", StringComparison.OrdinalIgnoreCase)) continue;
                    var icon = await LoadIcon(n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(32,32)));
                    Received?.Invoke(new Notice(app, title, body, "windows:" + n.Id, n.CreationTime) { Icon = icon });
                }
                catch (Exception e) { Storage.Log("Notification read: " + e.GetType().Name); }
            }
            if (_seen.Count > 512) { var current = notifications.Select(n => n.Id).ToHashSet(); _seen.IntersectWith(current); }
        }
        catch (Exception e) { Status = "Windows 通知暂不可用"; Storage.Log("Notification poll: " + e.GetType().Name); }
        finally { _busy = false; }
    }
    private static async Task<BitmapImage?> LoadIcon(RandomAccessStreamReference stream)
    {
        try
        {
            using var input = await stream.OpenReadAsync();
            if (input.Size > 2_000_000) return null;
            using var reader = new DataReader(input.GetInputStreamAt(0));
            await reader.LoadAsync((uint)input.Size); var bytes = new byte[(int)input.Size]; reader.ReadBytes(bytes);
            using var memory = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = memory; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        catch { return null; }
    }
    public void Stop(){_timer.Stop();_primed=false;_seen.Clear();Status="系统通知已关闭";}
    public void Dispose() { _timer.Stop(); }
}
