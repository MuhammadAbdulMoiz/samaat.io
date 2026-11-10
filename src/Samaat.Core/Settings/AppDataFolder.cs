namespace Samaat.Core.Settings;

/// <summary>The per-user "Samaat" data folder. Never relative: some Linux setups report an empty roaming folder.</summary>
public static class AppDataFolder
{
    public static string Path { get; } = Choose(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        System.IO.Path.GetTempPath());

    /// <summary>The "Samaat" folder under the first root that is an absolute path; blank and relative roots are skipped.</summary>
    public static string Choose(params string?[] roots) =>
        System.IO.Path.Combine(roots.First(r => !string.IsNullOrWhiteSpace(r) && System.IO.Path.IsPathFullyQualified(r))!, "Samaat");
}
