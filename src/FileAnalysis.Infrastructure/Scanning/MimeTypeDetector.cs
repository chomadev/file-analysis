namespace FileAnalysis.Infrastructure.Scanning;

/// <summary>Detects MIME type by extension and magic bytes.</summary>
public static class MimeTypeDetector
{
    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain", [".md"] = "text/markdown", [".csv"] = "text/csv",
        [".log"] = "text/plain", [".json"] = "application/json", [".xml"] = "application/xml",
        [".yaml"] = "application/yaml", [".yml"] = "application/yaml", [".toml"] = "application/toml",
        [".cs"] = "text/x-csharp", [".js"] = "text/javascript", [".ts"] = "text/typescript",
        [".py"] = "text/x-python", [".go"] = "text/x-go", [".rs"] = "text/x-rust",
        [".java"] = "text/x-java", [".cpp"] = "text/x-c++", [".c"] = "text/x-c",
        [".h"] = "text/x-c", [".sh"] = "text/x-sh", [".bat"] = "text/x-bat",
        [".ps1"] = "text/x-powershell", [".sql"] = "text/x-sql",
        [".html"] = "text/html", [".htm"] = "text/html", [".css"] = "text/css",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".zip"] = "application/zip", [".rar"] = "application/x-rar-compressed",
        [".7z"] = "application/x-7z-compressed", [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".bmp"] = "image/bmp", [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".mp4"] = "video/mp4", [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav",
        [".3mf"] = "model/3mf",
        [".exe"] = "application/x-msdownload", [".dll"] = "application/x-msdownload",
    };

    /// <summary>Detects the MIME type of a file by extension, then magic bytes.</summary>
    public static string Detect(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && ExtensionMap.TryGetValue(ext, out var mime))
            return mime;

        try
        {
            Span<byte> header = stackalloc byte[8];
            using var fs = File.OpenRead(filePath);
            var read = fs.Read(header);
            if (read >= 4)
            {
                if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
                    return "application/pdf";
                if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
                    return "application/zip";
                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                    return "image/png";
                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                    return "image/jpeg";
                if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
                    return "image/gif";
            }
        }
        catch { /* unreadable file — fall through */ }

        return "application/octet-stream";
    }
}
