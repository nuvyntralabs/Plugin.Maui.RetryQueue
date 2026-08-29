using Microsoft.Maui.Hosting;

namespace Plugin.Maui.RetryQueue;

sealed class RetryQueueInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        var queue = services.GetService<IRetryQueue>();
        if (queue is null)
        {
            return;
        }

        RetryQueue.SetDefault(queue);
        var options = services.GetService<RetryQueueOptions>();
        if (options?.AutoStart != false)
        {
            _ = queue.StartAsync();
        }
    }
}
