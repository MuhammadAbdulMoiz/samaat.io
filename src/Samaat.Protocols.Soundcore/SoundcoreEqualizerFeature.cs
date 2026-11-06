// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/common/modules/equalizer.rs, Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Equalizer;

namespace Samaat.Protocols.Soundcore;

/// <summary>
/// The 22 built-in EQ presets; the raw value in the state is the preset id. Custom (0xFEFE) or any other value reads
/// as no current preset. Always read-only: writing a preset needs per-preset band tables, which aren't ported.
/// It shares the battery feature's session; disposing it only detaches its handler.
/// </summary>
public sealed class SoundcoreEqualizerFeature : IEqualizerFeature, IAsyncDisposable
{
    private readonly SoundcoreSession _session;
    private readonly SoundcoreModel _model;
    private readonly Lock _gate = new();
    private int? _currentId;

    internal SoundcoreEqualizerFeature(SoundcoreSession session, SoundcoreModel model, SoundcoreState first)
    {
        _session = session;
        _model = model;
        _currentId = IdOf(first.EqRaw);
        _session.Message += OnMessage;
    }

    public IReadOnlyList<EqPreset> Presets { get; } =
    [
        new(0, "Signature"), new(1, "Acoustic"), new(2, "Bass booster"), new(3, "Bass reducer"), new(4, "Classical"),
        new(5, "Podcast"), new(6, "Dance"), new(7, "Deep"), new(8, "Electronic"), new(9, "Flat"), new(10, "Hip-hop"),
        new(11, "Jazz"), new(12, "Latin"), new(13, "Lounge"), new(14, "Piano"), new(15, "Pop"), new(16, "R&B"),
        new(17, "Rock"), new(18, "Small speakers"), new(19, "Spoken word"), new(20, "Treble booster"), new(21, "Treble reducer"),
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

    public Task<CommandResult> SetPresetAsync(int presetId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Presets.All(p => p.Id != presetId)
            ? CommandResult.Rejected("That equalizer preset isn't available on this device.")
            : CommandResult.Unsupported(SoundcoreDriver.ReadOnlyMessage));

    public ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        return ValueTask.CompletedTask;
    }

    private int? IdOf(int? raw) => raw is { } r && Presets.Any(p => p.Id == r) ? r : null;

    private void OnMessage(SoundcoreMessage message)
    {
        if (SoundcoreState.TryParse(_model, message) is not { EqRaw: not null } state)
        {
            return;
        }

        var id = IdOf(state.EqRaw);
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
