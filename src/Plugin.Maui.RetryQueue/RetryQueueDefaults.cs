namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Default knobs for failed-operation retries.
/// </summary>
public static class RetryQueueDefaults
{
    /// <summary>SQLite file name under app data.</summary>
    public const string DatabaseFileName = "plugin.maui.retryqueue.db3";

    /// <summary>Retry ceiling when enqueue options do not specify one.</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>How many operations run at once.</summary>
    public const int DefaultWorkerCount = 1;

    /// <summary>Idle wait between drain passes.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long a claimed operation may stay <see cref="RetryOperationStatus.Running"/> before recovery.</summary>
    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);

    /// <summary>Delay after the first failure when using exponential backoff.</summary>
    public static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound for a single delay.</summary>
    public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(10);

    /// <summary>Multiplier that produces 30s → 2min → 8min (capped at 10min).</summary>
    public const double DefaultMultiplier = 4.0;

    /// <summary>Fraction of the delay applied as +/- random jitter (0–1). 0.2 is ±20%.</summary>
    public const double DefaultJitter = 0.2;
}
