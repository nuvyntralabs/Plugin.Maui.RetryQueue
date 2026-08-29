namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Computes the delay before the next retry of a failed operation.
/// </summary>
public sealed class BackoffPolicy
{
    /// <summary>Delay after the first failure when <see cref="Delays"/> is empty.</summary>
    public TimeSpan InitialDelay { get; init; } = RetryQueueDefaults.DefaultInitialBackoff;

    /// <summary>Upper bound for a single computed delay.</summary>
    public TimeSpan MaxDelay { get; init; } = RetryQueueDefaults.DefaultMaxBackoff;

    /// <summary>Multiplied by the previous delay each attempt.</summary>
    public double Multiplier { get; init; } = RetryQueueDefaults.DefaultMultiplier;

    /// <summary>Fraction of the delay applied as +/- random jitter (0–1). 0.2 is ±20%.</summary>
    public double Jitter { get; init; } = RetryQueueDefaults.DefaultJitter;

    /// <summary>
    /// Explicit delay after each failure (1st failure uses index 0).
    /// The last entry is reused for later attempts.
    /// </summary>
    public IReadOnlyList<TimeSpan>? Delays { get; init; }

    /// <summary>
    /// Default failed-operation schedule: 30 seconds, 2 minutes, then 10 minutes.
    /// </summary>
    public static BackoffPolicy Default { get; } = Schedule(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10));

    /// <summary>Exponential backoff from <paramref name="initial"/> capped at <paramref name="max"/>.</summary>
    public static BackoffPolicy Exponential(TimeSpan initial, TimeSpan max, double multiplier = 2.0) =>
        new()
        {
            InitialDelay = initial,
            MaxDelay = max,
            Multiplier = multiplier
        };

    /// <summary>Fixed delay on every retry.</summary>
    public static BackoffPolicy Constant(TimeSpan delay) =>
        new()
        {
            InitialDelay = delay,
            MaxDelay = delay,
            Multiplier = 1,
            Jitter = 0
        };

    /// <summary>
    /// Exact delay after each failure. Matches the 30s → 2min → 10min product default.
    /// </summary>
    public static BackoffPolicy Schedule(params TimeSpan[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        if (delays.Length == 0)
        {
            throw new ArgumentException("At least one delay is required.", nameof(delays));
        }

        return new BackoffPolicy
        {
            Delays = delays,
            InitialDelay = delays[0],
            MaxDelay = delays[^1]
        };
    }

    /// <summary>
    /// Delay after <paramref name="failedAttempt"/> failures (1 = first failure).
    /// </summary>
    public TimeSpan Compute(int failedAttempt, Random? random = null)
    {
        if (failedAttempt < 1)
        {
            failedAttempt = 1;
        }

        double capped;
        if (Delays is { Count: > 0 })
        {
            var index = Math.Min(failedAttempt - 1, Delays.Count - 1);
            capped = Math.Max(0, Delays[index].TotalMilliseconds);
        }
        else
        {
            var raw = InitialDelay.TotalMilliseconds * Math.Pow(Multiplier, failedAttempt - 1);
            capped = Math.Min(Math.Max(0, raw), MaxDelay.TotalMilliseconds);
        }

        if (Jitter <= 0)
        {
            return TimeSpan.FromMilliseconds(capped);
        }

        random ??= Random.Shared;
        var spread = capped * Jitter * ((random.NextDouble() * 2) - 1);
        return TimeSpan.FromMilliseconds(Math.Max(0, capped + spread));
    }
}
