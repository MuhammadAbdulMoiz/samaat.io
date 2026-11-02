namespace Samaat.Core.Platform;

/// <summary>Starting Samaat when the user signs in. Implemented per platform.</summary>
public interface IStartupRegistration
{
    bool IsSupported { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public sealed class NoStartupRegistration : IStartupRegistration
{
    public bool IsSupported => false;

    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }
}
