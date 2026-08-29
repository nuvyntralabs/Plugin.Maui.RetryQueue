namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Point-in-time counts for dashboards and sample UIs.
/// </summary>
public sealed class RetryQueueSnapshot
{
    public int Pending { get; init; }

    /// <summary>Pending operations whose next attempt is still in the future.</summary>
    public int Scheduled { get; init; }

    public int Running { get; init; }

    /// <summary>Failed operations waiting for backoff.</summary>
    public int Failed { get; init; }

    public int DeadLetter { get; init; }

    public int Succeeded { get; init; }

    public int Cancelled { get; init; }

    public bool IsWorkerRunning { get; init; }

    public bool IsOnline { get; init; }
}
