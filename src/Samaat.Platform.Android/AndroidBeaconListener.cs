using Android.Content;
using Android.Net.Wifi;
using Samaat.Handoff.Discovery;

namespace Samaat.Platform.Android;

/// <summary>
/// Hears the computer's beacons. Many phones drop broadcast packets to save power unless an app holds a Wi-Fi multicast
/// lock, so the lock is held while listening.
/// </summary>
public sealed class AndroidBeaconListener : IBeaconListener
{
    private readonly UdpBeaconListener _inner = new();
    private readonly WifiManager.MulticastLock? _lock;

    public AndroidBeaconListener(Context context)
    {
        var wifi = (context.ApplicationContext ?? context).GetSystemService(Context.WifiService) as WifiManager;
        _lock = wifi?.CreateMulticastLock("samaat-beacons");
        _lock?.SetReferenceCounted(false);
        _inner.Heard += (_, heard) => Heard?.Invoke(this, heard);
    }

    public event EventHandler<HeardBeacon>? Heard;

    public void Start()
    {
        _lock?.Acquire();
        _inner.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        if (_lock?.IsHeld == true)
        {
            _lock.Release();
        }
    }
}
