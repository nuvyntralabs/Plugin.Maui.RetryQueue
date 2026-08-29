namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Per-enqueue overrides. Unset values fall back to <see cref="RetryQueueOptions"/>.
/// </summary>
public sealed class RetryEnqueueOptions
{
    /// <summary>Wait this long from now before the first attempt.</summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>Run no earlier than this UTC instant. Wins over <see cref="Delay"/> when both are set.</summary>
    public DateTimeOffset? ScheduleAt { get; set; }

    /// <summary>Maximum attempts before dead-letter.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>
    /// When set, a second enqueue with the same key returns the existing operation id
    /// if that operation is still pending, running, or waiting to retry.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Optional correlation id for logs and UI.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Skip this operation while the device is offline.
    /// Defaults to <see cref="RetryQueueOptions.DefaultRequiresNetwork"/> (true).
    /// </summary>
    public bool? RequiresNetwork { get; set; }

    /// <summary>Opaque string pairs persisted with the operation.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}
