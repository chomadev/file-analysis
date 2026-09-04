using FileAnalysis.Core.Options;
using FileAnalysis.Infrastructure.Extraction;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Tests;

public class ContentChunkerTests
{
    private static ContentChunker CreateChunker(int chunkSize = 100, int overlap = 10) =>
        new(Options.Create(new ExtractionOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlap = overlap,
            SingleChunkThresholdBytes = 51_200,
        }));

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var chunker = CreateChunker(chunkSize: 200);
        var text = "Hello, world! This is a short piece of text.";

        var result = chunker.Chunk(text);

        result.Should().HaveCount(1);
        result[0].Should().Be(text);
    }

    [Fact]
    public void Chunk_LongText_ReturnsMultipleChunksWithOverlap()
    {
        var chunker = CreateChunker(chunkSize: 50, overlap: 10);
        // Build a text longer than one chunk
        var text = string.Join(" ", Enumerable.Range(1, 30).Select(i => $"word{i}"));

        var result = chunker.Chunk(text);

        result.Should().HaveCountGreaterThan(1);
        // Verify overlap: the start of chunk[1] should contain text from the end of chunk[0]
        var endOfFirst = result[0][^Math.Min(20, result[0].Length)..];
        result[1].Should().Contain(endOfFirst.Split(' ').Last());
    }

    [Fact]
    public void Chunk_EmptyString_ReturnsSingleEmptyChunk()
    {
        var chunker = CreateChunker();

        var result = chunker.Chunk(string.Empty);

        result.Should().HaveCount(1);
        result[0].Should().Be(string.Empty);
    }

    [Fact]
    public void Chunk_TextExactlyChunkSize_ReturnsSingleChunk()
    {
        var chunker = CreateChunker(chunkSize: 20);
        var text = new string('a', 20);

        var result = chunker.Chunk(text);

        result.Should().HaveCount(1);
        result[0].Should().Be(text);
    }
}
