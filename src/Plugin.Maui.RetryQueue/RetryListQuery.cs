namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Filter for <see cref="IRetryQueue.ListAsync"/>.
/// </summary>
public sealed class RetryListQuery
{
    /// <summary>Restrict to one status.</summary>
    public RetryOperationStatus? Status { get; set; }

    /// <summary>Restrict to one registered operation name.</summary>
    public string? OperationName { get; set; }

    /// <summary>Maximum rows to return. Defaults to 100.</summary>
    public int Take { get; set; } = 100;
}
