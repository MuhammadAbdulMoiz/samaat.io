using Microsoft.Win32;
using Samaat.Core.Platform;
using Windows.ApplicationModel;

namespace Samaat.Platform.Windows;

/// <summary>Starts Samaat at sign-in: the package startup task when packaged, else the current user's Run key (no admin rights needed).</summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Samaat";
    private const string TaskId = "SamaatStartup";

    // Under `dotnet run` the process is dotnet.exe; registering that would launch nothing useful.
    public bool IsSupported => PackagedApp.IsPackaged || Environment.ProcessPath is { } path
        && !Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    public bool IsEnabled
    {
        get
        {
            if (PackagedApp.IsPackaged)
            {
                return IsOn(GetTask().State);
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (PackagedApp.IsPackaged)
        {
            SetTask(enabled);
            return;
        }

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

    // The Run key is virtualized inside MSIX, so a packaged app must use its manifest startup task.
    private static StartupTask GetTask() => StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();

    private static bool IsOn(StartupTaskState state) => state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    private static void SetTask(bool enabled)
    {
        var task = GetTask();
        if (!enabled)
        {
            task.Disable();
            if (IsOn(task.State))
            {
                throw new InvalidOperationException("Couldn't stop starting with Windows because it's turned on by policy.");
            }

            return;
        }

        var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
        if (!IsOn(state))
        {
            throw new InvalidOperationException(state switch
            {
                StartupTaskState.DisabledByUser => "Couldn't start with Windows because it's turned off in Task Manager > Startup apps.",
                StartupTaskState.DisabledByPolicy => "Couldn't start with Windows because it's turned off by policy.",
                _ => $"Couldn't start with Windows (startup state: {state}).",
            });
        }
    }
}
