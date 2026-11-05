using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;

namespace Samaat.Platform.Windows;

/// <summary>
/// Connects or disconnects a paired Bluetooth audio device the way Windows Settings does: a one-shot KS property
/// (<c>KSPROPSETID_BtAudio</c>) sent to the device's Bluetooth audio filter. Each audio endpoint of the device leads to its
/// filter (endpoint, device topology, connector 0, device id connected to), and the property goes to every distinct filter
/// (the A2DP one and the hands-free one). All COM work runs on the thread pool (MTA).
/// </summary>
// shortcut: one-shot KS property is what Windows Settings uses but is undocumented for apps; if it stops working, fall back to opening ms-settings:bluetooth
internal static class BluetoothAudioLink
{
    internal const uint OneShotReconnect = 0;
    internal const uint OneShotDisconnect = 1;

    private const uint ClsctxAll = 23;
    private const int EFail = unchecked((int)0x80004005);
    private const uint GenericReadWrite = 0xC0000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;

    private static readonly Guid IidDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
    private static readonly PropertyKey ContainerIdKey = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    /// <summary>The render and capture endpoints (active or unplugged) of the Bluetooth device's container. The caller disposes them.</summary>
    private static List<MMDevice> Endpoints(Guid containerId)
    {
        var found = new List<MMDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Unplugged))
                {
                    var keep = false;
                    try
                    {
                        keep = endpoint.Properties.Contains(ContainerIdKey) && endpoint.Properties[ContainerIdKey].Value is Guid id && id == containerId;
                    }
                    finally
                    {
                        if (keep)
                        {
                            found.Add(endpoint);
                        }
                        else
                        {
                            endpoint.Dispose();
                        }
                    }
                }
            }

            return found;
        }
        catch
        {
            found.ForEach(e => e.Dispose()); // don't leak what was collected before the failure
            throw;
        }
    }

    /// <summary>Best effort: the link feature is optional, so any enumeration or COM failure means "no endpoints".</summary>
    public static Task<int> CountEndpointsAsync(Guid containerId) => Task.Run(() =>
    {
        try
        {
            var endpoints = Endpoints(containerId);
            endpoints.ForEach(e => e.Dispose());
            return endpoints.Count;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"PC link probe failed: {ex.Message}");
            return 0;
        }
    });

    /// <summary>Sends the one-shot to every distinct filter behind the device's endpoints. Failures, including enumeration, come back as a failed result; nothing throws.</summary>
    public static Task<PcLinkSendResult> SendAsync(Guid containerId, uint property) => Task.Run(() =>
    {
        List<MMDevice> endpoints;
        try
        {
            endpoints = Endpoints(containerId);
        }
        catch (Exception ex)
        {
            var hr = Marshal.GetHRForException(ex);
            return new PcLinkSendResult(false, 1, hr < 0 ? hr : EFail);
        }

        try
        {
            var targets = new List<PcLinkTarget>();
            foreach (var (endpoint, index) in endpoints.Select((e, i) => (e, i)))
            {
                try
                {
                    var path = FilterPath(RawDevice(endpoint));
                    targets.Add(new PcLinkTarget(path, () => Send(path, property)));
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
                {
                    var hr = Marshal.GetHRForException(ex);
                    targets.Add(new PcLinkTarget($"endpoint-{index}", () => hr < 0 ? hr : EFail));
                }
            }

            return PcLink.SendToAll(targets);
        }
        finally
        {
            endpoints.ForEach(e => e.Dispose());
        }
    });

    // shortcut: NAudio keeps its IMMDevice private, and a second CoCreate of the enumerator clashes with NAudio's own wrapper, so read the field; NAudio is pinned, re-check on upgrade
    private static IMMDevice RawDevice(MMDevice endpoint) =>
        (IMMDevice)(typeof(MMDevice).GetField("deviceInterface", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(endpoint)
            ?? throw new InvalidOperationException("NAudio no longer exposes the endpoint's IMMDevice."));

    /// <summary>The device interface path of the KS filter the endpoint's connector leads to, e.g. <c>\\?\bthenum#...#{6994ad04-...}\src</c>.</summary>
    private static string FilterPath(IMMDevice device)
    {
        object? topology = null;
        IConnector? connector = null;
        try
        {
            var iid = IidDeviceTopology;
            Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out topology));
            Check(((IDeviceTopology)topology).GetConnector(0, out connector));
            Check(connector.GetDeviceIdConnectedTo(out var id));

            // The id reads "{2}.\\?\bthenum#...": a data-flow prefix, then the interface path.
            var start = id.IndexOf('}') + 2;
            return start >= 2 && start < id.Length ? id[start..] : id;
        }
        finally
        {
            Release(connector);
            Release(topology);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }

    /// <summary>Opens the filter and sends the one-shot. Returns 0 or a failure HRESULT.</summary>
    private static int Send(string filterPath, uint property)
    {
        using var handle = CreateFileW(filterPath, GenericReadWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return HResultFromWin32(Marshal.GetLastWin32Error());
        }

        var request = KsProperty.BtAudio(property);
        return DeviceIoControl(handle, KsProperty.Ioctl, ref request, Marshal.SizeOf<KsProperty.Request>(), IntPtr.Zero, 0, out _, IntPtr.Zero)
            ? 0
            : HResultFromWin32(Marshal.GetLastWin32Error());
    }

    private static int HResultFromWin32(int error) => KsProperty.HResultFromWin32(error);

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, ref KsProperty.Request input, int inputLength, IntPtr output, int outputLength, out int returned, IntPtr overlapped);

    // Vtable orders below were checked against the Windows SDK's mmdeviceapi.h and devicetopology.h.
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);

        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig] int GetId(out IntPtr id);

        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);

        [PreserveSig] int GetConnector(uint index, out IConnector connector);

        [PreserveSig] int GetSubunitCount(out uint count);

        [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);

        [PreserveSig] int GetPartById(uint id, out IntPtr part);

        [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig] int GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IntPtr parts);
    }

    [ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetType(out int type);

        [PreserveSig] int GetDataFlow(out int flow);

        [PreserveSig] int ConnectTo(IntPtr other);

        [PreserveSig] int Disconnect();

        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);

        [PreserveSig] int GetConnectedTo([MarshalAs(UnmanagedType.IUnknown)] out object connector);

        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
}

