using Microsoft.Win32;
using Samaat.Core.Platform;

namespace Samaat.Platform.Windows;

/// <summary>Starts Samaat at sign-in through the current user's Run key (no admin rights needed).</summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Samaat";

    // Under `dotnet run` the process is dotnet.exe; registering that would launch nothing useful.
    public bool IsSupported => Environment.ProcessPath is { } path
        && !Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
