using DesktopIsland.Core;
using System.Text.Json;
using System.IO;

int checks=0;
void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
var now=DateTimeOffset.Parse("2025-01-15T00:00:00Z");
var timer=new TimerEngine(new(),()=>now);int alarms=0;timer.Elapsed+=()=>alarms++;
timer.StartCountdown(10);now=now.AddSeconds(3);timer.PauseCountdown();now=now.AddHours(1);
Check(timer.Remaining==7,"pause freezes remaining time");timer.ResumeCountdown();now=now.AddSeconds(8);timer.Tick();timer.Tick();
Check(alarms==1&&timer.State.Alarm,"completion fires once after sleep");
timer.StartCountdown(60);var persisted=JsonSerializer.Deserialize<TimerState>(JsonSerializer.Serialize(timer.State))!;now=now.AddSeconds(20);
Check(new TimerEngine(persisted,()=>now).Remaining==40,"countdown survives restart");
timer.ToggleStopwatch();now=now.AddSeconds(3);timer.ToggleStopwatch();now=now.AddSeconds(10);
Check(timer.StopwatchElapsed==3,"stopwatch pause");timer.ToggleStopwatch();now=now.AddSeconds(2);Check(timer.StopwatchElapsed==5,"stopwatch resumes");
var lines=Lrc.Parse("[offset:500]\n[00:10.25][01:02.125]alpha\n[00:12]beta\n[ar:test]");
Check(lines.Count==3&&Math.Abs(lines[0].Time-10.75)<.001,"LRC multi timestamp and offset");
Check(Lrc.At(lines,0)==("♪","alpha"),"intro does not show future line as current");
Check(Lrc.At(lines,13)==("beta","alpha"),"seek determines current and next");
Check(Lrc.At(lines,80)==("alpha",""),"last line bounded");
var json="{\"timestamp\":\"2025-01-15T00:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn123\",\"last_agent_message\":\"private content\"}}";
var notice=CodexWatcher.ParseCompletion(json,"test.jsonl");
Check(notice!=null&&!JsonSerializer.Serialize(notice).Contains("private content"),"Codex notification omits conversation contents");
Check(CodexWatcher.ParseCompletion(json.Replace("event_msg","response_item"),"test.jsonl")==null,"not triggered by tool text");
var hub=new NoticeHub();hub.Add(notice!);hub.Add(notice!);Check(hub.Items.Count==1,"completion deduplicated");hub.Clear();hub.Add(notice!);Check(hub.Items.Count==0,"read notifications stay deduplicated");
double frameNow=0;
var frames=new FrameWindow(()=>frameNow);
frames.Add(1,0);frames.Add(1,10);frames.Add(1,40);frames.Add(2,0);frames.Add(2,1);
Check(frames.Sample()==50,"FPS uses elapsed intervals on the main swapchain");
frameNow=2000;Check(frames.Sample()==null,"stopped frames expire instead of freezing FPS");
frames.Add(3,2000);frames.Add(3,2020);frames.Add(3,2040);
Check(frames.Sample()==50,"a new scene does not reuse an old swapchain");
frames.Add(3,2040);frames.Add(3,double.NaN);
Check(frames.Sample()==50,"duplicate or invalid timestamps do not inflate FPS");
var settings=new IslandSettings();
Check(settings.WeatherPlaces.Count==0&&!settings.NotificationsEnabled&&!settings.CodexEnabled&&!settings.FpsEnabled&&!settings.SetupCompleted,"fresh install contains no location and requires optional features to be enabled");
var installFixture=Path.Combine(Path.GetTempPath(),"DesktopIsland.PackageCheck");
Check(AppInstaller.SafeChild(installFixture,"sub/file.dll").StartsWith(installFixture,StringComparison.OrdinalIgnoreCase),"package file stays inside the selected application directory");
foreach(var invalidName in new[]{"../outside.txt",Path.GetPathRoot(installFixture)!+"outside.txt"})
{
    bool rejected=false;try{AppInstaller.SafeChild(installFixture,invalidName);}catch(InvalidDataException){rejected=true;}
    Check(rejected,"installer rejects package path traversal and absolute destinations");
}
Check(GameDetector.IsGame(new ForegroundApp(10,"CrimsonDesert",false),settings),"known game detected in windowed mode");
Check(!GameDetector.IsGame(new ForegroundApp(10,"Codex",true),settings),"fullscreen Codex not a game");
Check(!GameDetector.IsGame(new ForegroundApp(10,"chrome",true),settings),"fullscreen browser not a game");
Check(!GameDetector.IsGame(new ForegroundApp(10,"QQMusic",true),settings),"fullscreen QQ Music does not activate game tracing");
var city=new WeatherPlace(1,"示例城市","测试区域",31.23,121.47,"Asia/Shanghai");
var weatherAt=DateTimeOffset.Parse("2025-01-15T10:00:00Z");
var weatherJson="""{"current":{"weather_code":0,"is_day":0},"daily":{"time":["2025-01-15"],"temperature_2m_min":[4.8],"temperature_2m_max":[13.6]}}""";
var weather=WeatherService.Parse(weatherJson,weatherAt)!;
Check(weather.Minimum==4.8&&weather.Maximum==13.6&&!weather.IsDay,"weather keeps daily extremes and nighttime condition");
Check(WeatherService.Parse(weatherJson.Replace("[4.8]","[null]"),weatherAt)==null,"missing forecast is not displayed as zero degrees");
Check(WeatherService.IsFresh(city,weather,weatherAt.AddMinutes(20)),"recent local-day forecast remains valid");
Check(!WeatherService.IsFresh(city,weather,weatherAt.AddHours(3)),"stale weather expires");
var late=weather with {FetchedAt=DateTimeOffset.Parse("2025-01-15T15:40:00Z")};
Check(!WeatherService.IsFresh(city,late,DateTimeOffset.Parse("2025-01-15T16:01:00Z")),"daily temperatures expire at the city's local midnight");
settings.WeatherPlaces=Enumerable.Range(1,7).Select(i=>city with{Id=i}).Prepend(city with{Id=1}).ToList();settings.SelectedWeatherPlace=7;
WeatherService.Normalize(settings);
Check(settings.WeatherPlaces.Count==5&&settings.SelectedWeatherPlace==1,"saved places deduplicate, cap at five and repair deleted selection");
Check(WeatherService.Condition(0)=="晴"&&WeatherService.Condition(2)=="多云"&&WeatherService.Condition(63)=="雨"&&WeatherService.Condition(75)=="雪","WMO conditions map to requested weather icons");
var qq=new MediaInfo {Title="Example (示例)",Artist="Example Artist",Album="Example Album",Dur=196,Aumid="Tencent.QQMusic"};
Check(QqMusicLyrics.BaseTitle(qq.Title)=="Example","QQ translated subtitle is removable");
Check(QqMusicLyrics.BaseTitle("Song (Live)")=="Song (Live)"&&QqMusicLyrics.BaseTitle("Song (现场版)")=="Song (现场版)","live and version labels remain part of song identity");
Check(QqMusicLyrics.Matches(qq,"Example","Example Artist",196,qq.Album),"QQ song matches title, singer, duration and album");
Check(!QqMusicLyrics.Matches(qq,"Example","Example Artist/2Spade",196,qq.Album)&&!QqMusicLyrics.Matches(qq,"Example","Example Artist",240,qq.Album),"QQ remix singer and different duration are rejected");
var iu=new MediaInfo {Title="노래 (歌曲)",Artist="Singer (가수)",Album="Korean Example Album",Dur=190.786,Aumid="Tencent.QQMusic"};
Check(QqMusicLyrics.Matches(iu,"노래","Singer",191,iu.Album)&&QqMusicLyrics.ArtistQuery(iu.Artist)=="Singer","Korean title translations and singer aliases match the same recording");
Check(QqMusicLyrics.ArtistKey("Singer (feat. Guest)")!=QqMusicLyrics.ArtistKey("Singer")&&QqMusicLyrics.BaseTitle("노래 (现场版)")=="노래 (现场版)","featured artists and alternate recordings are not stripped as aliases");
var preview=new MediaInfo {Title="示例歌曲",Artist="示例歌手",Album="示例专辑",Dur=60.055};
Check(QqMusicLyrics.Matches(preview,"示例歌曲","示例歌手",269,"示例专辑")&&!QqMusicLyrics.Matches(preview,"示例歌曲","示例歌手",269,"Live"),"QQ 60-second preview requires the same album as the full recording");
preview.Album="";
Check(!QqMusicLyrics.Matches(preview,"示例歌曲","示例歌手",269,"示例专辑"),"unknown-album short tracks do not accept a different duration");
var encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.00]A &amp; B"));
Check(QqMusicLyrics.Decode(encoded)=="[00:01.00]A & B"&&QqMusicLyrics.Decode("not lyrics")==null,"QQ plain LRC decodes without treating missing content as lyrics");
Check(LyricsService.CacheKey(qq)!=LyricsService.Key(qq.Title,qq.Artist),"QQ cache does not reuse another provider's matching title");
using(var packet=new MemoryStream())
{
    var command=new FpsCommand(123,456,789);await FpsProtocol.WriteAsync(packet,command,CancellationToken.None);packet.Position=0;
    Check(await FpsProtocol.ReadAsync<FpsCommand>(packet,CancellationToken.None)==command,"collector IPC preserves parent identity and target PID");
}
using(var malformed=new MemoryStream(BitConverter.GetBytes(FpsProtocol.MaximumPacket+1)))
{
    bool rejected=false;try{await FpsProtocol.ReadAsync<FpsCommand>(malformed,CancellationToken.None);}catch(InvalidDataException){rejected=true;}
    Check(rejected,"collector rejects oversized packets before allocating a body");
}

