// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Equalizer;

namespace Samaat.Protocols.Nothing;

/// <summary>
/// The buds' EQ presets, read from 4050 (<c>PP</c>) and set with F01D (<c>PP 00</c>). Custom is listed so the current
/// value round-trips; choosing it restores the curve stored on the buds.
/// </summary>
public sealed class NothingEqualizerFeature : IEqualizerFeature, IAsyncDisposable
{
    private readonly NothingSession _session;
    private readonly bool _ownsSession;
    private readonly Lock _gate = new();
    private int? _currentId;

    internal NothingEqualizerFeature(NothingSession session, NothingModel model, NothingMessage first, bool ownsSession)
    {
        _session = session;
        _ownsSession = ownsSession;
        Presets = model.Presets;
        _currentId = IdOf(first);
        _session.Message += OnMessage;
    }

    public IReadOnlyList<EqPreset> Presets { get; }

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Verified;

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
            return CommandResult.Unsupported(NothingDriver.ReadOnlyMessage);
        }

        var sent = await _session.WriteAsync(NothingCommands.EqSet, [(byte)presetId, 0x00], cancellationToken);
        if (sent.Status != CommandStatus.Confirmed)
        {
            return sent;
        }

        var reread = await _session.RequestAsync(NothingCommands.EqRead, [], cancellationToken);
        if (reread is null)
        {
            Apply(presetId);
            return CommandResult.Confirmed;
        }

        Apply(IdOf(reread.Value));
        return CurrentPresetId == presetId ? CommandResult.Confirmed : CommandResult.Rejected("The earbuds didn't switch to that preset.");
    }

    public async ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        if (_ownsSession)
        {
            await _session.DisposeAsync();
        }
    }

    private int? IdOf(NothingMessage message) =>
        message is { Command: NothingCommands.EqReply, Payload.Length: >= 1 } && Presets.Any(p => p.Id == message.Payload[0])
            ? message.Payload[0]
            : null;

    private void OnMessage(NothingMessage message)
    {
        if (message.Command == NothingCommands.EqReply)
        {
            Apply(IdOf(message));
        }
    }

    private void Apply(int? id)
    {
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
