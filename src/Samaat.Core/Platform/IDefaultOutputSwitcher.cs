namespace Samaat.Core.Platform;

/// <summary>Making another audio output the Windows default. Implemented per platform.</summary>
public interface IDefaultOutputSwitcher
{
    bool IsSupported { get; }

    /// <summary>Makes the output (its MMDevice id) the default for all roles. Throws on failure.</summary>
    void SetDefault(string outputId);
}

public sealed class NoDefaultOutputSwitcher : IDefaultOutputSwitcher
{
    public bool IsSupported => false;

    public void SetDefault(string outputId) => throw new NotSupportedException("Switching the default output is not supported here.");
}
