using System.Runtime.InteropServices;
using Samaat.Core.Platform;

namespace Samaat.Platform.Windows;

/// <summary>Sets the Windows default playback device through the undocumented IPolicyConfig COM interface.</summary>
public sealed class WindowsDefaultOutputSwitcher : IDefaultOutputSwitcher
{
    private static readonly Guid PolicyConfigClient = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private static readonly string[] RoleNames = ["Console", "Multimedia", "Communications"];

    public bool IsSupported => true;

    public void SetDefault(string outputId)
    {
        // shortcut: IPolicyConfig is undocumented; if a Windows update breaks it, hide Switch here and point to Windows sound settings
        var type = Type.GetTypeFromCLSID(PolicyConfigClient) ?? throw new InvalidOperationException("Windows policy config is unavailable.");
        var policy = (IPolicyConfig)Activator.CreateInstance(type)!;
        try
        {
            for (var role = 0; role < RoleNames.Length; role++)
            {
                var hr = policy.SetDefaultEndpoint(outputId, role);
                if (hr < 0)
                {
                    throw new InvalidOperationException($"Windows refused to switch ({RoleNames[role]}): 0x{hr:X8}");
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr deviceId, IntPtr isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr deviceId);
        [PreserveSig] int SetDeviceFormat(IntPtr deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(IntPtr deviceId, IntPtr isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(IntPtr deviceId, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr deviceId, IntPtr isFx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr deviceId, IntPtr isFx, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr deviceId, int visible);
    }
}
