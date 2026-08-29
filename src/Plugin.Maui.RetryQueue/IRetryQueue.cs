namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Failed-operation retry queue. Operations live in SQLite until they succeed (and are deleted),
/// are cancelled, or land in the dead-letter queue.
/// </summary>
public interface IRetryQueue
{
    /// <summary>True while the in-process worker is running.</summary>
    bool IsRunning { get; }

    event EventHandler<RetryEventArgs>? OperationQueued;

    event EventHandler<RetryEventArgs>? OperationStarted;

    event EventHandler<RetryCompletedEventArgs>? OperationSucceeded;

    event EventHandler<RetryFailedEventArgs>? OperationFailed;

    event EventHandler<RetryEventArgs>? OperationDeadLettered;

    event EventHandler<RetryEventArgs>? OperationCancelled;

    /// <summary>Persists <paramref name="operationName"/> and retries <paramref name="operation"/> until it succeeds or dead-letters.</summary>
    Task<string> EnqueueAsync(string operationName, Func<Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="operationName"/> and retries <paramref name="operation"/> until it succeeds or dead-letters.</summary>
    Task<string> EnqueueAsync(string operationName, Func<CancellationToken, Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="payload"/> and retries <paramref name="operation"/> with that payload.</summary>
    Task<string> EnqueueAsync<TPayload>(string operationName, TPayload payload, Func<TPayload, CancellationToken, Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="payload"/> and retries the handler registered for <paramref name="operationName"/>.</summary>
    Task<string> EnqueueAsync<TPayload>(string operationName, TPayload payload, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Cancels a pending or retrying operation. Running operations are cancelled at the next cooperative check.</summary>
    Task<bool> CancelAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>Cancels every active operation with this name. Returns how many were cancelled.</summary>
    Task<int> CancelByNameAsync(string operationName, CancellationToken cancellationToken = default);

    /// <summary>Returns one operation, including dead-lettered rows.</summary>
    Task<RetryOperationInfo?> GetAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>Lists persisted operations matching <paramref name="query"/>.</summary>
    Task<IReadOnlyList<RetryOperationInfo>> ListAsync(RetryListQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Operations that exhausted retries or were aborted.</summary>
    Task<IReadOnlyList<RetryOperationInfo>> GetDeadLettersAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves one dead-lettered operation back to pending with attempts reset.</summary>
    Task<bool> RequeueDeadLetterAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>Requeues every dead-lettered operation. Returns how many were moved.</summary>
    Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a dead-lettered operation.</summary>
    Task<bool> DiscardDeadLetterAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>Queue depth by status.</summary>
    Task<RetryQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes due operations until the queue is idle or <paramref name="maxOperations"/> is reached.
    /// Use this from tests or from an OS background task that should drain work.
    /// </summary>
    Task<int> DrainAsync(int? maxOperations = null, CancellationToken cancellationToken = default);

    /// <summary>Starts the in-process worker. Safe to call more than once.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the in-process worker. Persisted operations stay in SQLite.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
