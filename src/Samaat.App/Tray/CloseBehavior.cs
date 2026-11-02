namespace Samaat.App.Tray;

public enum CloseAction
{
    Hide,
    Exit,
}

public static class CloseBehavior
{
    /// <summary>Closing the window hides it to the tray only when the user didn't ask to quit and the setting is on.</summary>
    public static CloseAction Decide(bool quitting, bool closeToTray) =>
        !quitting && closeToTray ? CloseAction.Hide : CloseAction.Exit;
}
