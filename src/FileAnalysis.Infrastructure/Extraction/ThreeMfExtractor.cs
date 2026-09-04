using System.IO.Compression;
using System.Net;
using System.Xml;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Options;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Extraction;

/// <summary>
/// Extracts metadata from .3mf files (a ZIP container). 3MF has no readable "text" per se — the mesh
/// geometry itself is meaningless to an LLM — but the small root model XML (3D/3dmodel.model) commonly
/// carries a &lt;metadata&gt; block (Title, Description, Application, ...) populated by slicers like
/// Bambu Studio / PrusaSlicer, which is a much stronger signal than the filename alone. When present, the
/// slicer-rendered thumbnail is also described via a vision model (llava) — the closest local equivalent
/// to a human glancing at the preview image in a file browser.
/// </summary>
public class ThreeMfExtractor(IOllamaClient ollama, IOptions<OllamaOptions> options) : IContentExtractor
{
    private static readonly string[] FallbackThumbnailPaths = ["Metadata/plate_1.png", "Metadata/thumbnail.png"];

    private const string VisionPrompt =
        "Describe the physical object(s) shown in this 3D-printable model preview in one or two concise " +
        "sentences. Focus on what the object actually is, not the rendering style, plate, or background.";

    private readonly OllamaOptions _options = options.Value;

    public IReadOnlyList<string> SupportedMimeTypes { get; } = ["model/3mf"];

    public async Task<string?> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        Dictionary<string, string> metadata;
        byte[]? thumbnail;

        try
        {
            using var zip = ZipFile.OpenRead(filePath);

            var modelEntry = zip.GetEntry("3D/3dmodel.model");
            metadata = modelEntry is not null ? ReadModelMetadata(modelEntry) : [];
            thumbnail = ReadThumbnail(zip, metadata);
        }
        catch
        {
            return null;
        }

        var parts = new List<string>();
        if (metadata.Count > 0)
            parts.Add(string.Join("\n", metadata.Select(kv => $"{kv.Key}: {kv.Value}")));

        if (thumbnail is not null)
        {
            try
            {
                var description = await ollama.DescribeImageAsync(_options.VisionModel, thumbnail, VisionPrompt, ct);
                if (!string.IsNullOrWhiteSpace(description))
                    parts.Add($"Visual description of preview image: {description.Trim()}");
            }
            catch
            {
                // Vision model unavailable/failed — degrade to metadata-only, still useful.
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    /// <summary>Reads the &lt;metadata name="..."&gt;value&lt;/metadata&gt; entries from the 3MF core spec's root model element.</summary>
    private static Dictionary<string, string> ReadModelMetadata(ZipArchiveEntry entry)
    {
        var result = new Dictionary<string, string>();

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            // Metadata only ever appears before <resources> (which holds the — potentially huge — mesh data).
            if (reader.LocalName == "resources")
                break;

            if (reader.LocalName != "metadata")
                continue;

            var name = reader.GetAttribute("name");
            var value = reader.IsEmptyElement ? null : reader.ReadElementContentAsString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                continue;

            // Some slicer exporters HTML-escape rich text fields (e.g. Description) more than once.
            result[name] = WebUtility.HtmlDecode(WebUtility.HtmlDecode(value));
        }

        return result;
    }

    /// <summary>Locates the slicer-rendered preview image, if any, and reads its bytes.</summary>
    private static byte[]? ReadThumbnail(ZipArchive zip, Dictionary<string, string> metadata)
    {
        var candidates = new List<string>();
        if (metadata.TryGetValue("Thumbnail_Middle", out var tm))
            candidates.Add(tm.TrimStart('/'));
        candidates.AddRange(FallbackThumbnailPaths);

        var entry = candidates.Select(zip.GetEntry).FirstOrDefault(e => e is not null);
        if (entry is null)
            return null;

        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
