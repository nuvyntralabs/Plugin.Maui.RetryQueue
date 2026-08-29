using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace Plugin.Maui.RetryQueue;

/// <summary>
/// MAUI host registration for the failed-operation retry queue.
/// </summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="IRetryQueue"/>, named handlers, and optional lifecycle drain-on-resume.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseMauiRetryQueue(options =>
    /// {
    ///     options.Register("customer-registration", async (ctx, ct) =>
    ///         await RegisterCustomer(ct));
    /// });
    /// </code>
    /// </example>
    public static MauiAppBuilder UseMauiRetryQueue(this MauiAppBuilder builder, Action<RetryQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new RetryQueueOptions();
        configure?.Invoke(options);

        builder.Services.AddMauiRetryQueue(options);
        builder.Services.AddTransient<IMauiInitializeService, RetryQueueInitializer>();

        if (options.DrainOnResume)
        {
            builder.ConfigureLifecycleEvents(events =>
            {
#if ANDROID
                events.AddAndroid(android => android.OnResume(_ => ResumeDrain()));
#elif IOS
                events.AddiOS(ios => ios.OnActivated(_ => ResumeDrain()));
#endif
            });
        }

        return builder;
    }

    static void ResumeDrain()
    {
        if (!RetryQueue.IsInitialized)
        {
            return;
        }

        if (RetryQueue.Current.IsRunning)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RetryQueue.Current.DrainAsync().ConfigureAwait(false);
            }
            catch
            {
                // Resume drain is best-effort; failures surface through OperationFailed.
            }
        });
    }
}
