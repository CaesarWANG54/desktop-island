using System.Buffers.Binary;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace DesktopIsland.Core;

public sealed record FpsCommand(int ParentPid,long ParentStart,int TargetPid);
public sealed record FpsReply(int Pid,double? Fps,string Status,int CollectorPid);

public static class FpsProtocol
{
    public const int MaximumPacket=2048;
    public static readonly string UserSid=ReadUserSid();
    private static string ReadUserSid(){using var identity=WindowsIdentity.GetCurrent();return identity.User!.Value;}
    public static string PipeName=>"DesktopIsland.Frames.v2."+UserSid;
    public static string TaskName=>"DesktopIsland-FPS-"+UserSid;
    public static async Task WriteAsync<T>(Stream stream,T value,CancellationToken ct)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value);
        if(bytes.Length>MaximumPacket)throw new InvalidDataException("Frame message too large");
        var size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,bytes.Length);
        await stream.WriteAsync(size,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);
    }
    public static async Task<T> ReadAsync<T>(Stream stream,CancellationToken ct)
    {
        var size=new byte[4];await stream.ReadExactlyAsync(size,ct);
        int count=BinaryPrimitives.ReadInt32LittleEndian(size);
        if(count<=0||count>MaximumPacket)throw new InvalidDataException("Invalid frame message length");
        var bytes=new byte[count];await stream.ReadExactlyAsync(bytes,ct);
        return JsonSerializer.Deserialize<T>(bytes)??throw new InvalidDataException("Empty frame message");
    }
}
