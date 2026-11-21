using Windows.Media.Control;
using Windows.Storage.Streams;

// Adapted from kadrxy/Dynamic-Island, MIT. See licenses/Dynamic-Island-MIT.txt.
namespace DesktopIsland.Core;

public class MediaInfo
{
    public bool Active;
    public string Title = "";
    public string Artist = "";
    public string Album = "";
    public string Aumid = "";
    public bool Playing;
    public double Rate = 1;
    public string SongId = "";
    public double Pos;
    public double Dur;
    /// <summary>Time when SMTC last updated Pos. Live position equals Pos plus the
    /// elapsed time since PosAt; browsers do not continuously refresh Pos.</summary>
    public DateTime PosAt = DateTime.MinValue;
    public byte[]? Art;
    public DateTime Updated = DateTime.MinValue;
}

public class MediaService : IDisposable
{
    private readonly Func<string> _source;
    public MediaService(Func<string> source)=>_source=source;
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private string _artKey = "";
    private byte[]? _artCache;
    private System.Windows.Threading.DispatcherTimer? _timer;
    private int _polling;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly NeteaseAdapter _netease = new();
    public string Status { get; private set; } = "正在连接音乐";

    public MediaInfo Info { get; private set; } = new();
    public event Action<MediaInfo>? MediaUpdated;
    public event Action<string>? ControlFailed;
    private bool _commandBusy;

    public async Task InitAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception e) { Status = "系统媒体暂不可用"; Storage.Log("SMTC: " + e.GetType().Name); return; }
        Status = "等待播放音乐";

        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        await PollAsync();
    }

    private async Task PollAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        var info = new MediaInfo { Updated = DateTime.Now };
        try
        {
            var preferred=_source();
            var sessions = _mgr?.GetSessions().Where(s=>preferred switch {
                "qq"=>s.SourceAppUserModelId.Contains("QQMusic",StringComparison.OrdinalIgnoreCase),
                "netease"=>s.SourceAppUserModelId.Contains("cloudmusic",StringComparison.OrdinalIgnoreCase),
                _=>true}).ToArray();
            var s = sessions?.FirstOrDefault(x => x.SourceAppUserModelId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase)
                && x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? sessions?.FirstOrDefault(x => x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? sessions?.FirstOrDefault(x=>x==_mgr?.GetCurrentSession()) ?? sessions?.FirstOrDefault();
            _session = s;
            if (s != null)
            {
                var props = await s.TryGetMediaPropertiesAsync();
                var pb = s.GetPlaybackInfo();
                var tl = s.GetTimelineProperties();

                info.Active = !string.IsNullOrWhiteSpace(props.Title);
                info.Title = props.Title ?? "";
                info.Artist = props.Artist ?? "";
                info.Album = props.AlbumTitle ?? "";
                info.Aumid = s.SourceAppUserModelId ?? "";
                info.Playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                info.Rate = pb.PlaybackRate ?? 1;
                info.Pos = tl.Position.TotalSeconds;
                info.Dur = tl.EndTime.TotalSeconds;
                var lu = tl.LastUpdatedTime.LocalDateTime;
                info.PosAt = lu.Year > 2015 ? lu : info.Updated;

                var key = info.Aumid + "|" + info.Title + "|" + info.Artist;
                if (key != _artKey)
                {
                    _artKey = key;
                    _artCache = await ReadThumbAsync(props.Thumbnail);
                }
                info.Art = _artCache;
                Status = info.Aumid.Contains("QQMusic",StringComparison.OrdinalIgnoreCase)?"已连接 QQ 音乐":"已连接系统媒体";
            }
            else Status = "等待播放音乐";
            if (preferred!="qq"&&(s == null || !info.Playing))
            {
                var local = await Task.Run(_netease.Poll);
                if(local != null && (local.Playing || !info.Active)) { info=local;_session=null;Status="已连接网易云音乐"; }
            }
        }
        catch { Status="音乐连接暂不可用，等待播放器恢复"; }

        Interlocked.Exchange(ref _polling, 0);
        Info = info;
        MediaUpdated?.Invoke(info);
    }

    private static async Task<byte[]?> ReadThumbAsync(IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return null;
        try
        {
            using var ras = await thumb.OpenReadAsync();
            using var reader = new DataReader(ras.GetInputStreamAt(0));
            var size = (uint)ras.Size;
            if (size == 0 || size > 4_000_000) return null;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch { return null; }
    }

    public void PlayPause()=>_ = ControlAsync(14);
    public void Next()=>_ = ControlAsync(11);
    public void Prev()=>_ = ControlAsync(12);
    private async Task ControlAsync(int command)
    {
        if(_commandBusy)return;
        _commandBusy=true;
        try
        {
            var s=_session;
            if(s==null&&Info.Aumid=="cloudmusic.local"&&_mgr?.GetSessions().Any(x=>!x.SourceAppUserModelId.Contains("cloudmusic",StringComparison.OrdinalIgnoreCase))==true)
            {ControlFailed?.Invoke("网易云当前使用系统媒体按键，请先关闭其他音乐播放器再操作。");return;}
            bool accepted=s!=null ? command switch {14=>await s.TryTogglePlayPauseAsync(),11=>await s.TrySkipNextAsync(),_=>await s.TrySkipPreviousAsync()}
                : Info.Aumid=="cloudmusic.local"&&_netease.Command(command);
            if(!accepted)ControlFailed?.Invoke("音乐软件未接收控制，请确认播放器正在运行");
            await Task.Delay(350);
            await PollAsync();
        }
        catch(Exception e) { Storage.Log("Media control: "+e.GetType().Name);ControlFailed?.Invoke("音乐控制暂不可用"); }
        finally {_commandBusy=false;}
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        _mgr = null;
    }
    public double Position
    {
        get
        {
            var value = Info.Pos + (Info.Playing ? Math.Max(0, (DateTime.Now - Info.PosAt).TotalSeconds) * Info.Rate : 0);
            return Info.Dur > 0 ? Math.Clamp(value, 0, Info.Dur) : Math.Max(0, value);
        }
    }
}
