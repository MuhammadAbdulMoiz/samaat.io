using System.Text.RegularExpressions;

namespace Samaat.Core.Tests;

/// <summary>
/// Enforces the porting rule in CONTRIBUTING.md ("Protocol work and licences"). Every file under
/// <c>src/Samaat.Protocols.*</c> that says "Ported from" must carry an <c>SPDX-License-Identifier:</c> line and name
/// a source listed in THIRD_PARTY_NOTICES.md, and no file may carry an SPDX identifier outside the allowed list.
/// The ported file itself is licensed GPL-3.0-or-later; the original licence is recorded in its header and the notices.
/// </summary>
public sealed partial class ProvenanceTests
{
    /// <summary>Licences whose code may be ported into Samaat (GPL-3.0-or-later). AGPL and GPL-2.0-only are behaviour only.</summary>
    public static readonly IReadOnlySet<string> AllowedSpdx = new HashSet<string>(StringComparer.Ordinal)
    {
        "GPL-3.0-only", "GPL-3.0-or-later", "MIT", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0",
    };

    private const string NoticesFile = "THIRD_PARTY_NOTICES.md";

    [GeneratedRegex(@"SPDX-License-Identifier:\s*(?<id>\S+)")]
    private static partial Regex SpdxLine();

    [GeneratedRegex(@"Ported from\s+(?<source>https?://\S+?)(?=[\s,]|$)")]
    private static partial Regex PortedFrom();

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

    /// <summary>The provenance problems in one file; empty when the file is fine.</summary>
    public static IReadOnlyList<string> Violations(string path, string text, string notices)
    {
        var problems = new List<string>();
        foreach (Match spdx in SpdxLine().Matches(text))
        {
            var id = spdx.Groups["id"].Value;
            if (!AllowedSpdx.Contains(id))
            {
                problems.Add($"{path}: SPDX identifier '{id}' is not on the allowed list ({string.Join(", ", AllowedSpdx)})");
            }
        }

        if (!text.Contains("Ported from", StringComparison.Ordinal))
        {
            return problems;
        }

        if (!SpdxLine().IsMatch(text))
        {
            problems.Add($"{path}: says \"Ported from\" but has no SPDX-License-Identifier line");
        }

        var sources = PortedFrom().Matches(text).Select(m => m.Groups["source"].Value.TrimEnd('/', '.')).ToList();
        if (sources.Count == 0)
        {
            problems.Add($"{path}: says \"Ported from\" but names no source URL");
        }

        problems.AddRange(sources
            .Where(source => !notices.Contains(source, StringComparison.OrdinalIgnoreCase))
            .Select(source => $"{path}: source {source} is not listed in {NoticesFile}"));
        return problems;
    }

    private static IEnumerable<string> ProtocolFiles() =>
        Directory.EnumerateDirectories(Path.Combine(Root.FullName, "src"), "Samaat.Protocols.*")
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Order(StringComparer.Ordinal);

    [Fact]
    public void EveryProtocolFile_HasValidProvenance()
    {
        var notices = File.ReadAllText(Path.Combine(Root.FullName, NoticesFile));
        var files = ProtocolFiles().ToList();
        Assert.NotEmpty(files);

        var problems = files
            .SelectMany(f => Violations(Path.GetRelativePath(Root.FullName, f).Replace('\\', '/'), File.ReadAllText(f), notices))
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private const string Notices = "| Example | https://github.com/example/buds | abc1234 | GPL-3.0 | Foo.cs |";

    private const string GoodHeader = """
        // SPDX-License-Identifier: GPL-3.0-or-later
        // Ported from https://github.com/example/buds @ abc1234, src/Foo.kt, Copyright (c) 2024 Example Authors, GPL-3.0.
        // Changes: rewritten onto Samaat.Transport (IRfcommChannel/IGattClient) and Samaat.Core features.
        namespace Samaat.Protocols.Example;
        """;

    [Fact]
    public void AWellFormedProvenanceHeader_Passes() =>
        Assert.Empty(Violations("Foo.cs", GoodHeader, Notices));

    [Fact]
    public void AFileThatIsNotPorted_NeedsNoHeader() =>
        Assert.Empty(Violations("Bar.cs", "namespace Samaat.Protocols.Example;", Notices));

    [Fact]
    public void PortedFrom_WithoutAnSpdxLine_Fails()
    {
        // Drop the SPDX line by lines, not by "\n": on Windows git checks this file out with CRLF line endings.
        var text = string.Join('\n', GoodHeader.Split('\n').Where(l => !l.Contains("SPDX-License-Identifier", StringComparison.Ordinal)));

        Assert.Contains(Violations("Foo.cs", text, Notices), p => p.Contains("no SPDX-License-Identifier", StringComparison.Ordinal));
    }

    [Fact]
    public void PortedFrom_ASourceMissingFromTheNotices_Fails()
    {
        var text = GoodHeader.Replace("example/buds", "someone/else", StringComparison.Ordinal);

        Assert.Contains(Violations("Foo.cs", text, Notices), p => p.Contains("not listed in THIRD_PARTY_NOTICES.md", StringComparison.Ordinal));
    }

    [Fact]
    public void PortedFrom_WithNoSourceUrl_Fails()
    {
        var text = GoodHeader.Replace("https://github.com/example/buds", "a friend's repo", StringComparison.Ordinal);

        Assert.Contains(Violations("Foo.cs", text, Notices), p => p.Contains("names no source URL", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("AGPL-3.0-only")]
    [InlineData("AGPL-3.0-or-later")]
    [InlineData("GPL-2.0-only")]
    [InlineData("LicenseRef-Vendor")]
    public void AnSpdxIdentifierOutsideTheAllowedList_Fails(string id)
    {
        var text = GoodHeader.Replace("GPL-3.0-or-later", id, StringComparison.Ordinal);

        Assert.Contains(Violations("Foo.cs", text, Notices), p => p.Contains($"'{id}' is not on the allowed list", StringComparison.Ordinal));
    }
}
