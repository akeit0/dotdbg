namespace AsyncDispatch.Domain;

internal sealed record DispatchJob(string Id, decimal Amount, int GatewayDelayMs);
