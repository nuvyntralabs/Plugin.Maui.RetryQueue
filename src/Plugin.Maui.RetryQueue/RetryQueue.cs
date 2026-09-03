namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Entry point when dependency injection is not used, and factory for tests.
/// </summary>
public static class RetryQueue
{
    static IRetryQueue? _current;

    /// <summary>
    /// Shared queue registered by <see cref="MauiAppBuilderExtensions.UseMauiRetryQueue"/>.
    /// </summary>
    public static IRetryQueue Current =>
        _current ?? throw new InvalidOperationException(
            "RetryQueue has not been initialized. Call builder.UseMauiRetryQueue() in MauiProgram.");

    /// <summary>True after <see cref="MauiAppBuilderExtensions.UseMauiRetryQueue"/> or <see cref="SetDefault"/>.</summary>
    public static bool IsInitialized => _current is not null;

    /// <summary>Retries <paramref name="operation"/> on the shared queue.</summary>
    public static Task<string> EnqueueAsync(string operationName, Func<Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default) =>
        Current.EnqueueAsync(operationName, operation, options, cancellationToken);

    /// <summary>Retries <paramref name="operation"/> on the shared queue.</summary>
    public static Task<string> EnqueueAsync(string operationName, Func<CancellationToken, Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default) =>
        Current.EnqueueAsync(operationName, operation, options, cancellationToken);

    /// <summary>Retries <paramref name="operation"/> with a persisted payload on the shared queue.</summary>
    public static Task<string> EnqueueAsync<TPayload>(string operationName, TPayload payload, Func<TPayload, CancellationToken, Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default) =>
        Current.EnqueueAsync(operationName, payload, operation, options, cancellationToken);

    /// <summary>Retries the registered handler for <paramref name="operationName"/> with a persisted payload.</summary>
    public static Task<string> EnqueueAsync<TPayload>(string operationName, TPayload payload, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default) =>
        Current.EnqueueAsync(operationName, payload, options, cancellationToken);

    /// <summary>Creates a queue. Register named handlers on <paramref name="options"/> first.</summary>
    public static IRetryQueue Create(IServiceProvider services, RetryQueueOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= services.GetService<RetryQueueOptions>() ?? new RetryQueueOptions();
        var store = options.Store
                    ?? services.GetService<IRetryStore>()
                    ?? (options.UseInMemoryStore ? new Storage.InMemoryRetryStore() : new Storage.SqliteRetryStore(options));
        var clock = options.Clock ?? services.GetService<IClock>() ?? SystemClock.Instance;
        var network = options.NetworkGate ?? services.GetService<INetworkGate>() ?? CreateNetworkGate();
        return new RetryQueueEngine(options, store, clock, network);
    }

    /// <summary>Replaces the shared instance. Intended for tests and custom hosts.</summary>
    public static void SetDefault(IRetryQueue implementation) =>
        _current = implementation ?? throw new ArgumentNullException(nameof(implementation));

    internal static INetworkGate CreateNetworkGate()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        return new ConnectivityNetworkGate();
#else
        return new AlwaysOnlineNetworkGate();
#endif
    }
}
