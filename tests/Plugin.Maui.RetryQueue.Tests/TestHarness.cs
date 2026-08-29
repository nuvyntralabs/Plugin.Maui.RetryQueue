namespace Plugin.Maui.RetryQueue.Tests;

sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan duration) => UtcNow += duration;
}

sealed class CustomerDto
{
    public string Name { get; set; } = "";
}

static class Counters
{
    public static int Registrations;
    public static int Telemetry;
    public static int Orders;
    public static List<string> Payloads { get; } = [];

    public static void Reset()
    {
        Registrations = 0;
        Telemetry = 0;
        Orders = 0;
        Payloads.Clear();
    }
}

static class Harness
{
    public static (IRetryQueue Queue, FakeClock Clock, ManualNetworkGate Network, InMemoryRetryStore Store, IServiceProvider Services)
        Create(Action<RetryQueueOptions>? configure = null)
    {
        Counters.Reset();

        var clock = new FakeClock();
        var network = new ManualNetworkGate { IsOnline = true };
        var store = new InMemoryRetryStore();
        var options = new RetryQueueOptions
        {
            UseInMemoryStore = true,
            Store = store,
            Clock = clock,
            NetworkGate = network,
            AutoStart = false,
            DeleteOnSuccess = true,
            DefaultMaxAttempts = 5,
            DefaultRequiresNetwork = true,
            Backoff = BackoffPolicy.Constant(TimeSpan.FromMinutes(1)),
            Random = new Random(1)
        };

        options.Register("customer-registration", async (ctx, ct) =>
        {
            _ = ctx;
            ct.ThrowIfCancellationRequested();
            Counters.Registrations++;
            await Task.CompletedTask;
        });
        options.Register<CustomerDto>("order-submit", async (order, ctx, ct) =>
        {
            _ = ctx;
            ct.ThrowIfCancellationRequested();
            Counters.Orders++;
            Counters.Payloads.Add(order.Name);
            await Task.CompletedTask;
        });
        options.Register("telemetry", async (ctx, ct) =>
        {
            _ = ctx;
            ct.ThrowIfCancellationRequested();
            Counters.Telemetry++;
            await Task.CompletedTask;
        });

        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddMauiRetryQueue(options);
        var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<IRetryQueue>();
        return (queue, clock, network, store, provider);
    }
}