var storageName="test-storage-"+Guid.NewGuid().ToString("N")+".json";
var storagePath=Path.Combine(Storage.Root,storageName);
try
{
    Storage.Write(storageName,1);
    using(var held=new FileStream(storagePath,FileMode.Open,FileAccess.Read,FileShare.Read))
        Check(!Storage.TryWrite(storageName,2),"temporarily locked telemetry is safely skipped");
    using(var held=new FileStream(storagePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        Check(Storage.TryWrite(storageName,3),"a live reader permits atomic replacement on Windows");
    int invalid=0,observed=0;
    var reader=Task.Run(()=>{int previous=3;for(int i=0;i<1500;i++){int value=Storage.Read(storageName,previous);if(value<0||value>499)Interlocked.Increment(ref invalid);if(value!=previous)observed++;previous=value;}});
    for(int i=0;i<500;i++)Storage.Write(storageName,i);
    await reader;
    Check(invalid==0&&observed>0&&Storage.Read(storageName,-1)==499,"concurrent telemetry keeps a complete reading during replacement");
}
finally { File.Delete(storagePath); }

var fixture=Path.Combine(Path.GetTempPath(),"DesktopIsland.Tests."+Guid.NewGuid().ToString("N"));
var sessions=Path.Combine(fixture,"sessions");Directory.CreateDirectory(sessions);
var sessionPath=Path.Combine(sessions,"test.jsonl");
try
{
    File.WriteAllText(sessionPath,json+"\n");
    using var watcher=new CodexWatcher();
    var received=new TaskCompletionSource<Notice>(TaskCreationOptions.RunContinuationsAsynchronously);
    watcher.Completed+=n=>received.TrySetResult(n);
    watcher.Start(fixture);
    await Task.Delay(1100);Check(!received.Task.IsCompleted,"watcher does not replay existing completions");
    var fresh=json.Replace("turn123","fresh-turn");
    File.AppendAllText(sessionPath,fresh[..(fresh.Length/2)]);
    await Task.Delay(1100);Check(!received.Task.IsCompleted,"an incomplete JSONL record does not notify");
    File.AppendAllText(sessionPath,fresh[(fresh.Length/2)..]+"\n");
    var actual=await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(actual.Id.EndsWith("fresh-turn"),"a real appended completion reaches the notification watcher");
}
finally { File.Delete(sessionPath);Directory.Delete(sessions);Directory.Delete(fixture); }
Console.WriteLine($"PASS: {checks} behavioral checks");
