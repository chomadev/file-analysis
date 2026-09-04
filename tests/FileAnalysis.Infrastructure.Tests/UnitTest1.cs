using FileAnalysis.Infrastructure.Scanning;
using FluentAssertions;

namespace FileAnalysis.Infrastructure.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("node_modules", "node_modules", true)]
    [InlineData("node_modules", "NODE_MODULES", true)]   // case-insensitive
    [InlineData("*.tmp", "cache.tmp", true)]
    [InlineData("*.tmp", "cache.TMP", true)]
    [InlineData("*.tmp", "cache.json", false)]
    [InlineData("*.log", "error.log", true)]
    [InlineData("*.log", "error.txt", false)]
    [InlineData(".git", ".git", true)]
    [InlineData(".git", ".gitignore", false)]
    [InlineData("obj", "obj", true)]
    [InlineData("obj", "objNew", false)]
    public void Matches_ReturnsExpected(string pattern, string name, bool expected)
    {
        GlobMatcher.Matches(pattern, name).Should().Be(expected);
    }
}

public class MimeTypeDetectorTests
{
    [Theory]
    [InlineData("file.txt", "text/plain")]
    [InlineData("file.md", "text/markdown")]
    [InlineData("file.json", "application/json")]
    [InlineData("file.cs", "text/x-csharp")]
    [InlineData("file.py", "text/x-python")]
    [InlineData("file.pdf", "application/pdf")]
    [InlineData("file.png", "image/png")]
    [InlineData("file.jpg", "image/jpeg")]
    [InlineData("file.MP4", "video/mp4")]
    [InlineData("file.unknown_extension_xyz", "application/octet-stream")]
    public void Detect_ByExtension_ReturnsExpectedMime(string filename, string expectedMime)
    {
        // We test with non-existent files — magic byte fallback won't be reached for known extensions
        MimeTypeDetector.Detect(filename).Should().Be(expectedMime);
    }
}
