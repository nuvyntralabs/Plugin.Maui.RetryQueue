namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Persistence for operation rows. SQLite by default; in-memory for tests.
/// </summary>
public interface IRetryStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<RetryRecord> InsertAsync(RetryRecord record, CancellationToken cancellationToken = default);

    Task<RetryRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<RetryRecord?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<RetryRecord?> ClaimNextAsync(ClaimRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(RetryRecord record, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RetryRecord>> ListAsync(RetryListQuery query, CancellationToken cancellationToken = default);

    Task<RetryQueueCounts> CountAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<int> RecoverExpiredLeasesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<int> RequeueDeadLettersAsync(string? operationId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
}

/// <summary>
/// Parameters for claiming the next due operation.
/// </summary>
public readonly record struct ClaimRequest(
    DateTimeOffset UtcNow,
    bool IsOnline,
    string LeaseOwner,
    DateTimeOffset LeaseExpiresAt);

/// <summary>
/// Status counts used by <see cref="RetryQueueSnapshot"/>.
/// </summary>
public readonly record struct RetryQueueCounts(
    int Pending,
    int Scheduled,
    int Running,
    int Failed,
    int DeadLetter,
    int Succeeded,
    int Cancelled);
