namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Tells the worker whether network-constrained operations may run.
/// </summary>
public interface INetworkGate
{
    bool IsOnline { get; }

    event EventHandler? ConnectivityChanged;
}