/// <summary>Builds the KSPROPERTY request <see cref="BluetoothAudioLink"/> sends with <c>IOCTL_KS_PROPERTY</c>.</summary>
internal static class KsProperty
{
    /// <summary>KSPROPERTY_TYPE_GET: the one-shots are "get" requests with no output buffer.</summary>
    internal const uint TypeGet = 1;

    /// <summary>IOCTL_KS_PROPERTY: CTL_CODE(FILE_DEVICE_KS, 0x000, METHOD_NEITHER, FILE_ANY_ACCESS).</summary>
    internal const uint Ioctl = 0x2F0003;

    private const int EFail = unchecked((int)0x80004005);

    /// <summary>KSPROPSETID_BtAudio: the property set Windows Settings uses to connect and disconnect Bluetooth audio.</summary>
    internal static readonly Guid BtAudioSet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

    internal static Request BtAudio(uint property) => new() { Set = BtAudioSet, Id = property, Flags = TypeGet };

    /// <summary>HRESULT_FROM_WIN32, except that "no error code" is a failure (E_FAIL): the call already failed.</summary>
    internal static int HResultFromWin32(int error) => error <= 0 ? EFail : unchecked((int)(0x80070000 | (uint)(error & 0xFFFF)));

    /// <summary>KSPROPERTY: the property set, the id within it and the request type.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Request
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }
}

/// <summary>The link feature of a Windows Bluetooth device: connects and disconnects through its audio endpoints.</summary>
internal sealed class WindowsPcLinkFeature(Guid containerId, TimeProvider time) : IPcLinkFeature
{
    private static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DisconnectWait = TimeSpan.FromSeconds(10);

    /// <summary>Set once the device exists; the feature is built first because the device takes its feature list.</summary>
    public IEarbudDevice? Device { get; set; }

    public Task<CommandResult> ConnectAsync(CancellationToken ct = default) =>
        RequestAsync(BluetoothAudioLink.OneShotReconnect, ConnectionState.Connected, ConnectWait, ct);

    public Task<CommandResult> DisconnectAsync(CancellationToken ct = default) =>
        RequestAsync(BluetoothAudioLink.OneShotDisconnect, ConnectionState.Disconnected, DisconnectWait, ct);

    private async Task<CommandResult> RequestAsync(uint property, ConnectionState wanted, TimeSpan wait, CancellationToken ct)
    {
        var sent = await BluetoothAudioLink.SendAsync(containerId, property).WaitAsync(ct);
        if (PcLink.ToResult(sent) is { } failure)
        {
            return failure;
        }

        return await PcLink.AwaitStateAsync(Device!, wanted, wait, time, ct);
    }
}
