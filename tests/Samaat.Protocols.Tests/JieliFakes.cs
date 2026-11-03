using Samaat.Protocols.Jieli;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>Bytes captured from the real ParadoX-E (docs/hardware-tests/2026-11-03-audionic-paradox-e.md).</summary>
internal static class Captured
{
    public static readonly byte[] AdvInfoFrame = Convert.FromHexString("FEDCBA80C20013" + "01" + "05D601D2004A22305F277C717E02646400" + "62" + "EF");

    public static readonly byte[] AdvInfoParams = Convert.FromHexString("05D601D2004A22305F277C717E02646400" + "62");

    public static readonly byte[] OtherFrame = Convert.FromHexString("FEDCBA800900050200020701EF");

    /// <summary>GetSysInfo 0x6000 answer (status 0, sn 2): current noise mode Off and the three listed modes.</summary>
    public static readonly byte[] Sys6000Response = Convert.FromHexString(
        "FEDCBA0007002C0002FF0A0D0000000000000000001D0E03000000000000000000014000400040004000024000400040004000EF");

    /// <summary>GetSysInfo 0x1010 answer (status 0, sn 3): current EQ (Classical) and the preset table.</summary>
    public static readonly byte[] Sys1010Response = Convert.FromHexString(
        "FEDCBA000700740003FF0C040300080804000000000202630C0A0019003A00940168035C05F00BB817701F4035E800F80000FBFA0108FE00FC01FE000204FEFE0000040402030100FEFCFCFE0001020300080804000000000202040000000404040002030405FE0000020200000004040600000000000000000000EF");

    /// <summary>The preset-table TLV (len, type, value) exactly as the buds sent it.</summary>
    public static readonly byte[] PresetsTlv = Sys1010Response[23..^1];

    /// <summary>The three VoiceModes the buds listed: Off, noise cancelling, transparency.</summary>
    public static readonly byte[][] Modes =
    [
        Convert.FromHexString("000000000000000000"),
        Convert.FromHexString("014000400040004000"),
        Convert.FromHexString("024000400040004000"),
    ];

    public static readonly ushort[] Frequencies = [25, 58, 148, 360, 860, 1520, 3000, 6000, 8000, 13800];

    /// <summary>Gains for presets 0 Standard, 1 Rock, 2 Pop, 3 Classical, 4 Jazz, 5 Country, 6 Custom.</summary>
    public static readonly sbyte[][] PresetGains =
    [
        [-8, 0, 0, -5, -6, 1, 8, -2, 0, -4],
        [-2, 0, 2, 4, -2, -2, 0, 0, 4, 4],
        [3, 1, 0, -2, -4, -4, -2, 0, 1, 2],
        [0, 8, 8, 4, 0, 0, 0, 0, 2, 2],
        [0, 0, 0, 4, 4, 4, 0, 2, 3, 4],
        [-2, 0, 0, 2, 2, 0, 0, 0, 4, 4],
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    ];

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
    /// <summary>Authenticates, pushes the captured AdvInfo, and answers sys-info reads and sets like the real buds.</summary>
    Normal,
    WrongCipher,
    Silent,

    /// <summary>Authenticates and answers sys-info, but never pushes the battery.</summary>
    NoPush,
    MalformedPush,
    NonBatteryPush,

    /// <summary>Answers every SetSysInfo with status 0x01 and changes nothing.</summary>
    SetRejected,

    /// <summary>Pushes the battery but ignores every GetSysInfo and SetSysInfo.</summary>
    SilentSysInfo,

    /// <summary>Answers reads but ignores every SetSysInfo.</summary>
    SilentSets,

    /// <summary>Acknowledges sets (status 0) but keeps its old state, so the readback differs.</summary>
    ReadbackMismatch,

    /// <summary>Acknowledges and applies sets without any push, and after the first set its readbacks carry no attributes.</summary>
    BrokenReadback,
}

/// <summary>A fake AE00 service that answers the auth handshake and RCSP sys-info like the real buds and records every write.</summary>
internal sealed class FakeBuds
{
    private static readonly byte[] Challenge = Convert.FromHexString("FBFB7830441A48787BC039EB502BE09A");
    private static readonly byte[] Pass = [0x70, 0x61, 0x73, 0x73];

    private byte _pushSn = 0x40;
    private bool _setSeen;

    public FakeBuds(BudsMode mode = BudsMode.Normal)
    {
        Mode = mode;
        Write = new FakeJieliCharacteristic(JieliGatt.Write, canWrite: true, canNotify: false) { OnWrite = OnWrite };
        Notify = new FakeJieliCharacteristic(JieliGatt.Notify, canWrite: false, canNotify: true);
        Gatt = new FakeJieliGatt(new FakeJieliService(JieliGatt.Service, Write, Notify));
    }

    public BudsMode Mode { get; set; }

    /// <summary>The buds' current noise mode id (0 Off, 1 NC, 2 Transparency).</summary>
    public byte Noise { get; set; }

