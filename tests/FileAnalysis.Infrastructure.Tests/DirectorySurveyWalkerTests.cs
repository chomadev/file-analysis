using FileAnalysis.Core.Models;
using FileAnalysis.Infrastructure.Survey;
using FluentAssertions;

namespace FileAnalysis.Infrastructure.Tests;

public class DirectorySurveyWalkerTests
{
    private static string CreateTempTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "survey-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(root, "src"));

        File.WriteAllText(Path.Combine(root, "readme.md"), "hello");
        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "index.js"), "module.exports = {};");
        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "package.json"), "{}");
        File.WriteAllText(Path.Combine(root, "src", "main.cs"), "class Program {}");

        return root;
    }

    [Fact]
    public void Walk_HeuristicMatch_ExcludesAndDoesNotRecurseInto()
    {
        var root = CreateTempTree();
        try
        {
            var entries = DirectorySurveyWalker.Walk(root, ["node_modules"], maxDepth: 6);

            var nodeModules = entries.Should().ContainSingle(e => e.Path.EndsWith("node_modules")).Subject;
            nodeModules.Classification.Should().Be(DirectoryClassification.LikelyJunk);
            nodeModules.ClassificationSource.Should().Be(ClassificationSource.Heuristic);
            nodeModules.ExcludeDecision.Should().BeTrue();
            nodeModules.TotalFileCount.Should().Be(2);

            // The heuristic-matched directory's own children are never surveyed individually.
            entries.Should().NotContain(e => e.Path.Contains("pkg"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Walk_NonMatchingDirectory_IsUnclassifiedAndRecursedInto()
    {
        var root = CreateTempTree();
        try
        {
            var entries = DirectorySurveyWalker.Walk(root, ["node_modules"], maxDepth: 6);

            var src = entries.Should().ContainSingle(e => e.Path.EndsWith("src")).Subject;
            src.Classification.Should().Be(DirectoryClassification.Unclassified);
            src.ExcludeDecision.Should().BeNull();
            src.DirectFileCount.Should().Be(1);
            src.TopExtensions.Should().Contain(".cs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Walk_BeyondMaxDepth_StopsCreatingEntriesButStillCountsTotals()
    {
        var root = CreateTempTree();
        try
        {
            var entries = DirectorySurveyWalker.Walk(root, [], maxDepth: 0);

            entries.Should().ContainSingle(); // only the root itself
            var rootEntry = entries[0];
            rootEntry.TotalFileCount.Should().Be(4); // readme.md + the 3 files under node_modules/src
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
