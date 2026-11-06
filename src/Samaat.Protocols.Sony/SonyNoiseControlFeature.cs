// SPDX-License-Identifier: GPL-3.0-or-later
// Written for Samaat from docs/protocols/sony.md (clean-room: behaviour learned from Gadgetbridge, no code copied).

using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.NoiseControl;

namespace Samaat.Protocols.Sony;

/// <summary>
/// Off, noise cancelling and transparency from the noise replies and pushes. Read-only unless the driver allows
/// writes; then the set command is SonyHeadphonesClient's. It shares the battery feature's session; disposing it
/// only detaches its handler.
/// </summary>
public sealed class SonyNoiseControlFeature : INoiseControlFeature, IAsyncDisposable
{
    private const byte EffectOn = 0x11;
    private const byte EffectOff = 0x00;
    private const byte MaxAmbientLevel = 19;

    private readonly SonySession _session;
    private readonly Lock _gate = new();
    private NoiseControlState _current;
    private SonyNoiseReport _last;

    internal SonyNoiseControlFeature(SonySession session, SonyNoiseReport first)
    {
        _session = session;
        _last = first;
        _current = new NoiseControlState(first.Mode);
        _session.Message += OnMessage;
    }

    public NoiseControlOptions Options { get; } = new([NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency]);

    public bool OptionsAreExhaustive => true;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

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
            return CommandResult.Unsupported(SonyDriver.ReadOnlyMessage);
        }

        SonyNoiseReport last;
        lock (_gate)
        {
            last = _last;
        }

        // shortcut: unverified mapping taken from SonyHeadphonesClient's UI, check with a capture before turning writes on
        byte[] payload;
        switch (mode)
        {
            case NoiseMode.Transparency:
                payload = SonyNcAsmCommand.Build(EffectOn, last.Voice, Math.Min(last.AmbientLevel, MaxAmbientLevel));
                break;
            case NoiseMode.NoiseCancelling:
                payload = SonyNcAsmCommand.Build(EffectOff, 0, SonyNcAsmCommand.LevelOff);
                break;
            default:
                return CommandResult.Unsupported("Turning noise control off isn't supported yet.");
        }

        var result = await _session.SendCommandAsync(payload, cancellationToken);
        if (result.Succeeded)
        {
            Apply(mode);
        }

        return result;
    }

    public Task<CommandResult> SetAncLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options.ValidateAncLevel(level) ?? CommandResult.Unsupported());

    public Task<CommandResult> SetAmbientLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options.ValidateAmbientLevel(level) ?? CommandResult.Unsupported());

    public Task<CommandResult> SetWindReductionAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommandResult.Unsupported("Wind reduction isn't available on this device."));

    public Task<CommandResult> SetConversationAwarenessAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommandResult.Unsupported("Conversation awareness isn't available on this device."));

    public ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        return ValueTask.CompletedTask;
    }

    private void OnMessage(byte[] payload)
    {
        if (SonyNoiseReport.TryParse(payload) is not { } report)
        {
            return;
        }

        lock (_gate)
        {
            _last = report;
        }

        Apply(report.Mode);
    }

    private void Apply(NoiseMode mode)
    {
        NoiseControlState state;
        lock (_gate)
        {
            if (_current.Mode == mode)
            {
                return;
            }

            state = _current = _current with { Mode = mode };
        }

        Changed?.Invoke(this, state);
    }
}
