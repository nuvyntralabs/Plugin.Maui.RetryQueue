namespace Plugin.Maui.RetryQueue.Tests;

public sealed class BackoffPolicyTests
{
    [Fact]
    public void Default_schedule_is_30s_then_2min_then_10min()
    {
        var policy = new BackoffPolicy
        {
            Delays = BackoffPolicy.Default.Delays,
            Jitter = 0
        };

        Assert.Equal(TimeSpan.FromSeconds(30), policy.Compute(1));
        Assert.Equal(TimeSpan.FromMinutes(2), policy.Compute(2));
        Assert.Equal(TimeSpan.FromMinutes(10), policy.Compute(3));
        Assert.Equal(TimeSpan.FromMinutes(10), policy.Compute(8));
    }

    [Fact]
    public void Exponential_times_four_matches_the_product_curve()
    {
        var policy = new BackoffPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(30),
            MaxDelay = TimeSpan.FromMinutes(10),
            Multiplier = 4,
            Jitter = 0
        };

        Assert.Equal(TimeSpan.FromSeconds(30), policy.Compute(1));
        Assert.Equal(TimeSpan.FromMinutes(2), policy.Compute(2));
        Assert.Equal(TimeSpan.FromMinutes(8), policy.Compute(3));
        Assert.Equal(TimeSpan.FromMinutes(10), policy.Compute(4));
    }

    [Fact]
    public void Constant_is_stable()
    {
        var policy = BackoffPolicy.Constant(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), policy.Compute(1));
        Assert.Equal(TimeSpan.FromSeconds(3), policy.Compute(8));
    }

    [Fact]
    public void Jitter_stays_within_the_configured_spread()
    {
        var policy = new BackoffPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(100),
            MaxDelay = TimeSpan.FromSeconds(100),
            Multiplier = 1,
            Jitter = 0.2
        };

        var delay = policy.Compute(1, new Random(42));
        Assert.InRange(delay.TotalSeconds, 80, 120);
    }
}
