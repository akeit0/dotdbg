using System.Security.Cryptography;
using System.Text;

namespace DotDbg.Ipc;

internal sealed class DaemonLease : IDisposable
{
    private readonly FileStream _stream;

    private DaemonLease(FileStream stream) => _stream = stream;

    public static DaemonLease? TryAcquire(string sessionId)
    {
        var identity = $"{Environment.UserName}\0{sessionId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var path = Path.Combine(Path.GetTempPath(), $"dotdbg-daemon-{hash}.lock");
        try
        {
            return new DaemonLease(
                new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
            );
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
