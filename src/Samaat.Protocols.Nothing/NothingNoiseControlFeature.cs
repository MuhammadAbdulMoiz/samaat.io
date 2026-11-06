// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.NoiseControl;

namespace Samaat.Protocols.Nothing;

/// <summary>
/// Off, noise cancelling and transparency, read from 401E and the E003 push and set with F00F. Noise cancelling
/// remembers the strength byte (01/02/03) the buds last reported and sends it back. Read-only unless the driver allows writes.
/// </summary>
public sealed class NothingNoiseControlFeature : INoiseControlFeature, IAsyncDisposable
{
    private readonly NothingSession _session;
    private readonly bool _ownsSession;
    private readonly Lock _gate = new();
    private NoiseControlState _current = new(NoiseMode.Off);
    private byte _lastNoiseCancelling = NothingAnc.DefaultNoiseCancelling;

    internal NothingNoiseControlFeature(NothingSession session, NothingModel model, NothingMessage first, bool ownsSession)
    {
        _session = session;
        _ownsSession = ownsSession;
        Options = new NoiseControlOptions(model.NoiseModes);
        if (NothingAnc.RawOf(first) is { } raw)
        {
            Apply(raw);
        }

        _session.Message += OnMessage;
    }

    public NoiseControlOptions Options { get; }

    public bool OptionsAreExhaustive => true;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Verified;

    public CapabilityAccess Access => _session.AllowWrites ? CapabilityAccess.ReadWrite : CapabilityAccess.ReadOnly;

    public NoiseControlState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<NoiseControlState>? Changed;

    public async Task<CommandResult> SetModeAsync(NoiseMode mode, CancellationToken cancellationToken = default)
    {
        if (Options.ValidateMode(mode) is { } invalid)
        {
            return invalid;
        }

        if (!_session.AllowWrites)
        {
            return CommandResult.Unsupported(NothingDriver.ReadOnlyMessage);
        }

        byte raw;
        lock (_gate)
        {
            raw = mode switch
            {
                NoiseMode.NoiseCancelling => _lastNoiseCancelling,
                NoiseMode.Transparency => NothingAnc.Transparency,
                _ => NothingAnc.Off,
            };
        }

        var sent = await _session.WriteAsync(NothingCommands.AncSet, [0x01, raw, 0x00], cancellationToken);
        if (sent.Status != CommandStatus.Confirmed)
        {
            return sent;
        }

        // The ack means the buds took it; read back what they are actually doing.
        var reread = await _session.RequestAsync(NothingCommands.AncRead, [], cancellationToken);
        var actual = reread is { } message ? NothingAnc.RawOf(message) : null;
        if (actual is null)
        {
            Apply(raw);
            return CommandResult.Confirmed;
        }

        Apply(actual.Value);
        return Current.Mode == mode ? CommandResult.Confirmed : CommandResult.Rejected("The earbuds didn't switch to that mode.");
    }

    public Task<CommandResult> SetAncLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options.ValidateAncLevel(level) ?? CommandResult.Unsupported());

    public Task<CommandResult> SetAmbientLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options.ValidateAmbientLevel(level) ?? CommandResult.Unsupported());

    public Task<CommandResult> SetWindReductionAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommandResult.Unsupported("Wind reduction isn't available on this device."));

    public Task<CommandResult> SetConversationAwarenessAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommandResult.Unsupported("Conversation awareness isn't available on this device."));

    public async ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        if (_ownsSession)
        {
            await _session.DisposeAsync();
        }
    }

    private void OnMessage(NothingMessage message)
    {
        if (NothingAnc.RawOf(message) is { } raw)
        {
            Apply(raw);
        }
    }

    private void Apply(byte raw)
    {
        NoiseControlState state;
        lock (_gate)
        {
            if (NothingAnc.IsNoiseCancelling(raw))
            {
                _lastNoiseCancelling = raw;
            }

            if (NothingAnc.ModeOf(raw) is not { } mode || _current.Mode == mode)
            {
                return;
            }

            state = _current = _current with { Mode = mode };
        }

        Changed?.Invoke(this, state);
    }
}
