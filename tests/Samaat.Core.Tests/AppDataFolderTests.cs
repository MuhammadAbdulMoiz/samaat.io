using Samaat.Core.Settings;

namespace Samaat.Core.Tests;

public sealed class AppDataFolderTests
{
    [Fact]
    public void Choose_SkipsNullEmptyAndBlankRoots_AndUsesTheFirstRealOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "real-root");

        Assert.Equal(Path.Combine(root, "Samaat"), AppDataFolder.Choose(null, "", "  ", root, "other"));
    }

    [Fact]
    public void Default_IsRooted_SoItCanNeverResolveAgainstTheWorkingDirectory()
    {
        Assert.True(Path.IsPathRooted(AppDataFolder.Path));
        Assert.True(Path.IsPathRooted(SettingsStore.DefaultPath));
    }
}
