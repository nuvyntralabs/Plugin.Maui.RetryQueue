namespace Plugin.Maui.RetryQueue.Sample;

public sealed class OrderDraft
{
    public string OrderId { get; set; } = "";

    public decimal Amount { get; set; }
}

/// <summary>
/// In-memory stand-ins for registration, orders, and telemetry so the sample can fail and retry.
/// </summary>
public sealed class DemoServices
{
    static int _customerFailsRemaining = 2;
    static int _orderFailsRemaining = 1;
    static int _telemetryFailsRemaining;

    public static void ResetFlakyCustomer() => _customerFailsRemaining = 2;

    public static void ArmPoisonTelemetry() => _telemetryFailsRemaining = 99;

    public static async Task RegisterCustomerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(80, cancellationToken);
        if (Interlocked.Decrement(ref _customerFailsRemaining) >= 0)
        {
            throw new InvalidOperationException("API 503 — try again");
        }
    }

    public static async Task SubmitOrderAsync(OrderDraft order, RetryContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(80, cancellationToken);
        if (order.Amount <= 0)
        {
            context.Abort("invalid order amount");
        }

        if (Interlocked.Decrement(ref _orderFailsRemaining) >= 0)
        {
            throw new InvalidOperationException($"Payment gateway timeout for {order.OrderId}");
        }
    }

    public static async Task SendTelemetryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(40, cancellationToken);
        if (Interlocked.Decrement(ref _telemetryFailsRemaining) >= 0)
        {
            throw new InvalidOperationException("analytics sink unavailable");
        }
    }
}
