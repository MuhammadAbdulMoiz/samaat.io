using Samaat.Core.Platform;
using Windows.UI.ViewManagement;
using Samaat.Core.Diagnostics;

namespace Samaat.Platform.Windows;

/// <summary>Reads the "Show animations in Windows" switch.</summary>
public sealed class WindowsMotionPreference : IMotionPreference
{
    public bool ReduceMotion
    {
        get
        {
            try
            {
                return !new UISettings().AnimationsEnabled;
            }
            catch (Exception ex)
            {
                SamaatLog.Warn("Windows", "Couldn't read the animation setting", ex);
                return false;
            }
        }
    }
}
