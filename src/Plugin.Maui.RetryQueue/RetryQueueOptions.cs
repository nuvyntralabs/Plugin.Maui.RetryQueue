namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Host configuration for the failed-operation retry queue.
/// </summary>
public sealed class RetryQueueOptions
{
    internal Dictionary<string, IRetryHandler> Handlers { get; } = new(StringComparer.Ordinal);

    /// <summary>Override the SQLite file path. When empty, uses app data + <see cref="DatabaseFileName"/>.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>File name under the app data directory.</summary>
    public string DatabaseFileName { get; set; } = RetryQueueDefaults.DatabaseFileName;

    /// <summary>Use an in-memory store. Intended for tests and demos.</summary>
    public bool UseInMemoryStore { get; set; }

    /// <summary>How many operations run at once.</summary>
    public int WorkerCount { get; set; } = RetryQueueDefaults.DefaultWorkerCount;

    /// <summary>Idle wait between drain passes.</summary>
    public TimeSpan PollInterval { get; set; } = RetryQueueDefaults.DefaultPollInterval;

    /// <summary>How long a claimed operation may stay <see cref="RetryOperationStatus.Running"/> before recovery.</summary>
    public TimeSpan LeaseDuration { get; set; } = RetryQueueDefaults.DefaultLeaseDuration;

    /// <summary>Retry ceiling when enqueue options do not specify one.</summary>
    public int DefaultMaxAttempts { get; set; } = RetryQueueDefaults.DefaultMaxAttempts;

    /// <summary>
    /// Default backoff: 30 seconds, then 2 minutes, then 10 minutes, with jitter.
    /// </summary>
    public BackoffPolicy Backoff { get; set; } = BackoffPolicy.Default;

    /// <summary>Delete the persisted row after a successful run.</summary>
    public bool DeleteOnSuccess { get; set; } = true;

    /// <summary>
    /// Failed API calls typically need the network. Set false to retry while offline.
    /// </summary>
    public bool DefaultRequiresNetwork { get; set; } = true;

    /// <summary>Start the worker from <c>IMauiInitializeService</c>.</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>Drain due operations when the app returns to the foreground.</summary>
    public bool DrainOnResume { get; set; } = true;

    /// <summary>Replace the clock. Tests inject a fake.</summary>
    public IClock? Clock { get; set; }

    /// <summary>Replace the connectivity gate. Tests inject a manual gate.</summary>
    public INetworkGate? NetworkGate { get; set; }

    /// <summary>Replace the store. Tests inject <see cref="Storage.InMemoryRetryStore"/>.</summary>
    public IRetryStore? Store { get; set; }

    /// <summary>Optional deterministic random for backoff jitter. Tests set this to disable jitter.</summary>
    public Random? Random { get; set; }

    /// <summary>
    /// Registers a named handler used after process death, when the original lambda is gone.
    /// </summary>
    public RetryQueueOptions Register(string operationName, Func<RetryContext, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(handler);
        Handlers[operationName] = new DelegateRetryHandler(handler);
        return this;
    }

    /// <summary>
    /// Registers a named handler that deserializes a persisted payload.
    /// </summary>
    public RetryQueueOptions Register<TPayload>(string operationName, Func<TPayload, RetryContext, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(handler);
        Handlers[operationName] = new PayloadRetryHandler<TPayload>(handler);
        return this;
    }
}
