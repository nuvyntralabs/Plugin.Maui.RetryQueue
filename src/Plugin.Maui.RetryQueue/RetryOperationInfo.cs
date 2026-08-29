namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Read model for a persisted failed operation.
/// </summary>
public sealed class RetryOperationInfo
{
    /// <summary>Stable identifier assigned at enqueue.</summary>
    public required string Id { get; init; }

    /// <summary>Registered operation name, for example <c>customer-registration</c>.</summary>
    public required string OperationName { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public RetryOperationStatus Status { get; init; }

    /// <summary>Completed attempts so far (includes the current run when <see cref="Status"/> is <see cref="RetryOperationStatus.Running"/>).</summary>
    public int Attempts { get; init; }

    /// <summary>Retry ceiling.</summary>
    public int MaxAttempts { get; init; }

    /// <summary>When the operation was first persisted.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the current or next attempt becomes due.</summary>
    public DateTimeOffset NextAttemptAt { get; init; }

    /// <summary>When the current run started, if running or previously started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the operation finished, if retained after success.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Last exception message or abort reason.</summary>
    public string? LastError { get; init; }

    /// <summary>Idempotency key, when supplied.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Optional correlation id.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Whether the worker waits for connectivity.</summary>
    public bool RequiresNetwork { get; init; }

    /// <summary>Persisted metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
