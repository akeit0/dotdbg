using System.IO.Pipes;
using System.Text;
using DotDbg.Ipc;
using Xunit;

namespace DotDbg.Tests;

public class PipeNameTests
{
    // Mirrors the layout System.IO.Pipes uses for a Unix domain socket.
    private static int SocketPathBytes(string pipeName) =>
        Encoding.UTF8.GetByteCount(Path.GetTempPath() + "CoreFxPipe_" + pipeName);

    [Fact]
    public void ShortSessionIdKeepsReadableName()
    {
        Assert.Equal("dotdbg_abc123", PipeServer.ComputePipeName("abc123"));
    }

    [Fact]
    public void SameSessionIdAlwaysMapsToSameName()
    {
        var sessionId = $"dotdbg-workload-{new string('a', 32)}";
        Assert.Equal(PipeServer.ComputePipeName(sessionId), PipeServer.ComputePipeName(sessionId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(48)] // the length the workload test harness generates
    [InlineData(200)]
    [InlineData(5000)]
    public void SocketPathFitsThePlatformLimit(int sessionIdLength)
    {
        var name = PipeServer.ComputePipeName(new string('a', sessionIdLength));

        // 104 bytes on macOS and 108 on Linux, including the terminator.
        var limit = OperatingSystem.IsMacOS() ? 104 : 108;
        Assert.True(
            OperatingSystem.IsWindows() || SocketPathBytes(name) < limit,
            $"session id of {sessionIdLength} chars produced a {SocketPathBytes(name)} byte socket path"
        );
    }

    [Fact]
    public void MultiByteSessionIdFitsThePlatformLimit()
    {
        // The socket path limit counts UTF-8 bytes, not characters.
        var name = PipeServer.ComputePipeName(new string('あ', 40));

        var limit = OperatingSystem.IsMacOS() ? 104 : 108;
        Assert.True(OperatingSystem.IsWindows() || SocketPathBytes(name) < limit);
    }

    [Fact]
    public void DistinctLongSessionIdsGetDistinctNames()
    {
        var prefix = new string('a', 60);
        Assert.NotEqual(
            PipeServer.ComputePipeName(prefix + "one"),
            PipeServer.ComputePipeName(prefix + "two")
        );
    }

    [Fact]
    public void LongSessionIdCanActuallyOpenAPipe()
    {
        // The regression this guards: a 48 character session id overflowed sun_path
        // on macOS and the server constructor threw before it could listen.
        var name = PipeServer.ComputePipeName($"dotdbg-workload-{new string('b', 32)}");

        using var server = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );

        Assert.NotNull(server);
    }
}
