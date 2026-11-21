using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DesktopIsland.Core;

// Only consumes new event markers. No historical chat bodies are loaded or stored.
public sealed class CodexWatcher : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Offset, string Pending)> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private bool _running;
    public string Status { get; private set; } = "未连接";
    public event Action<Notice>? Completed;
    public void Start(string home)
    {
        if(_running){if(_watcher!=null)_watcher.EnableRaisingEvents=true;Status="已连接 Codex";return;}
        var root = Path.Combine(home, "sessions");
        if (!Directory.Exists(root)) { Status = "未找到 Codex 会话目录"; return; }
        _watcher = new FileSystemWatcher(root, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 32768 };
        _watcher.Changed += (_, e) => _pending[e.FullPath] = 0;
        _watcher.Created += (_, e) => _pending[e.FullPath] = 0;
        _watcher.Error += (_, _) => { Status = "监听恢复中"; QueueRecent(root); };
        // Start tracking at EOF, without replaying old completed tasks.
        foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
            try { _offsets[path] = (new FileInfo(path).Length, ""); } catch { }
        _watcher.EnableRaisingEvents = true;
        _running=true;
        Status = "已连接 Codex";
        _ = Task.Run(RunAsync);
    }
    public void SetEnabled(bool enabled,string home)
    {
        if(enabled)Start(home);
        else{if(_watcher!=null)_watcher.EnableRaisingEvents=false;_pending.Clear();Status="Codex 提醒已关闭";}
    }
    private void QueueRecent(string root)
    {
        try { foreach (var p in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)) if (File.GetLastWriteTimeUtc(p) > DateTime.UtcNow.AddMinutes(-2)) _pending[p] = 0; } catch { }
    }
    private async Task RunAsync()
    {
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await tick.WaitForNextTickAsync(_stop.Token))
                foreach (var path in _pending.Keys)
                {
                    if (!_pending.TryRemove(path, out _)) continue;
                    try { ReadAppend(path); } catch (IOException) { _pending[path] = 0; } catch (Exception e) { Storage.Log("Codex watcher: " + e.GetType().Name); }
                }
        }
        catch (OperationCanceledException) { }
    }
    private void ReadAppend(string path)
    {
        var cursor = _offsets.GetValueOrDefault(path, (0L, ""));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < cursor.Item1) cursor = (0, "");
        // A burst of tool output must not monopolize memory or the UI thread.
        if (stream.Length - cursor.Item1 > 8_000_000) cursor = (stream.Length - 8_000_000, "");
        stream.Seek(cursor.Item1, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = cursor.Item2 + reader.ReadToEnd();
        var end = text.LastIndexOf('\n');
        if (end < 0) { _offsets[path] = (stream.Position, text.Length > 2_000_000 ? "" : text); return; }
        _offsets[path] = (stream.Position, text[(end + 1)..]);
        foreach (var line in text[..end].Split('\n'))
        {
            if (!line.Contains("task_complete", StringComparison.Ordinal)) continue;
            var notice = ParseCompletion(line, path);
            if (notice != null) Completed?.Invoke(notice);
        }
    }
    public static Notice? ParseCompletion(string line, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.GetProperty("type").GetString() != "event_msg") return null;
            var payload = root.GetProperty("payload");
            if (payload.GetProperty("type").GetString() != "task_complete") return null;
            string turn = payload.TryGetProperty("turn_id", out var t) ? t.GetString() ?? "" : "";
            string stamp = root.TryGetProperty("timestamp", out var ts) ? ts.ToString() : "";
            return new Notice("Codex", "任务已完成", "点击回到 Codex", "codex:" + Path.GetFileName(path) + ":" + (turn.Length > 0 ? turn : stamp));
        }
        catch { return null; }
    }
    public void Dispose() { _stop.Cancel(); _watcher?.Dispose(); }
}
