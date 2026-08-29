namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Thrown from a handler (or <see cref="RetryContext.Abort"/>) to skip remaining retries and dead-letter the operation.
/// </summary>
public sealed class RetryAbortException : Exception
{
    public RetryAbortException(string message) : base(message)
    {
    }

    public RetryAbortException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
