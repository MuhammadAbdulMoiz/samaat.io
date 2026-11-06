// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Message/Decoder/NoiseControlUpdateDecoder.cs and GalaxyBudsClient/Message/Decoder/ExtendedStatusUpdateDecoder.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.NoiseControl;

namespace Samaat.Protocols.Samsung;

/// <summary>
/// Off, noise cancelling and transparency from the status byte and the buds' mode pushes. Read-only unless the
/// driver allows writes. Adaptive (raw 3) isn't offered: it keeps the previous mode. It shares the battery
/// feature's session; disposing it only detaches its handler.
/// </summary>
public sealed class SamsungNoiseControlFeature : INoiseControlFeature, IAsyncDisposable
{
    private const string ReadOnlyMessage = "Changing this on Galaxy Buds isn't supported yet.";

    private readonly SamsungSession _session;
    private readonly SamsungModel _model;
    private readonly Lock _gate = new();
    private NoiseControlState _current;

    internal SamsungNoiseControlFeature(SamsungSession session, SamsungModel model, SamsungStatus first)
    {
        _session = session;
        _model = model;
        _current = new NoiseControlState(ModeOf(first.NoiseRaw) ?? NoiseMode.Off);
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
            return CommandResult.Unsupported(ReadOnlyMessage);
        }

        var result = await _session.SendAndConfirmAsync(SamsungIds.NoiseControlsSet, [RawOf(mode)], cancellationToken);
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

    private static NoiseMode? ModeOf(int? raw) => raw switch
    {
        0 => NoiseMode.Off,
        1 => NoiseMode.NoiseCancelling,
        2 => NoiseMode.Transparency,
        _ => null,
    };

    private static byte RawOf(NoiseMode mode) => mode switch
    {
        NoiseMode.NoiseCancelling => 1,
        NoiseMode.Transparency => 2,
        _ => 0,
    };

    private void OnMessage(SppMessage message)
    {
        var raw = message.Id == SamsungIds.NoiseControlsUpdate && message.Payload.Length >= 1
            ? message.Payload[0]
            : SamsungStatus.TryParse(message, _model)?.NoiseRaw;
        if (ModeOf(raw) is { } mode)
        {
            Apply(mode);
        }
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
