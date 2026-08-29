namespace Plugin.Maui.RetryQueue;

/// <summary>
/// Treats the device as always online. Used by tests and <c>net10.0</c> hosts.
/// </summary>
public sealed class AlwaysOnlineNetworkGate : INetworkGate
{
    public bool IsOnline => true;

    public event EventHandler? ConnectivityChanged
    {
        add { }
        remove { }
    }
}

/// <summary>
/// Manual connectivity switch for tests and sample apps.
/// </summary>
public sealed class ManualNetworkGate : INetworkGate
{
    bool _isOnline = true;

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value)
            {
                return;
            }

            _isOnline = value;
            ConnectivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ConnectivityChanged;
}
