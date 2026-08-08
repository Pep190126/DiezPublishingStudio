using System.Text;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal static partial class ContentStructureAnalyzer
{
    public static List<ContentNode> Analyze(MaterialEntry material)
    {
        if (string.IsNullOrWhiteSpace(material.ExtractedText))
            return [];

        var normalized = material.ExtractedText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var nodes = new List<ContentNode>();
        var root = new ContentNode
        {
            MaterialId = material.MaterialId,
            Kind = "Document",
            Title = Path.GetFileNameWithoutExtension(material.FileName),
            Ordinal = 0,
            SourceLocator = material.FileName
        };
        nodes.Add(root);

        var intro = new StringBuilder();
        ContentNode? current = null;
        var currentBody = new StringBuilder();
        var ordinal = 1;

        void FlushCurrent()
        {
            if (current is null) return;
            current.Body = currentBody.ToString().Trim();
            nodes.Add(current);
            current = null;
            currentBody.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (TryClassifyHeading(line, out var kind, out var title))
            {
                FlushCurrent();
                current = new ContentNode
                {
                    MaterialId = material.MaterialId,
                    ParentId = root.ContentId,
                    Kind = kind,
                    Title = title,
                    Ordinal = ordinal++,
                    SourceLocator = $"{material.FileName} · riga {i + 1}"
                };
                continue;
            }

            if (current is null)
                intro.AppendLine(raw);
            else
                currentBody.AppendLine(raw);
        }

        FlushCurrent();
        root.Body = intro.ToString().Trim();

        if (nodes.Count == 1)
        {
            root.Body = normalized.Trim();
            root.SourceLocator = material.FileName + " · documento completo";
        }

        return nodes;
    }

    private static bool TryClassifyHeading(string line, out string kind, out string title)
    {
        kind = string.Empty;
        title = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || line.Length > 160) return false;

        var markdown = MarkdownHeadingRegex().Match(line);
        if (markdown.Success)
        {
            var level = markdown.Groups["marks"].Value.Length;
            kind = level <= 1 ? "Chapter" : "Section";
            title = markdown.Groups["title"].Value.Trim();
            return title.Length > 0;
        }

        var chapter = ChapterHeadingRegex().Match(line);
        if (chapter.Success)
        {
            kind = "Chapter";
            title = line.Trim();
            return true;
        }

        var part = PartHeadingRegex().Match(line);
        if (part.Success)
        {
            kind = "Part";
            title = line.Trim();
            return true;
        }

        var section = SectionHeadingRegex().Match(line);
        if (section.Success)
        {
            kind = "Section";
            title = line.Trim();
            return true;
        }

        var numbered = NumberedHeadingRegex().Match(line);
        if (numbered.Success && line.Length <= 100)
        {
            kind = "Section";
            title = line;
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"^(?<marks>#{1,6})\s+(?<title>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"^(capitolo|chapter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterHeadingRegex();

    [GeneratedRegex(@"^(parte|part)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartHeadingRegex();

    [GeneratedRegex(@"^(sezione|section)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"^\d+(?:\.\d+)*[\.)]\s+\S.+$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedHeadingRegex();
}
