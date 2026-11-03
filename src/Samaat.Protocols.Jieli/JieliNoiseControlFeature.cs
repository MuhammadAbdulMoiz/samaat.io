using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.NoiseControl;

namespace Samaat.Protocols.Jieli;

/// <summary>
/// Noise control over RCSP sys-info: off, noise cancelling and transparency, as the buds list them.
/// It shares the battery feature's session and only detaches its handlers on dispose.
/// </summary>
public sealed class JieliNoiseControlFeature : INoiseControlFeature, IAsyncDisposable
{
    private readonly RcspSession _session;
    private readonly IReadOnlyDictionary<NoiseMode, VoiceMode> _voices;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _setGate = new(1, 1);
    private NoiseControlState _current;
    private bool _disposed;

    private JieliNoiseControlFeature(RcspSession session, IReadOnlyList<(NoiseMode Mode, VoiceMode Voice)> modes, NoiseMode current)
    {
        _session = session;
        _voices = modes.ToDictionary(m => m.Mode, m => m.Voice);
        Options = new NoiseControlOptions([.. modes.Select(m => m.Mode)]);
        _current = new NoiseControlState(current);
        _session.Packet += OnPacket;
        _session.Authenticated += OnAuthenticated;
    }

    public NoiseControlOptions Options { get; }

    public bool OptionsAreExhaustive => true;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

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

    /// <summary>
    /// Reads the mode list from an authenticated session. Null (nothing attached) unless the buds list at
    /// least one mode other than off that Samaat knows; never throws except on cancellation.
    /// </summary>
    public static async Task<JieliNoiseControlFeature?> CreateAsync(RcspSession session, CancellationToken cancellationToken)
    {
        var attrs = await ReadAsync(session, RcspCommands.MaskNoise, cancellationToken);
        if (attrs is null || !attrs.Values.TryGetValue(SysInfo.AttrNoiseModes, out var list) || SysInfo.ParseModes(list) is not { } listed)
        {
            return null;
        }

        var modes = new List<(NoiseMode, VoiceMode)>();
        foreach (var voice in listed)
        {
            if (ModeOf(voice) is { } mode && modes.All(m => m.Item1 != mode))
            {
                modes.Add((mode, voice));
            }
        }

        if (modes.All(m => m.Item1 == NoiseMode.Off))
        {
            return null;
        }

        var current = attrs.Values.TryGetValue(SysInfo.AttrCurrentNoise, out var value) && CurrentModeOf(value) is { } known
            ? known
            : NoiseMode.Off;
        return new JieliNoiseControlFeature(session, modes, current);
    }

    public async Task<CommandResult> SetModeAsync(NoiseMode mode, CancellationToken cancellationToken = default)
    {
        if (Options.ValidateMode(mode) is { } invalid)
        {
            return invalid;
        }

        await _setGate.WaitAsync(cancellationToken);
        try
        {
            var command = RcspCommands.SetNoiseMode(_voices[mode]);
            var reply = await _session.SendAsync(command, cancellationToken);
            if (reply is null)
            {
                return new CommandResult(CommandStatus.TimedOut);
            }

            if (reply.Status != 0)
            {
                return CommandResult.Rejected("The earbuds refused the change.");
            }

            // Confirm by asking what the buds are actually doing now.
            if (await ReadCurrentAsync(cancellationToken) is not { } actual)
            {
                return new CommandResult(CommandStatus.TimedOut);
            }

            Apply(actual);
            return actual == mode ? CommandResult.Confirmed : CommandResult.Rejected("The earbuds didn't switch to that mode.");
        }
        finally
        {
            _setGate.Release();
        }
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
        _disposed = true;
        _session.Packet -= OnPacket;
        _session.Authenticated -= OnAuthenticated;
        return ValueTask.CompletedTask;
    }

    private static async Task<SysInfoAttrs?> ReadAsync(RcspSession session, uint mask, CancellationToken cancellationToken)
    {
        var command = RcspCommands.GetSysInfo(mask)!;
        var reply = await session.SendAsync(command, cancellationToken);
        return reply is { Status: 0 } ? SysInfo.ParseParams(reply.Params) : null;
    }

    private async Task<NoiseMode?> ReadCurrentAsync(CancellationToken cancellationToken) =>
        await ReadAsync(_session, RcspCommands.MaskNoiseCurrent, cancellationToken) is { } attrs
        && attrs.Values.TryGetValue(SysInfo.AttrCurrentNoise, out var value)
            ? CurrentModeOf(value)
            : null;

    private void OnPacket(RcspPacket packet)
    {
        if (packet.Op == 0x09
            && SysInfo.ParseParams(packet.Params) is { Function: SysInfo.FunctionPublic } attrs
            && attrs.Values.TryGetValue(SysInfo.AttrCurrentNoise, out var value)
            && CurrentModeOf(value) is { } mode)
        {
            Apply(mode);
        }
    }

    private async void OnAuthenticated()
    {
        try
        {
            if (await ReadCurrentAsync(CancellationToken.None) is { } mode && !_disposed)
            {
                Apply(mode);
            }
        }
        catch (Exception)
        {
            // The next push or reconnect brings the state back; nothing to report here.
        }
    }

    private void Apply(NoiseMode mode)
    {
        NoiseControlState updated;
        lock (_gate)
        {
            _current = _current with { Mode = mode };
            updated = _current;
        }

        Changed?.Invoke(this, updated);
    }

    private static NoiseMode? CurrentModeOf(byte[] value) => VoiceMode.Parse(value) is { } voice ? ModeOf(voice) : null;

    /// <summary>Jieli's ANC modes: 0 off, 1 noise cancelling, 2 transparency. Anything else is not exposed.</summary>
    private static NoiseMode? ModeOf(VoiceMode voice) => voice.Mode switch
    {
        0 => NoiseMode.Off,
        1 => NoiseMode.NoiseCancelling,
        2 => NoiseMode.Transparency,
        _ => null,
    };
}
