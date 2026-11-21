using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DesktopIsland.Core;

public record FpsReading(int Pid,double? Fps,string Status,DateTimeOffset At);

public sealed class FpsService : IDisposable
{
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref uint length);
    private static string CollectorPath=>AppPaths.CollectorExecutable;
    private readonly Func<bool> _enabled;
    private readonly CancellationTokenSource _stop=new();
    private readonly int _parent=Environment.ProcessId;
    private readonly long _started;
    private volatile int _target;
    private long _lastLaunch;
    private volatile FpsReading _reading=new(0,null,"正在连接自动帧率",DateTimeOffset.MinValue);
    public FpsReading Reading=>_reading;
    public int CollectorPid {get;private set;}
    public bool Installed=>AppPaths.CollectorConfigured;
    public bool HelperAlive=>(DateTimeOffset.UtcNow-Reading.At).TotalSeconds<4;
    public FpsService(Func<bool> enabled){_enabled=enabled;using var self=Process.GetCurrentProcess();_started=self.StartTime.ToUniversalTime().Ticks;_ = Task.Run(RunAsync);}
    public void Update(int target)=>_target=Math.Max(0,target);
    public double? ForProcess(int pid)=>Reading.Pid==pid&&(DateTimeOffset.UtcNow-Reading.At).TotalSeconds<3?Reading.Fps:null;
    public bool Enable(out string error)
    {
        Interlocked.Exchange(ref _lastLaunch,0);
        error=Installed?"":"请先完成一次自动帧率配置";
        return Installed;
    }
    private static int VerifyCollector(NamedPipeClientStream pipe)
    {
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var pid))throw new IOException("Cannot identify frame collector");
        using var process=OpenProcess(0x1000,false,pid); // Query limited information; no game process access.
        var path=new StringBuilder(1024);uint length=(uint)path.Capacity;
        if(process.IsInvalid||!QueryFullProcessImageName(process,0,path,ref length)||!string.Equals(path.ToString(),CollectorPath,StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Unexpected frame collector");
        return checked((int)pid);
    }
    private async Task<bool> StartTaskAsync(CancellationToken ct)
    {
        try
        {
            var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"schtasks.exe"))
                {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add("/Run");info.ArgumentList.Add("/TN");info.ArgumentList.Add(FpsProtocol.TaskName);
            using var task=Process.Start(info)!;await task.WaitForExitAsync(ct);return task.ExitCode==0;
        }
        catch(OperationCanceledException){throw;}
        catch(Exception e){Storage.Log("FPS task: "+e.GetType().Name);return false;}
    }
    private async Task RunAsync()
    {
        var stop=_stop.Token;
        try
        {
            while(!stop.IsCancellationRequested)
            {
                if(!_enabled()){CollectorPid=0;_reading=new(0,null,"游戏帧率未启用",DateTimeOffset.MinValue);await Task.Delay(1000,stop);continue;}
                if(!Installed){_reading=new(0,null,"自动帧率尚未配置",DateTimeOffset.MinValue);await Task.Delay(3000,stop);continue;}
                try
                {
                    if(DateTime.UtcNow.Ticks-Interlocked.Read(ref _lastLaunch)>TimeSpan.FromSeconds(30).Ticks)
                    {
                        Interlocked.Exchange(ref _lastLaunch,DateTime.UtcNow.Ticks);
                        if(!await StartTaskAsync(stop))_reading=new(0,null,"自动帧率任务未就绪",DateTimeOffset.MinValue);
                    }
                    using var pipe=new NamedPipeClientStream(".",FpsProtocol.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
                    await pipe.ConnectAsync(4000,stop);
                    int verifiedPid=VerifyCollector(pipe);
                    while(!stop.IsCancellationRequested&&_enabled())
                    {
                        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);deadline.CancelAfter(4000);
                        await FpsProtocol.WriteAsync(pipe,new FpsCommand(_parent,_started,_target),deadline.Token);
                        var result=await FpsProtocol.ReadAsync<FpsReply>(pipe,deadline.Token);
                        if(result.CollectorPid!=verifiedPid)throw new IOException("Frame collector identity changed");
                        CollectorPid=verifiedPid;
                        _reading=new(result.Pid,result.Fps,result.Status,DateTimeOffset.UtcNow);
                        await Task.Delay(1000,stop);
                    }
                }
                catch(OperationCanceledException) when(!stop.IsCancellationRequested){_reading=new(0,null,"正在重连自动帧率",DateTimeOffset.MinValue);}
                catch(Exception e) when(e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException)
                    {_reading=new(0,null,"正在重连自动帧率",DateTimeOffset.MinValue);}
                await Task.Delay(2000,stop);
            }
        }
        catch(OperationCanceledException){}
    }
    public void Dispose()=>_stop.Cancel();
}
