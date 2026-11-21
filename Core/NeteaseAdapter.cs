// Elog byte transform and playback marker semantics adapted from
// Coooookies/netease-cloudmusic-detector (MIT, copyright 2025 ButterCookies).
// See licenses/netease-cloudmusic-detector-MIT.txt.
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopIsland.Core;

public sealed class NeteaseAdapter
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetEase", "CloudMusic", "cloudmusic.elog");
    private long _offset = -1;
    private string _tail = "";
    private int _pid;
    private DateTime _lastFind = DateTime.MinValue;
    private DateTime _lastMetadataRetry = DateTime.MinValue;
    private MediaInfo _info = new();
    private string _cover = "";
    public string Cover => _cover;
    private static readonly Regex Header = new(@"^\[(\d+):\d+:\d{4}/\d{6}:(\d+):",RegexOptions.Compiled);
    private static readonly Regex Position = new("setPlayingPosition\",([0-9.]+)",RegexOptions.Compiled);
    private static readonly Regex Playback = new("native播放state\",(\\d+)",RegexOptions.Compiled);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] inputs,int size);
    public bool Command(int command)
    {
        // CloudMusic 3.x ignores WM_APPCOMMAND, but handles Windows media keys.
        ushort key=command switch {14=>0xB3,11=>0xB0,12=>0xB1,_=>0};
        if(_pid==0||key==0)return false;
        var down=new Input {Type=1,Data=new InputData {Keyboard=new KeyboardInput {Key=key}}};
        var up=new Input {Type=1,Data=new InputData {Keyboard=new KeyboardInput {Key=key,Flags=2}}};
        return SendInput(2,[down,up],Marshal.SizeOf<Input>())==2;
    }
    public MediaInfo? Poll()
    {
        if ((DateTime.Now-_lastFind).TotalSeconds>3)
        {
            _lastFind=DateTime.Now;
            var processes=Process.GetProcessesByName("cloudmusic");
            try
            {
                // The main process starts before its Chromium children, including
                // when its window is hidden in the tray.
                var main=processes.OrderBy(p=>p.StartTime).FirstOrDefault();var next=main?.Id??0;
                if(next!=_pid){_offset=-1;_tail="";_info=new();}
                _pid=next;
            }
            catch {_pid=0;}
            finally {foreach(var p in processes)p.Dispose();}
        }
        if (_pid==0 || !File.Exists(_path)) { _info=new();_offset=-1;return null; }
        try
        {
            using var stream=new FileStream(_path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if (_offset<0 || stream.Length<_offset) { _offset=Math.Max(0,stream.Length-2_000_000);_tail="";_info=new(); }
            if(stream.Length>_offset)
            {
                if(stream.Length-_offset>2_000_000){_offset=stream.Length-2_000_000;_tail="";}
                stream.Position=_offset;var bytes=new byte[stream.Length-_offset];var count=stream.Read(bytes,0,bytes.Length);_offset=stream.Position;
                for(int i=0;i<count;i++) { int b=bytes[i];bytes[i]=(byte)((((b/16)^((b%16)+8))%16)*16+(b/64)*4+(~(b/16)&3)); }
                var text=_tail+Encoding.UTF8.GetString(bytes,0,count);int end=text.LastIndexOf('\n');
                if(end>=0) {foreach(var row in text[..end].Split('\n'))Parse(row);_tail=text[(end+1)..];}else _tail=text;
                if(_tail.Length>1_000_000)_tail="";
            }
            if(!_info.Active)
            {
                // Client startup/rotation can hide the first metadata record from
                // an incremental read. Retry a bounded tail, never the whole log.
                if((DateTime.Now-_lastMetadataRetry).TotalSeconds>=10){_offset=-1;_lastMetadataRetry=DateTime.Now;}
                return null;
            }
            return new MediaInfo {Active=true,Title=_info.Title,Artist=_info.Artist,Aumid="cloudmusic.local",Playing=_info.Playing,Pos=_info.Pos,Dur=_info.Dur,PosAt=_info.PosAt,Updated=DateTime.Now,SongId=_info.SongId,Art=_info.Art};
        }
        catch(IOException){return null;}
    }
    private void Parse(string row)
    {
        if(!row.Contains("【playing】",StringComparison.Ordinal))return;
        var header=Header.Match(row);
        if(!header.Success || !int.TryParse(header.Groups[1].Value,out var pid) || pid!=_pid || !long.TryParse(header.Groups[2].Value,out var tick))return;
        var age=Environment.TickCount64-tick;
        if(age< -5000 || age>86400*1000L)return;
        var at=DateTime.Now.AddMilliseconds(-age);
        try
        {
            if(row.Contains("checkPlayPrivilege") || row.Contains("playOneTrackInPlayingList"))
            {
                using var doc=JsonDocument.Parse(row[row.IndexOf('{')..(row.LastIndexOf('}')+1)]);
                var track=doc.RootElement; if(track.TryGetProperty("track",out var t))track=t;
                var id=track.GetProperty("id").ToString();
                if(id!=_info.SongId) { _info.Pos=0;_info.PosAt=at;_info.Playing=false; }
                _info.SongId=id; _info.Title=track.GetProperty("name").GetString()??"";
                _info.Artist=string.Join(" / ",track.GetProperty("artists").EnumerateArray().Select(a=>a.GetProperty("name").GetString()));
                _info.Dur=track.GetProperty("duration").GetDouble()/1000;
                _info.Active=!string.IsNullOrWhiteSpace(_info.Title);
                if(track.TryGetProperty("album",out var album)&&album.TryGetProperty("cover",out var cover))_cover=cover.GetString()??"";
            }
            else if(row.Contains("native播放资源load完成")) { _info.Pos=0;_info.PosAt=at;_info.Playing=true; }
            else if(Position.Match(row) is {Success:true} pos) { _info.Pos=double.Parse(pos.Groups[1].Value,CultureInfo.InvariantCulture);_info.PosAt=at; }
            else if(Playback.Match(row) is {Success:true} state)
            {
                var playing=state.Groups[1].Value=="1";
                if(_info.Playing)_info.Pos=Math.Min(_info.Dur,_info.Pos+Math.Max(0,(at-_info.PosAt).TotalSeconds));
                _info.PosAt=at;_info.Playing=playing;
            }
        }
        catch { /* incomplete write / unknown client version: await next complete marker */ }
    }
}
