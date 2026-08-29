namespace Plugin.Maui.RetryQueue.Sample;

public partial class MainPage : ContentPage
{
    readonly IRetryQueue _queue;
    int _customer;
    int _order;
    int _telemetry;

    public MainPage(IRetryQueue queue)
    {
        InitializeComponent();
        _queue = queue;
        _queue.OperationQueued += OnChanged;
        _queue.OperationStarted += OnChanged;
        _queue.OperationSucceeded += OnChanged;
        _queue.OperationFailed += OnChanged;
        _queue.OperationDeadLettered += OnChanged;
        _queue.OperationCancelled += OnChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = RefreshAsync();
    }

    async void OnCustomerClicked(object? sender, EventArgs e)
    {
        _customer++;
        DemoServices.ResetFlakyCustomer();
        await RetryQueue.EnqueueAsync(
            "customer-registration",
            async () => await DemoServices.RegisterCustomerAsync(),
            new RetryEnqueueOptions { IdempotencyKey = $"customer:{_customer}" });
    }

    async void OnOrderClicked(object? sender, EventArgs e)
    {
        _order++;
        await _queue.EnqueueAsync(
            "order-submit",
            new OrderDraft { OrderId = $"ord-{_order}", Amount = 49.00m + _order });
    }

    async void OnTelemetryClicked(object? sender, EventArgs e)
    {
        _telemetry++;
        await RetryQueue.EnqueueAsync(
            "telemetry",
            async ct => await DemoServices.SendTelemetryAsync(ct),
            new RetryEnqueueOptions { CorrelationId = $"evt-{_telemetry}" });
    }

    async void OnPoisonClicked(object? sender, EventArgs e)
    {
        DemoServices.ArmPoisonTelemetry();
        await _queue.EnqueueAsync(
            "telemetry",
            async () => await DemoServices.SendTelemetryAsync(),
            new RetryEnqueueOptions { MaxAttempts = 2 });
    }

    async void OnDrainClicked(object? sender, EventArgs e) =>
        await _queue.DrainAsync();

    async void OnReplayClicked(object? sender, EventArgs e) =>
        await _queue.RequeueDeadLettersAsync();

    void OnChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());

    async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _queue.GetSnapshotAsync();
            SnapshotLabel.Text =
                $"Worker {(snapshot.IsWorkerRunning ? "on" : "off")} · Net {(snapshot.IsOnline ? "online" : "offline")}{Environment.NewLine}" +
                $"Pending {snapshot.Pending} · Scheduled {snapshot.Scheduled} · Running {snapshot.Running}{Environment.NewLine}" +
                $"Retry {snapshot.Failed} · Dead letter {snapshot.DeadLetter} · Kept success {snapshot.Succeeded}";

            var operations = await _queue.ListAsync(new RetryListQuery { Take = 40 });
            if (operations.Count == 0)
            {
                OperationsLabel.Text = "(empty — success deletes the SQLite row)";
                return;
            }

            OperationsLabel.Text = string.Join(Environment.NewLine, operations.Select(item =>
                $"{item.Status,-11} {item.OperationName,-22} try {item.Attempts}/{item.MaxAttempts}  {Short(item.LastError)}"));
        }
        catch (Exception ex)
        {
            OperationsLabel.Text = ex.Message;
        }
    }

    static string Short(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Length <= 40 ? value : value[..40];
}
