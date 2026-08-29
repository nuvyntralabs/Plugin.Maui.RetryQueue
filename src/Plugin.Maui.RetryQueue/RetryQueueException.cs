namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Thrown when the queue cannot persist or dispatch an operation.
/// </summary>
public sealed class RetryQueueException : Exception
{
    public RetryQueueException(string message) : base(message)
    {
    }

    public RetryQueueException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
