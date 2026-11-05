namespace Samaat.App.Tray;

/// <summary>The window and tray icon: the Samaat logo (Samaat.UI), drawn at runtime so no binary asset is shipped.</summary>
public static class AppIcon
{
    /// <summary>Must run on the UI thread. Returns PNG bytes.</summary>
    public static byte[] RenderPng(int size = 64) => Samaat.UI.Logo.RenderPng(size);
}
