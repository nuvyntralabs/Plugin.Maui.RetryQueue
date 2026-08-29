namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Raised for retry-queue lifecycle changes. Handlers may run on a worker thread.
/// </summary>
public class RetryEventArgs : EventArgs
{
    public RetryEventArgs(RetryOperationInfo operation)
    {
        Operation = operation;
    }

    public RetryOperationInfo Operation { get; }
}

/// <summary>
/// Raised after a successful execution.
/// </summary>
public sealed class RetryCompletedEventArgs : RetryEventArgs
{
    public RetryCompletedEventArgs(RetryOperationInfo operation, TimeSpan duration) : base(operation)
    {
        Duration = duration;
    }

    public TimeSpan Duration { get; }
}

/// <summary>
/// Raised after a failed attempt that will retry or dead-letter.
/// </summary>
public sealed class RetryFailedEventArgs : RetryEventArgs
{
    public RetryFailedEventArgs(RetryOperationInfo operation, Exception? exception, bool willRetry, TimeSpan? nextDelay)
        : base(operation)
    {
        Exception = exception;
        WillRetry = willRetry;
        NextDelay = nextDelay;
    }

    public Exception? Exception { get; }

    public bool WillRetry { get; }

    /// <summary>Backoff before the next attempt, when <see cref="WillRetry"/> is true.</summary>
    public TimeSpan? NextDelay { get; }
}
