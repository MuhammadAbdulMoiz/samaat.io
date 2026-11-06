using System.Runtime.InteropServices;
using Windows.ApplicationModel.Activation;
using Samaat.Core.Diagnostics;

namespace Samaat.Platform.Windows;

/// <summary>Detects whether the app runs with MSIX package identity (the Microsoft Store build).</summary>
public static class PackagedApp
{
    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE

    private static readonly Lazy<bool> Packaged = new(() =>
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, null) != NoPackage;
    });

    public static bool IsPackaged => Packaged.Value;

    /// <summary>True when Windows started the packaged app through its sign-in startup task.</summary>
    public static bool LaunchedAtStartup
    {
        get
        {
            if (!IsPackaged)
            {
                return false;
            }

            try
            {
                return global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs() is StartupTaskActivatedEventArgs;
            }
            catch (Exception ex)
            {
                SamaatLog.Warn("Store", "Couldn't read how the packaged app was activated", ex);
                return false;
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);
}
