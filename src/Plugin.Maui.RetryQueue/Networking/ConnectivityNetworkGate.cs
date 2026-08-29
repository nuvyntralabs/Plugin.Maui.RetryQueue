namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Uses MAUI <see cref="Microsoft.Maui.Networking.Connectivity"/> when running on a device.
/// </summary>
public sealed class ConnectivityNetworkGate : INetworkGate, IDisposable
{
    public ConnectivityNetworkGate()
    {
        Connectivity.ConnectivityChanged += OnChanged;
    }

    public bool IsOnline =>
        Connectivity.NetworkAccess is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet;

    public event EventHandler? ConnectivityChanged;

    public void Dispose() => Connectivity.ConnectivityChanged -= OnChanged;

    void OnChanged(object? sender, ConnectivityChangedEventArgs e) =>
        ConnectivityChanged?.Invoke(this, EventArgs.Empty);
}
