using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using Samaat.Core.Platform;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Samaat.Platform.Windows;

/// <summary>
/// Toast notifications. A packaged app toasts with its package identity; an unpackaged app needs an
/// AppUserModelID, so the first use registers one under HKCU (the same registration the Windows
/// Community Toolkit performs for unpackaged apps).
/// </summary>
public sealed class WindowsToastNotifier(Func<byte[]?>? iconPng = null) : INotifier
{
    private const string Aumid = "Samaat.App";
    private const string AumidKey = @"Software\Classes\AppUserModelId\" + Aumid;

    private bool _registered;

    public void Show(string title, string body)
    {
        try
        {
            var packaged = PackagedApp.IsPackaged;
            if (!packaged)
            {
                EnsureRegistered();
            }

            var xml = new XmlDocument();
            xml.LoadXml(ToastXml(title, body));
            (packaged ? ToastNotificationManager.CreateToastNotifier() : ToastNotificationManager.CreateToastNotifier(Aumid)).Show(new ToastNotification(xml));
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Samaat toast failed: {e}");
        }
    }

    /// <summary>A generic two-line toast; both lines are escaped, since device names can contain any character.</summary>
    internal static string ToastXml(string title, string body) =>
        "<toast><visual><binding template=\"ToastGeneric\">" +
        $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>" +
        "</binding></visual></toast>";

    private void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(AumidKey);
        key.SetValue("DisplayName", "Samaat");
        if (WriteIcon() is { } path)
        {
            key.SetValue("IconUri", path);
        }

        _registered = true;
    }

    private string? WriteIcon()
    {
        try
        {
            if (iconPng?.Invoke() is not { } png)
            {
                return null;
            }

            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Samaat");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "icon.png");
            File.WriteAllBytes(path, png);
            return path;
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Samaat toast icon skipped: {e.Message}");
            return null;
        }
    }
}
