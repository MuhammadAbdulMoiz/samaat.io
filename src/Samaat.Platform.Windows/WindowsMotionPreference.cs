using Samaat.Core.Platform;
using Windows.UI.ViewManagement;

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
            catch (Exception)
            {
                return false;
            }
        }
    }
}
