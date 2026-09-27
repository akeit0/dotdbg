namespace AsyncDispatch.Domain;

internal sealed record DispatchResult(
    DispatchJob Job,
    bool Delivered,
    int GatewayCalls,
    string? FailureCode
);