    /// <summary>The buds' current EQ mode id (0-6); the real buds were on Classical.</summary>
    public byte Eq { get; set; } = 3;

    /// <summary>Before answering a set, push an unrelated function-0 update and the state change.</summary>
    public bool InterleavePushes { get; set; }

    public FakeJieliCharacteristic Write { get; }

    public FakeJieliCharacteristic Notify { get; }

    public FakeJieliGatt Gatt { get; }

    public List<byte[]> Writes { get; } = [];

    public int AuthStarts => Writes.Count(w => w.Length == 17 && w[0] == 0x00);

    /// <summary>Framed RCSP commands only (the auth steps are unframed).</summary>
    public IEnumerable<byte[]> Commands => Writes.Where(w => w.Length > 3 && w[0] == 0xFE && w[1] == 0xDC && w[2] == 0xBA);

    public void Push(byte[] frame)
    {
        // The captured frame arrives in three notifications.
        Notify.Notify(frame[..6]);
        Notify.Notify(frame[6..12]);
        Notify.Notify(frame[12..]);
    }

    /// <summary>The buds change mode on their own (a button press, say) and tell us.</summary>
    public void PushNoise(byte mode)
    {
        Noise = mode;
        PushState(NoiseTlv());
    }

    public void PushEq(byte mode)
    {
        Eq = mode;
        PushState(EqTlv());
    }

    private byte[] NoiseTlv() => [0x0A, 0x0D, .. Captured.Modes[Noise]];

    private byte[] EqTlv() => [0x0C, 0x04, Eq, .. Captured.PresetGains[Eq].Select(g => (byte)g)];

    private void PushState(byte[] tlv) =>
        Notify.Notify(RcspFrame.Build(0x80, 0x09, _pushSn++, [0xFF, .. tlv]));

    private void Reply(byte op, byte status, byte sn, byte[] parameters) =>
        Notify.Notify(RcspFrame.Build(0x00, op, status, [sn, .. parameters]));

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
                case BudsMode.SetRejected:
                case BudsMode.SilentSysInfo:
                case BudsMode.SilentSets:
                case BudsMode.ReadbackMismatch:
                case BudsMode.BrokenReadback:
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
        else if (new RcspFrameParser().Feed(data).FirstOrDefault() is { } command)
        {
            HandleCommand(command);
        }

        return true;
    }

    private void HandleCommand(RcspPacket command)
    {
        if (Mode == BudsMode.SilentSysInfo || command.Flags != 0xC0)
        {
            return;
        }

        if (command.Op == 0x07 && command.Params.Length == 5)
        {
            Read(command);
        }
        else if (command.Op == 0x08 && command.Params.Length >= 3)
        {
            Set(command);
        }
    }

    private void Read(RcspPacket command)
    {
        var mask = (command.Params[1] << 24) | (command.Params[2] << 16) | (command.Params[3] << 8) | command.Params[4];
        var attrs = new List<byte> { 0xFF };
        if (Mode == BudsMode.BrokenReadback && _setSeen && mask is 0x2000 or 0x0010)
        {
            Reply(0x07, 0x00, command.Sn, [.. attrs]);
            return;
        }

        if ((mask & 0x0010) != 0)
        {
            attrs.AddRange(EqTlv());
        }

        if ((mask & 0x1000) != 0)
        {
            attrs.AddRange(Captured.PresetsTlv);
        }

        if ((mask & 0x2000) != 0)
        {
            attrs.AddRange(NoiseTlv());
        }

        if ((mask & 0x4000) != 0)
        {
            attrs.AddRange([0x1D, 0x0E, 0x03, .. Captured.Modes.SelectMany(m => m)]);
        }

        Reply(0x07, 0x00, command.Sn, [.. attrs]);
    }

    private void Set(RcspPacket command)
    {
        if (Mode == BudsMode.SilentSets)
        {
            return;
        }

        if (Mode == BudsMode.SetRejected)
        {
            Reply(0x08, 0x01, command.Sn, []);
            return;
        }

        _setSeen = true;
        byte[]? pushed = null;
        var type = command.Params[2];
        var value = command.Params[3..];
        if (Mode != BudsMode.ReadbackMismatch)
        {
            if (type == 0x0D && Captured.Modes.Any(m => m.AsSpan().SequenceEqual(value)))
            {
                Noise = value[0];
                pushed = NoiseTlv();
            }
            else if (type == 0x04 && value.Length == 11 && value[0] <= 6
                && value[1..].Select(b => (sbyte)b).SequenceEqual(Captured.PresetGains[value[0]]))
            {
                Eq = value[0];
                pushed = EqTlv();
            }
        }

        if (Mode == BudsMode.BrokenReadback)
        {
            pushed = null;
        }

        if (InterleavePushes)
        {
            Notify.Notify(Captured.OtherFrame);
            if (pushed is not null)
            {
                PushState(pushed);
            }
        }

        Reply(0x08, 0x00, command.Sn, []);
        if (!InterleavePushes && pushed is not null)
        {
            PushState(pushed);
        }
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
