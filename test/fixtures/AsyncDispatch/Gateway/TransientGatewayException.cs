namespace AsyncDispatch.Gateway;

internal sealed class TransientGatewayException(string jobId)
    : Exception($"Gateway is busy for {jobId}")
{
    public string Code => "BUSY";
}
