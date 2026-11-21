using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Media;

namespace DesktopIsland.Core;

public sealed record Notice(string App, string Title, string Body = "", string Id = "", DateTimeOffset? At = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public ImageSource? Icon { get; init; }
}
public sealed class NoticeHub
{
    public List<Notice> Items { get; } = [];
    private readonly HashSet<string> _seen = [];
    private readonly Queue<string> _seenOrder = [];
    public event Action? Changed;
    public void Add(Notice notice)
    {
        if (notice.Id.Length > 0 && !_seen.Add(notice.Id)) return;
        if (notice.Id.Length > 0) _seenOrder.Enqueue(notice.Id);
        while (_seenOrder.Count > 512) _seen.Remove(_seenOrder.Dequeue());
        Items.Insert(0, notice with { At = notice.At ?? DateTimeOffset.Now });
        if (Items.Count > 30) Items.RemoveRange(30, Items.Count - 30);
        Changed?.Invoke();
    }
    public void Dismiss(Notice notice) { Items.Remove(notice); Changed?.Invoke(); }
    public void Clear() { Items.Clear(); Changed?.Invoke(); }
}

public sealed class LocalBridge : IDisposable
{
    public static string PipeName => "DesktopIsland.Notifications.v1."+FpsProtocol.UserSid;
    private readonly CancellationTokenSource _stop = new();
    public event Action<Notice>? Received;
    public void Start() => _ = Task.Run(RunAsync);
    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); deadline.CancelAfter(3000);
                using var data = new MemoryStream();
                var buffer = new byte[2048];
                while (data.Length <= 32768)
                {
                    var read = await pipe.ReadAsync(buffer, deadline.Token);
                    if (read == 0) break;
                    data.Write(buffer, 0, read);
                }
                if (data.Length > 32768) continue;
                var n = JsonSerializer.Deserialize<Notice>(Encoding.UTF8.GetString(data.ToArray()), Storage.Json);
                if (n != null && !string.IsNullOrWhiteSpace(n.App) && !string.IsNullOrWhiteSpace(n.Title))
                    Received?.Invoke(new Notice(n.App[..Math.Min(80,n.App.Length)], n.Title[..Math.Min(200,n.Title.Length)], (n.Body ?? "")[..Math.Min(600,(n.Body ?? "").Length)], (n.Id ?? "")[..Math.Min(200,(n.Id ?? "").Length)]));
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Storage.Log("Local notification: " + e.GetType().Name); await Task.Delay(250); }
        }
    }
    public static async Task<bool> SendAsync(Notice notice)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(1500);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notice)));
            return true;
        }
        catch { return false; }
    }
    public void Dispose() { _stop.Cancel(); }
}
