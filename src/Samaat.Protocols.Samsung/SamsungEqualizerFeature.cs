// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Message/Encoder/SetEqualizerEncoder.cs and GalaxyBudsClient/Message/Decoder/ExtendedStatusUpdateDecoder.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Equalizer;

namespace Samaat.Protocols.Samsung;

/// <summary>
/// The six built-in EQ presets; the raw EQ byte is the preset id. Any other value (e.g. 7, custom) reads as no
/// current preset. Read-only unless the driver allows writes. It shares the battery feature's session;
/// disposing it only detaches its handler.
/// </summary>
public sealed class SamsungEqualizerFeature : IEqualizerFeature, IAsyncDisposable
{
    private readonly SamsungSession _session;
    private readonly SamsungModel _model;
    private readonly Lock _gate = new();
    private int? _currentId;

    internal SamsungEqualizerFeature(SamsungSession session, SamsungModel model, SamsungStatus first)
    {
        _session = session;
        _model = model;
        _currentId = IdOf(first.EqRaw);
        _session.Message += OnMessage;
    }

    public IReadOnlyList<EqPreset> Presets { get; } =
    [
        new(0, "Normal"), new(1, "Bass boost"), new(2, "Soft"), new(3, "Dynamic"), new(4, "Clear"), new(5, "Treble boost"),
    ];

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

    public async Task<CommandResult> SetPresetAsync(int presetId, CancellationToken cancellationToken = default)
    {
        if (Presets.All(p => p.Id != presetId))
        {
            return CommandResult.Rejected("That equalizer preset isn't available on this device.");
        }

        if (!_session.AllowWrites)
        {
            return CommandResult.Unsupported("Changing this on Galaxy Buds isn't supported yet.");
        }

        var result = await _session.SendAndConfirmAsync(SamsungIds.Equalizer, [(byte)presetId], cancellationToken);
        if (result.Succeeded)
        {
            Apply(presetId);
        }

        return result;
    }

    public ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        return ValueTask.CompletedTask;
    }

    private int? IdOf(int? raw) => raw is { } r && Presets.Any(p => p.Id == r) ? r : null;

    private void OnMessage(SppMessage message)
    {
        int? raw = message switch
        {
            { Id: SamsungIds.Ack, Payload.Length: >= 2 } when message.Payload[0] == SamsungIds.Equalizer => message.Payload[1],
            _ => SamsungStatus.TryParse(message, _model)?.EqRaw,
        };
        if (raw is not null)
        {
            Apply(raw.Value);
        }
    }

    private void Apply(int raw)
    {
        var id = IdOf(raw);
        lock (_gate)
        {
            if (_currentId == id)
            {
                return;
            }

            _currentId = id;
        }

        Changed?.Invoke(this, id);
    }
}
