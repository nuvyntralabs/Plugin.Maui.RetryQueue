namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Registers the retry queue without MAUI lifecycle hooks.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IRetryQueue"/> using the supplied options instance.
    /// </summary>
    public static IServiceCollection AddMauiRetryQueue(this IServiceCollection services, RetryQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IClock>(sp =>
            sp.GetRequiredService<RetryQueueOptions>().Clock ?? SystemClock.Instance);
        services.TryAddSingleton<INetworkGate>(sp =>
            sp.GetRequiredService<RetryQueueOptions>().NetworkGate ?? RetryQueue.CreateNetworkGate());
        services.TryAddSingleton<IRetryStore>(sp =>
        {
            var resolved = sp.GetRequiredService<RetryQueueOptions>();
            if (resolved.Store is not null)
            {
                return resolved.Store;
            }

            return resolved.UseInMemoryStore
                ? new Storage.InMemoryRetryStore()
                : new Storage.SqliteRetryStore(resolved);
        });
        services.TryAddSingleton<IRetryQueue>(sp =>
        {
            var queue = RetryQueue.Create(sp, sp.GetRequiredService<RetryQueueOptions>());
            RetryQueue.SetDefault(queue);
            return queue;
        });

        return services;
    }

    /// <summary>
    /// Adds <see cref="IRetryQueue"/> and applies <paramref name="configure"/> to a new options instance.
    /// </summary>
    public static IServiceCollection AddMauiRetryQueue(this IServiceCollection services, Action<RetryQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new RetryQueueOptions();
        configure?.Invoke(options);
        return services.AddMauiRetryQueue(options);
    }
}
