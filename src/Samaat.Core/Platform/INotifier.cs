namespace Samaat.Core.Platform;

/// <summary>Shows a system notification. Must never throw: a failed notification is not worth a crash.</summary>
public interface INotifier
{
    void Show(string title, string body);
}

public sealed class NoNotifier : INotifier
{
    public void Show(string title, string body)
    {
    }
}
