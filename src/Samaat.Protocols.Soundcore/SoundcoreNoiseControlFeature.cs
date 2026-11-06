// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/common/packet/inbound/sound_modes.rs, lib/src/devices/soundcore/common/packet/outbound/set_sound_modes.rs and lib/src/devices/soundcore/common/structures/sound_modes.rs, Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.NoiseControl;

namespace Samaat.Protocols.Soundcore;

/// <summary>
/// Off, noise cancelling and transparency from the state and the sound-mode pushes. Read-only unless the driver
/// allows writes. A write keeps the last-known sub-mode bytes. It shares the battery feature's session; disposing
/// it only detaches its handler.
/// </summary>
public sealed class SoundcoreNoiseControlFeature : INoiseControlFeature, IAsyncDisposable
{
    private readonly SoundcoreSession _session;
    private readonly SoundcoreModel _model;
    private readonly Lock _gate = new();
    private NoiseControlState _current;
    private SoundcoreSoundModes _lastModes;

    internal SoundcoreNoiseControlFeature(SoundcoreSession session, SoundcoreModel model, SoundcoreState first)
    {
        _session = session;
        _model = model;
        _lastModes = first.Modes ?? default;
        _current = new NoiseControlState(first.Modes is { } modes ? ModeOf(modes.Ambient) ?? NoiseMode.Off : NoiseMode.Off);
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
            return CommandResult.Unsupported(SoundcoreDriver.ReadOnlyMessage);
        }

        SoundcoreSoundModes last;
        lock (_gate)
        {
            last = _lastModes;
        }

        byte[] body = [RawOf(mode), last.NoiseCancelling, last.Transparency, last.CustomNoiseCancelling];
        var result = await _session.SendAndConfirmAsync(SoundcoreCommands.SoundModesGroup, SoundcoreCommands.SoundModesSet, body, cancellationToken);
        if (result.Succeeded)
        {
            Apply(last with { Ambient = body[0] });
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

    private static NoiseMode? ModeOf(byte ambient) => ambient switch
    {
        2 => NoiseMode.Off,
        0 => NoiseMode.NoiseCancelling,
        1 => NoiseMode.Transparency,
        _ => null,
    };

    private static byte RawOf(NoiseMode mode) => mode switch
    {
        NoiseMode.NoiseCancelling => 0,
        NoiseMode.Transparency => 1,
        _ => 2,
    };

    private void OnMessage(SoundcoreMessage message)
    {
        SoundcoreSoundModes? modes = message switch
        {
            { C0: SoundcoreCommands.SoundModesGroup, C1: SoundcoreCommands.SoundModesPush, Body.Length: >= SoundcoreSoundModes.Length } => SoundcoreSoundModes.From(message.Body),
            _ => SoundcoreState.TryParse(_model, message)?.Modes,
        };
        if (modes is { } m)
        {
            Apply(m);
        }
    }

    private void Apply(SoundcoreSoundModes modes)
    {
        NoiseControlState state;
        lock (_gate)
        {
            _lastModes = modes;
            if (ModeOf(modes.Ambient) is not { } mode || _current.Mode == mode)
            {
                return;
            }

            state = _current = _current with { Mode = mode };
        }

        Changed?.Invoke(this, state);
    }
}
