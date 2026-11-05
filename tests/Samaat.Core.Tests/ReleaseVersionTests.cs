using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Samaat.Core.Devices;

namespace Samaat.Core.Tests;

/// <summary>
/// One version for every assembly, set in Directory.Build.props. The release workflow refuses a v* tag that doesn't
/// match it, and packaging/msix/build-msix.ps1 reads it, so the app, the report and the Store package agree.
/// </summary>
public sealed partial class ReleaseVersionTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Samaat.slnx")))
        {
            dir = dir.Parent ?? throw new InvalidOperationException("Samaat.slnx not found above the test output");
        }

        return Path.Combine([dir.FullName, .. parts]);
    }

    private static string PropsVersion() =>
        XDocument.Load(RepoFile("Directory.Build.props")).Descendants("Version").Single().Value;

    [Fact]
    public void Version_IsThreePartSemVer()
    {
        Assert.Matches(ThreePart(), PropsVersion());
    }

    [Fact]
    public void Assemblies_CarryTheVersion()
    {
        var informational = typeof(IEarbudDevice).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.StartsWith(PropsVersion(), informational, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_ChecksTheTagAgainstTheVersion()
    {
        var workflow = File.ReadAllText(RepoFile(".github", "workflows", "release.yml"));

        Assert.Contains("tags: ['v*']", workflow, StringComparison.Ordinal);
        Assert.Contains("Directory.Build.props", workflow, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex ThreePart();
}
