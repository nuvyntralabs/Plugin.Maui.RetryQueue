using Microsoft.Extensions.Logging;

namespace Plugin.Maui.RetryQueue.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<DemoServices>();

        builder
            .UseMauiApp<App>()
            .UseMauiRetryQueue(options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(400);
                options.DefaultMaxAttempts = 5;
                options.Backoff = BackoffPolicy.Schedule(
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(6),
                    TimeSpan.FromSeconds(15));
                options.DeleteOnSuccess = true;
                options.Register("customer-registration", async (_, ct) =>
                    await DemoServices.RegisterCustomerAsync(ct));
                options.Register<OrderDraft>("order-submit", async (order, ctx, ct) =>
                    await DemoServices.SubmitOrderAsync(order, ctx, ct));
                options.Register("telemetry", async (_, ct) =>
                    await DemoServices.SendTelemetryAsync(ct));
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
