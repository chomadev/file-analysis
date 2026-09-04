using FileAnalysis.Core.Models;
using FluentAssertions;

namespace FileAnalysis.Core.Tests;

public class IndexedFileTests
{
    [Fact]
    public void IndexedFile_DefaultId_IsNewGuid()
    {
        var file = new IndexedFile { Path = "/foo/bar.txt", Name = "bar.txt" };
        file.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void IndexedFile_DefaultIndexedAt_IsRecentUtc()
    {
        var before = DateTime.UtcNow;
        var file = new IndexedFile { Path = "/foo/bar.txt", Name = "bar.txt" };
        var after = DateTime.UtcNow;

        file.IndexedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void FileAnalysisResult_DefaultArrays_AreEmpty()
    {
        var result = new FileAnalysisResult { FileId = Guid.NewGuid() };
        result.Tags.Should().BeEmpty();
        result.Topics.Should().BeEmpty();
    }
}
