using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Equalizer;
using Samaat.Core.Diagnostics;

namespace Samaat.Protocols.Jieli;

/// <summary>
/// EQ presets over RCSP sys-info. Only presets the buds list in attribute 0x0C (modes 0-6, ten bands) are
/// shown or writable. It shares the battery feature's session and only detaches its handlers on dispose.
/// </summary>
public sealed class JieliEqualizerFeature : IEqualizerFeature, IAsyncDisposable
{
    // Jieli SDK names for the EQ modes.
    private static readonly string[] Names = ["Standard", "Rock", "Pop", "Classical", "Jazz", "Country", "Custom"];

    private readonly RcspSession _session;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<sbyte>> _gains;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _setGate = new(1, 1);
    private int? _currentId;
    private bool _disposed;

    private JieliEqualizerFeature(RcspSession session, EqPresetTable table, byte currentMode)
    {
        _session = session;
        _gains = table.Presets.ToDictionary(p => (int)p.Mode, p => p.Gains);
        Presets = [.. table.Presets.Select(p => new EqPreset(p.Mode, Names[p.Mode]))];
        _currentId = IdOf(currentMode);
        _session.Packet += OnPacket;
        _session.Authenticated += OnAuthenticated;
    }

    public IReadOnlyList<EqPreset> Presets { get; }

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

    public int? CurrentPresetId
    {
        get
        {
            lock (_gate)
            {
                return _currentId;
            }
        }
    }

    public event EventHandler<int?>? Changed;

    /// <summary>
    /// Reads EQ and presets from an authenticated session. Null (nothing attached) unless both parse and at
    /// least one ten-band preset with mode 0-6 is listed; never throws except on cancellation.
    /// </summary>
    public static async Task<JieliEqualizerFeature?> CreateAsync(RcspSession session, CancellationToken cancellationToken)
    {
        var command = RcspCommands.GetSysInfo(RcspCommands.MaskEq)!;
        var reply = await session.SendAsync(command, cancellationToken);
        if (reply is not { Status: 0 }
            || SysInfo.ParseParams(reply.Params) is not { } attrs
            || !attrs.Values.TryGetValue(SysInfo.AttrEq, out var eqValue)
            || SysInfo.ParseEq(eqValue) is not { } eq
            || !attrs.Values.TryGetValue(SysInfo.AttrEqPresets, out var presetsValue)
            || SysInfo.ParsePresets(presetsValue) is not { } table
            || table.Frequencies.Count != SysInfo.Bands)
        {
            return null;
        }

        return table.Presets.Count == 0 ? null : new JieliEqualizerFeature(session, table, eq.Mode);
    }

    public async Task<CommandResult> SetPresetAsync(int presetId, CancellationToken cancellationToken = default)
    {
        if (!_gains.TryGetValue(presetId, out var gains) || RcspCommands.SetEqPreset((byte)presetId, gains) is not { } command)
        {
            return CommandResult.Unsupported("That EQ preset isn't available on this device.");
        }

        await _setGate.WaitAsync(cancellationToken);
        try
        {
            var reply = await _session.SendAsync(command, cancellationToken);
            if (reply is null)
            {
                return new CommandResult(CommandStatus.TimedOut);
            }

            if (reply.Status != 0)
            {
                return CommandResult.Rejected("The earbuds refused the change.");
            }

            // Confirm by asking what the buds are actually using now.
            if (await ReadCurrentModeAsync(cancellationToken) is not { } actual)
            {
                return new CommandResult(CommandStatus.TimedOut);
            }

            Apply(actual);
            return actual == presetId ? CommandResult.Confirmed : CommandResult.Rejected("The earbuds didn't switch to that preset.");
        }
        finally
        {
            _setGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _session.Packet -= OnPacket;
        _session.Authenticated -= OnAuthenticated;
        return ValueTask.CompletedTask;
    }

    private async Task<byte?> ReadCurrentModeAsync(CancellationToken cancellationToken)
    {
        var command = RcspCommands.GetSysInfo(RcspCommands.MaskEqCurrent)!;
        var reply = await _session.SendAsync(command, cancellationToken);
        return reply is { Status: 0 }
            && SysInfo.ParseParams(reply.Params) is { } attrs
            && attrs.Values.TryGetValue(SysInfo.AttrEq, out var value)
            && SysInfo.ParseEq(value) is { } eq
                ? eq.Mode
                : null;
    }

    private void OnPacket(RcspPacket packet)
    {
        if (packet.Op == 0x09
            && SysInfo.ParseParams(packet.Params) is { Function: SysInfo.FunctionPublic } attrs
            && attrs.Values.TryGetValue(SysInfo.AttrEq, out var value)
            && SysInfo.ParseEq(value) is { } eq)
        {
            Apply(eq.Mode);
        }
    }

    // On failure the next push or reconnect brings the state back; the log records why it was missed.
    private void OnAuthenticated() => _ = SamaatLog.Guard("Jieli", "Reading the EQ preset after authentication", async () =>
    {
        if (await ReadCurrentModeAsync(CancellationToken.None) is { } mode && !_disposed)
        {
            Apply(mode);
        }
    });

    private void Apply(byte mode)
    {
        var id = IdOf(mode);
        lock (_gate)
        {
            _currentId = id;
        }

        Changed?.Invoke(this, id);
    }

    /// <summary>The preset id for a reported EQ mode, or null when it isn't one of the listed presets.</summary>
    private int? IdOf(byte mode) => _gains.ContainsKey(mode) ? mode : null;
}
