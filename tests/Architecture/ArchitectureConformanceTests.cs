using System.Text.RegularExpressions;
using Xunit;

namespace Photocore.Tests.Architecture;

// Conformance gates for the Core + heads modular split: Core owns mechanism and must stay
// head-agnostic; heads own policy and must stay thin. These scan source rather than reflect over
// the assemblies because the game DLLs are Private=False (absent from test output), so loading
// Core's block/item types would throw at test runtime.
public class ArchitectureConformanceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Photography.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Photography.sln not found above test bin");
    }

    private static IEnumerable<string> SourceFiles(string relativeDir)
        => Directory.EnumerateFiles(Path.Combine(RepoRoot, relativeDir), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // Comment-stripped so rules can be *discussed* in comments without tripping the gate.
    private static IEnumerable<(string File, int Line, string Code)> CodeLines(string relativeDir)
    {
        foreach (string file in SourceFiles(relativeDir))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i];
                int comment = code.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) code = code[..comment];
                if (code.Trim().Length > 0) yield return (file, i + 1, code);
            }
        }
    }

    // Every head zip bundles Photocore.dll, and VintageStory refuses a mod zip containing more
    // than one DLL with an instantiable ModSystem — so Core may only declare abstract ones.
    [Fact]
    public void CoreDeclaresNoConcreteModSystem()
    {
        var offenders = CodeLines("Core/src")
            .Where(l => Regex.IsMatch(l.Code, @"\bclass\s+\w+[^:{]*:[^{]*\b(ModSystem|PhotocoreModSystem)\b")
                        && !l.Code.Contains("abstract"))
            .Select(l => $"{l.File}:{l.Line}")
            .ToList();
        Assert.True(offenders.Count == 0, "Concrete ModSystem declared in Core:\n" + string.Join("\n", offenders));
    }

    // Core must never know which head loaded it: no head identity may appear in Core code.
    // (The baseline's item codes legitimately contain "collodion" — the chemical — so the gate
    // checks head identities, not the bare word.)
    [Fact]
    public void CoreNeverNamesAHead()
    {
        string[] headIdentities = ["kosphotograph", "CollodionMod"];
        var offenders = CodeLines("Core/src")
            .Where(l => headIdentities.Any(h => l.Code.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Select(l => $"{l.File}:{l.Line}: {l.Code.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Core source names a head:\n" + string.Join("\n", offenders));
    }

    // Heads are composition roots: policy (profiles, recipes, strategy implementations), never
    // feature logic. All Core feature logic lives in Photocore.<Feature> namespaces, so a head
    // declaring types there is feature logic escaping into a head — the superset trap.
    [Theory]
    [InlineData("Collodion")]
    [InlineData("Kosphotography")]
    public void HeadsDeclareNoCoreFeatureNamespaces(string headDir)
    {
        var offenders = CodeLines(headDir)
            .Where(l => Regex.IsMatch(l.Code, @"\bnamespace\s+Photocore\.\w"))
            .Select(l => $"{l.File}:{l.Line}: {l.Code.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Head declares a Core feature namespace:\n" + string.Join("\n", offenders));
    }

    // Shared assets live once in Core under the neutral photocore domain; each head ships only
    // its own domain. "game" is allowed anywhere for patches into the base game (e.g. remaps).
    [Theory]
    [InlineData("Core", "photocore")]
    [InlineData("Collodion", "collodion")]
    [InlineData("Kosphotography", "kosphotography")]
    public void AssetTreesStayInTheirOwnDomain(string project, string ownDomain)
    {
        string assetsRoot = Path.Combine(RepoRoot, project, "assets");
        var offenders = Directory.EnumerateDirectories(assetsRoot)
            .Select(d => Path.GetFileName(d)!)
            .Where(d => d != ownDomain && d != "game")
            .ToList();
        Assert.True(offenders.Count == 0, $"{project}/assets contains foreign domain(s): " + string.Join(", ", offenders));
    }
}
