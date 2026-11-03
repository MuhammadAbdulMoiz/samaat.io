namespace Samaat.Core.Platform;

/// <summary>Whether the operating system asks apps to reduce animation. Implemented per platform.</summary>
public interface IMotionPreference
{
    bool ReduceMotion { get; }
}

public sealed class NoMotionPreference : IMotionPreference
{
    public bool ReduceMotion => false;
}
