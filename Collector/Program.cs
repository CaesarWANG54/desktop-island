using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32.SafeHandles;
using DesktopIsland.Core;

// A fixed, protected executable. Accepts PID-only telemetry requests through a
// current-user pipe; never executes client commands or writes client-selected files.
internal static class Program
{
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    public static async Task<int> Main()
    {
        var config=System.Text.Json.JsonSerializer.Deserialize<CollectorConfiguration>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"client.json")));
        if(config==null||config.UserSid!=FpsProtocol.UserSid||!Path.IsPathFullyQualified(config.ClientPath)||Path.GetFileName(config.ClientPath)!="DesktopIsland.exe")return 2;
        using var singleton=new Mutex(true,@"Local\DesktopIsland.Collector.v2."+FpsProtocol.UserSid,out var first);
        if(!first)return 0;
        try {using var self=Process.GetCurrentProcess();self.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
        string sessionName="DesktopIslandFrames_"+FpsProtocol.UserSid;
        try
        {
            if(TraceEventSession.GetActiveSessionNames().Contains(sessionName))
                using(var stale=new TraceEventSession(sessionName,TraceEventSessionOptions.Attach)){stale.Stop();}
        }
        catch{}
        while(true)
        {
            // CurrentUserOnly also demands matching elevation. Give only this user
            // access with explicit ownership so the unelevated island can connect.
            var security=new PipeSecurity();
            security.SetSecurityDescriptorSddlForm("O:"+FpsProtocol.UserSid+"D:P(D;;GA;;;NU)(A;;GA;;;"+FpsProtocol.UserSid+")");
            using var pipe=NamedPipeServerStreamAcl.Create(FpsProtocol.PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
            using var idle=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try {await pipe.WaitForConnectionAsync(idle.Token);}catch(OperationCanceledException){return 0;}
            DxgiFrameCounter? counter=null;int target=0;
            try
            {
                if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out var clientPid))continue;
                using var client=Process.GetProcessById((int)clientPid);
                if(!string.Equals(client.MainModule?.FileName,config.ClientPath,StringComparison.OrdinalIgnoreCase))continue;
                long started=client.StartTime.ToUniversalTime().Ticks;
                while(pipe.IsConnected&&!client.HasExited)
                {
                    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var request=await FpsProtocol.ReadAsync<FpsCommand>(pipe,deadline.Token);
                    if(request.ParentPid!=clientPid||request.ParentStart!=started)break;
                    int next=0;
                    if(request.TargetPid>0)
                        try {using var process=Process.GetProcessById(request.TargetPid);if(process.SessionId==client.SessionId&&!process.HasExited)next=process.Id;}catch{}
                    if(next!=target)
                    {
                        counter?.Dispose();counter=null;target=0;
                        if(next>0)
                            try {counter=new DxgiFrameCounter(next,sessionName);target=next;}
                            catch {await FpsProtocol.WriteAsync(pipe,new FpsReply(next,null,"帧率采集暂不可用",Environment.ProcessId),deadline.Token);continue;}
                    }
                    var fps=counter?.Sample();
                    await FpsProtocol.WriteAsync(pipe,new FpsReply(target,fps,target==0?"自动帧率已就绪":fps!=null?"正在读取游戏帧率":"等待游戏帧率数据",Environment.ProcessId),deadline.Token);
                }
            }
            catch(Exception e) when(e is IOException or OperationCanceledException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException){}
            finally{try{counter?.Dispose();}catch{}}
        }
    }
}
internal sealed record CollectorConfiguration(string UserSid,string ClientPath);
