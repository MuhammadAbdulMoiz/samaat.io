using Samaat.Protocols.Jieli;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>Bytes captured from the real ParadoX-E (docs/hardware-tests/2026-11-03-audionic-paradox-e.md).</summary>
internal static class Captured
{
    public static readonly byte[] AdvInfoFrame = Convert.FromHexString("FEDCBA80C20013" + "01" + "05D601D2004A22305F277C717E02646400" + "62" + "EF");

    public static readonly byte[] AdvInfoParams = Convert.FromHexString("05D601D2004A22305F277C717E02646400" + "62");

    public static readonly byte[] OtherFrame = Convert.FromHexString("FEDCBA800900050200020701EF");

    public static byte[] AdvInfoWith(byte left, byte right, byte caseLevel)
    {
        var frame = (byte[])AdvInfoFrame.Clone();
        frame[7 + 1 + 14] = left;
        frame[7 + 1 + 15] = right;
        frame[7 + 1 + 16] = caseLevel;
        return frame;
    }
}

public enum BudsMode
{
    /// <summary>Authenticates, then pushes the captured AdvInfo.</summary>
    Normal,
    WrongCipher,
    Silent,
    NoPush,
    MalformedPush,
    NonBatteryPush,
}

/// <summary>A fake AE00 service that answers the auth handshake like the real buds and records every write.</summary>
internal sealed class FakeBuds
{
    private static readonly byte[] Challenge = Convert.FromHexString("FBFB7830441A48787BC039EB502BE09A");
    private static readonly byte[] Pass = [0x70, 0x61, 0x73, 0x73];

    public FakeBuds(BudsMode mode = BudsMode.Normal)
    {
        Mode = mode;
        Write = new FakeJieliCharacteristic(JieliGatt.Write, canWrite: true, canNotify: false) { OnWrite = OnWrite };
        Notify = new FakeJieliCharacteristic(JieliGatt.Notify, canWrite: false, canNotify: true);
        Gatt = new FakeJieliGatt(new FakeJieliService(JieliGatt.Service, Write, Notify));
    }

    public BudsMode Mode { get; set; }

    public FakeJieliCharacteristic Write { get; }

    public FakeJieliCharacteristic Notify { get; }

    public FakeJieliGatt Gatt { get; }

    public List<byte[]> Writes { get; } = [];

    public int AuthStarts => Writes.Count(w => w.Length == 17 && w[0] == 0x00);

    public void Push(byte[] frame)
    {
        // The captured frame arrives in three notifications.
        Notify.Notify(frame[..6]);
        Notify.Notify(frame[6..12]);
        Notify.Notify(frame[12..]);
    }

    private bool OnWrite(byte[] data)
    {
        Writes.Add(data);
        if (Mode == BudsMode.Silent)
        {
            return true;
        }

        if (data.Length == 17 && data[0] == 0x00)
        {
            var reply = JlAuthCipher.Encrypt(data[1..]);
            if (Mode == BudsMode.WrongCipher)
            {
                reply[0] ^= 0xFF;
            }

            Notify.Notify([0x01, .. reply]);
        }
        else if (data.Length == 5 && data[0] == 0x02 && data.AsSpan(1).SequenceEqual(Pass))
        {
            Notify.Notify([0x00, .. Challenge]);
        }
        else if (data.Length == 17 && data[0] == 0x01 && data.AsSpan(1).SequenceEqual(JlAuthCipher.Encrypt(Challenge)))
        {
            Notify.Notify([0x02, .. Pass]);
            switch (Mode)
            {
                case BudsMode.Normal:
                    Push(Captured.AdvInfoFrame);
                    break;
                case BudsMode.MalformedPush:
                    Push([.. Captured.AdvInfoFrame[..10], 0xEF]);
                    break;
                case BudsMode.NonBatteryPush:
                    Notify.Notify(Captured.OtherFrame);
                    break;
            }
        }

        return true;
    }
}

internal sealed class FakeJieliGatt(params FakeJieliService[] services) : IGattClient
{
    public Func<CancellationToken, Task>? OnGetServices { get; set; }

    public async Task<IReadOnlyList<IGattService>> GetServicesAsync(Guid serviceUuid, CancellationToken cancellationToken = default)
    {
        if (OnGetServices is not null)
        {
            await OnGetServices(cancellationToken);
        }

        return services.Where(s => s.Uuid == serviceUuid).ToList<IGattService>();
    }
}

internal sealed class FakeJieliService(Guid uuid, params FakeJieliCharacteristic[] characteristics) : IGattService
{
    public Guid Uuid { get; } = uuid;

    public Task<IReadOnlyList<IGattCharacteristic>> GetCharacteristicsAsync(Guid characteristicUuid, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IGattCharacteristic>>(characteristics.Where(c => c.Uuid == characteristicUuid).ToList());
}

internal sealed class FakeJieliCharacteristic(Guid uuid, bool canWrite, bool canNotify) : IGattCharacteristic
{
    private Action<byte[]>? _subscriber;

    public Guid Uuid { get; } = uuid;

    public bool CanRead => false;

    public bool CanNotify { get; } = canNotify;

    public bool CanWrite { get; } = canWrite;

    public bool IsSubscribed => _subscriber is not null;

    public Func<byte[], bool>? OnWrite { get; set; }

    public Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

    public Task<byte[]?> ReadDescriptorAsync(Guid descriptorUuid, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

    public Task<bool> WriteAsync(ReadOnlyMemory<byte> data, bool withoutResponse, CancellationToken cancellationToken = default) =>
        Task.FromResult(OnWrite?.Invoke(data.ToArray()) ?? false);

    public Task<IAsyncDisposable> SubscribeAsync(Action<byte[]> onValue, CancellationToken cancellationToken = default)
    {
        _subscriber = onValue;
        return Task.FromResult<IAsyncDisposable>(new Unsubscriber(this));
    }

    public void Notify(byte[] value) => _subscriber?.Invoke(value);

    private sealed class Unsubscriber(FakeJieliCharacteristic owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner._subscriber = null;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeTransports(IGattClient? gatt) : IDeviceTransports
{
    public IGattClient? Gatt { get; } = gatt;

    public Task<IRfcommChannel?> OpenRfcommAsync(Guid serviceUuid, CancellationToken cancellationToken = default) =>
        Task.FromResult<IRfcommChannel?>(null);
}
