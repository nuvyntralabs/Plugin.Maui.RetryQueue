namespace Plugin.Maui.RetryQueue;

interface IRetryHandler
{
    Task ExecuteAsync(string? payloadJson, RetryContext context, CancellationToken cancellationToken);
}

sealed class DelegateRetryHandler : IRetryHandler
{
    readonly Func<RetryContext, CancellationToken, Task> _handler;

    public DelegateRetryHandler(Func<RetryContext, CancellationToken, Task> handler) =>
        _handler = handler;

    public Task ExecuteAsync(string? payloadJson, RetryContext context, CancellationToken cancellationToken)
    {
        _ = payloadJson;
        return _handler(context, cancellationToken);
    }
}

sealed class PayloadRetryHandler<TPayload> : IRetryHandler
{
    readonly Func<TPayload, RetryContext, CancellationToken, Task> _handler;

    public PayloadRetryHandler(Func<TPayload, RetryContext, CancellationToken, Task> handler) =>
        _handler = handler;

    public Task ExecuteAsync(string? payloadJson, RetryContext context, CancellationToken cancellationToken)
    {
        var payload = RetryJson.Deserialize<TPayload>(payloadJson ?? "null");
        return _handler(payload, context, cancellationToken);
    }
}
