using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace DesktopIsland.Core;

public sealed class DxgiFrameCounter : IDisposable
{
    private readonly TraceEventSession _session;
    private readonly int _pid;
    private readonly Dictionary<int,(ulong Chain,double Time)> _pending=[];
    private readonly FrameWindow _frames=new();
    private readonly Task _consumer;
    private int _disposed;
    private long _events;
    public long EventCount=>Interlocked.Read(ref _events);
    public DxgiFrameCounter(int pid,string name)
    {
        _pid=pid;
        _session=new TraceEventSession(name) { StopOnDispose=true, BufferSizeMB=4 };
        try
        {
            _session.Source.Dynamic.All+=OnEvent;
            _session.Source.UnhandledEvents+=OnEvent;
            _session.EnableProvider(new Guid("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9"),TraceEventLevel.Verbose,0x8000000000000002,
                new TraceEventProviderOptions { ProcessIDFilter=[pid] });
            _consumer=Task.Run(()=>{try{_session.Source.Process();}catch(Exception e){Debug.WriteLine("DXGI trace: "+e.GetType().Name);}});
        }
        catch { _session.Dispose(); throw; }
    }
    private void OnEvent(TraceEvent e)
    {
        Interlocked.Increment(ref _events);
        if(e.ProcessID!=_pid)return;
        var id=(int)e.ID;
        if(id is not (42 or 43 or 55 or 56))return;
        try
        {
                if(id is 42 or 55)
                {
                    var flags=Convert.ToUInt32(e.PayloadByName("Flags"));
                    if((flags&1)!=0){_pending.Remove(e.ThreadID);return;}
                    var chain=Convert.ToUInt64(e.PayloadByName("pIDXGISwapChain"));
                    _pending[e.ThreadID]=(chain,e.TimeStampRelativeMSec);
                }
                else if(_pending.Remove(e.ThreadID,out var frame))
                {
                    var result=Convert.ToInt64(e.PayloadByName("Result"));
                    if(result!=0)return;
                    _frames.Add(frame.Chain,frame.Time);
                }
        }
        catch(Exception ex) { if(EventCount<10)Debug.WriteLine("DXGI event format: "+ex.GetType().Name); }
    }
    public double? Sample()=>_frames.Sample();
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        try { _session.Stop(); _consumer.Wait(TimeSpan.FromSeconds(3)); }
        finally { _session.Dispose(); }
    }
}
