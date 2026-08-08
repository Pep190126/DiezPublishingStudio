using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class EditorialTextExtractor
{
    public static async Task<string> ExtractAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".txt" or ".md" => await File.ReadAllTextAsync(path),
            ".docx" => ExtractDocx(path),
            ".odt" => ExtractOdt(path),
            ".rtf" => ExtractRtf(await File.ReadAllTextAsync(path)),
            _ => string.Empty
        };
    }

    private static string ExtractDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var documentEntry = archive.GetEntry("word/document.xml");
        if (documentEntry is null) return string.Empty;

        XDocument document;
        using (var stream = documentEntry.Open()) document = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        return string.Join(Environment.NewLine,
            document.Descendants(w + "p")
                .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Trim())
                .Where(text => text.Length > 0));
    }

    private static string ExtractOdt(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var contentEntry = archive.GetEntry("content.xml");
        if (contentEntry is null) return string.Empty;

        XDocument document;
        using (var stream = contentEntry.Open()) document = XDocument.Load(stream);
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        return string.Join(Environment.NewLine,
            document.Descendants()
                .Where(e => e.Name == text + "p" || e.Name == text + "h")
                .Select(e => string.Concat(e.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim())
                .Where(value => value.Length > 0));
    }

    private static string ExtractRtf(string rtf)
    {
        var output = new StringBuilder();
        for (var i = 0; i < rtf.Length; i++)
        {
            var ch = rtf[i];
            if (ch is '{' or '}') continue;
            if (ch != '\\')
            {
                if (ch != '\r' && ch != '\n') output.Append(ch);
                continue;
            }

            if (i + 1 >= rtf.Length) break;
            var next = rtf[++i];
            if (next is '\\' or '{' or '}')
            {
                output.Append(next);
                continue;
            }

            if (next == '\'' && i + 2 < rtf.Length)
            {
                var hex = rtf.Substring(i + 1, 2);
                if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                    output.Append((char)value);
                i += 2;
                continue;
            }

            if (!char.IsLetter(next))
            {
                if (next == '~') output.Append(' ');
                continue;
            }

            var word = new StringBuilder().Append(next);
            while (i + 1 < rtf.Length && char.IsLetter(rtf[i + 1])) word.Append(rtf[++i]);

            var sign = 1;
            if (i + 1 < rtf.Length && rtf[i + 1] == '-')
            {
                sign = -1;
                i++;
            }

            var number = 0;
            var hasNumber = false;
            while (i + 1 < rtf.Length && char.IsDigit(rtf[i + 1]))
            {
                hasNumber = true;
                number = number * 10 + (rtf[++i] - '0');
            }
            number *= sign;
            if (i + 1 < rtf.Length && rtf[i + 1] == ' ') i++;

            switch (word.ToString())
            {
                case "par":
                case "line":
                    output.AppendLine();
                    break;
                case "tab":
                    output.Append('\t');
                    break;
                case "u" when hasNumber:
                    output.Append((char)(number < 0 ? number + 65536 : number));
                    if (i + 1 < rtf.Length && rtf[i + 1] != '\\' && rtf[i + 1] != '{' && rtf[i + 1] != '}') i++;
                    break;
            }
        }

        return Regex.Replace(output.ToString(), @"[ \t]+", " ").Trim();
    }
}
