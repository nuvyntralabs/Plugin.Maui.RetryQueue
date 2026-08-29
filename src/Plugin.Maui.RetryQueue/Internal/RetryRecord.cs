namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Internal persisted row. Mapped to SQLite and the in-memory store.
/// </summary>
public sealed class RetryRecord
{
    public string Id { get; set; } = "";

    public string OperationName { get; set; } = "";

    public string? PayloadJson { get; set; }

    public RetryOperationStatus Status { get; set; } = RetryOperationStatus.Pending;

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = RetryQueueDefaults.DefaultMaxAttempts;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastError { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? CorrelationId { get; set; }

    public bool RequiresNetwork { get; set; } = true;

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public RetryOperationInfo ToInfo() => new()
    {
        Id = Id,
        OperationName = OperationName,
        Status = Status,
        Attempts = Attempts,
        MaxAttempts = MaxAttempts,
        CreatedAt = CreatedAt,
        NextAttemptAt = NextAttemptAt,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        LastError = LastError,
        IdempotencyKey = IdempotencyKey,
        CorrelationId = CorrelationId,
        RequiresNetwork = RequiresNetwork,
        Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal)
    };

    public RetryRecord Clone() => new()
    {
        Id = Id,
        OperationName = OperationName,
        PayloadJson = PayloadJson,
        Status = Status,
        Attempts = Attempts,
        MaxAttempts = MaxAttempts,
        CreatedAt = CreatedAt,
        NextAttemptAt = NextAttemptAt,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        LastError = LastError,
        IdempotencyKey = IdempotencyKey,
        CorrelationId = CorrelationId,
        RequiresNetwork = RequiresNetwork,
        Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal),
        LeaseOwner = LeaseOwner,
        LeaseExpiresAt = LeaseExpiresAt
    };
}
