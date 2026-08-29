namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Execution context passed to a registered handler or live lambda.
/// </summary>
public sealed class RetryContext
{
    internal RetryContext(
        string operationId,
        string operationName,
        int attempt,
        int maxAttempts,
        string? payloadJson,
        string? correlationId,
        IReadOnlyDictionary<string, string> metadata)
    {
        OperationId = operationId;
        OperationName = operationName;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        PayloadJson = payloadJson;
        CorrelationId = correlationId;
        Metadata = metadata;
    }

    /// <summary>Stable identifier assigned at enqueue.</summary>
    public string OperationId { get; }

    /// <summary>Registered operation name, for example <c>customer-registration</c>.</summary>
    public string OperationName { get; }

    /// <summary>1-based attempt number for this execution.</summary>
    public int Attempt { get; }

    /// <summary>Configured retry ceiling.</summary>
    public int MaxAttempts { get; }

    /// <summary>Serialized payload, when one was supplied at enqueue.</summary>
    public string? PayloadJson { get; }

    /// <summary>Optional correlation id from <see cref="RetryEnqueueOptions"/>.</summary>
    public string? CorrelationId { get; }

    /// <summary>Persisted metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Moves the operation to the dead-letter queue without further retries.</summary>
    public void Abort(string reason) => throw new RetryAbortException(reason);
}
