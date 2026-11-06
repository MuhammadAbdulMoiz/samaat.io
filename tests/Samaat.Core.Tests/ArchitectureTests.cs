using System.Xml.Linq;

namespace Samaat.Core.Tests;

/// <summary>
/// Enforces the layering in docs/ARCHITECTURE.md by reading each project's references. Dependencies point inwards:
/// apps → composition (Drivers) → protocols and platforms → Core → Transport. A new project or a new reference has to
/// be added to <see cref="Allowed"/> on purpose, which is where a layering decision gets reviewed.
/// </summary>
public sealed class ArchitectureTests
{
    private const string Core = "Samaat.Core";
    private const string Transport = "Samaat.Transport";
    private const string Handoff = "Samaat.Handoff";
    private const string Ui = "Samaat.UI";
    private const string Simulator = "Samaat.Simulator";
    private const string Drivers = "Samaat.Drivers";
    private const string Standard = "Samaat.Protocols.Standard";
    private const string Jieli = "Samaat.Protocols.Jieli";
    private const string FastPair = "Samaat.Protocols.FastPair";
    private const string Bose = "Samaat.Protocols.Bose";
    private const string Samsung = "Samaat.Protocols.Samsung";
    private const string Soundcore = "Samaat.Protocols.Soundcore";
    private const string Windows = "Samaat.Platform.Windows";

    /// <summary>Which projects each source project may reference. Anything not listed is a layering violation.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        // Innermost: no Samaat dependencies at all.
        [Transport] = [],
        [Ui] = [],

        // The domain: features, capabilities, drivers' contracts. Knows transports, nothing above.
        [Core] = [Transport],

        // Protocol codecs and drivers: bytes ↔ features. Never a platform, never another vendor.
        [Standard] = [Core, Transport],
        [Jieli] = [Core, Transport],
        [FastPair] = [Core, Transport],
        [Bose] = [Core, Transport],
        [Samsung] = [Core, Transport],
        [Soundcore] = [Core, Transport],

        // Feature modules beside Core.
        [Handoff] = [Core],
        [Simulator] = [Core],

        // The single list of shipped drivers.
        [Drivers] = [Core, Transport, Jieli, FastPair, Bose, Samsung, Soundcore],

        // A platform implements Core, Transport and Handoff contracts, and reads the standard Battery Service.
        [Windows] = [Core, Transport, Handoff, Standard],

        // Composition roots. They pick drivers through Samaat.Drivers, never a vendor protocol directly.
        ["Samaat.App"] = [Core, Ui, Handoff, Drivers, Simulator, Windows],
        ["Samaat.Cli"] = [Core, Drivers, Simulator, Windows],
    };

    /// <summary>Projects that may use Avalonia: the UI layer only, so the domain stays testable without a UI.</summary>
    private static readonly HashSet<string> UiProjects = [Ui, "Samaat.App"];

    private static readonly DirectoryInfo Root = FindRoot();

    private static DirectoryInfo FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Samaat.slnx")))
        {
            dir = dir.Parent ?? throw new InvalidOperationException("Samaat.slnx not found above the test output");
        }

        return dir;
    }

    private static IEnumerable<string> SourceProjects() =>
        Directory.EnumerateFiles(Path.Combine(Root.FullName, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Order(StringComparer.Ordinal);

    private static XDocument Load(string project) =>
        XDocument.Load(Path.Combine(Root.FullName, "src", project, $"{project}.csproj"));

    private static IReadOnlyList<string> ProjectReferences(string project) =>
        Load(project).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();

    private static IReadOnlyList<string> PackageReferences(string project) =>
        Load(project).Descendants("PackageReference").Select(r => r.Attribute("Include")!.Value).ToList();

    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (var project in SourceProjects())
        {
            data.Add(project);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_HasALayeringRule(string project)
    {
        Assert.True(Allowed.ContainsKey(project),
            $"{project} has no entry in ArchitectureTests.Allowed. Decide which layers it may depend on and add it there and to docs/ARCHITECTURE.md.");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_ReferencesOnlyWhatItsLayerAllows(string project)
    {
        Assert.True(Allowed.TryGetValue(project, out var allowed), project);

        var violations = ProjectReferences(project).Except(allowed).ToList();

        Assert.True(violations.Count == 0, $"{project} must not reference {string.Join(", ", violations)}");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void OnlyTheUiLayer_UsesAvalonia(string project)
    {
        if (UiProjects.Contains(project))
        {
            return;
        }

        Assert.DoesNotContain(PackageReferences(project), p => p.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void EverySourceProject_IsInTheSolution()
    {
        var solution = File.ReadAllText(Path.Combine(Root.FullName, "Samaat.slnx"));

        var missing = SourceProjects().Where(p => !solution.Contains($"src/{p}/{p}.csproj", StringComparison.Ordinal)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void TheRules_DescribeOnlyProjectsThatExist()
    {
        var stale = Allowed.Keys.Except(SourceProjects()).ToList();

        Assert.Empty(stale);
    }
}
