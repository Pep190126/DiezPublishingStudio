namespace DiezPublishingStudio;

internal static class EditionMetadataService
{
    public static EditionMetadataUpdateResult Update(
        PreviewProject project,
        string? title,
        string? subtitle,
        string? creator,
        string? language,
        string? publisher,
        string? isbn,
        string? description)
    {
        project.EditionMetadata ??= new EditionMetadata();

        var normalizedIsbn = NormalizeIsbn(isbn);
        if (!string.IsNullOrWhiteSpace(normalizedIsbn) && !IsValidIsbn(normalizedIsbn))
            return new EditionMetadataUpdateResult(false,
                "ISBN non valido. Inserisci un ISBN-10 o ISBN-13 corretto, oppure lascia il campo vuoto.");

        var next = new EditionMetadata
        {
            Title = Clean(title),
            Subtitle = Clean(subtitle),
            Creator = Clean(creator),
            Language = Clean(language),
            Publisher = Clean(publisher),
            Isbn = normalizedIsbn,
            Description = Clean(description)
        };

        if (Equivalent(project.EditionMetadata, next))
            return new EditionMetadataUpdateResult(false, "Nessuna modifica ai metadati dell'edizione.");

        project.EditionMetadata.Title = next.Title;
        project.EditionMetadata.Subtitle = next.Subtitle;
        project.EditionMetadata.Creator = next.Creator;
        project.EditionMetadata.Language = next.Language;
        project.EditionMetadata.Publisher = next.Publisher;
        project.EditionMetadata.Isbn = next.Isbn;
        project.EditionMetadata.Description = next.Description;

        return new EditionMetadataUpdateResult(true,
            "Metadati edizione aggiornati. Se esisteva un Edition Freeze, ora deve essere ricreato prima della pubblicazione.");
    }

    public static bool IsValidIsbn(string? value)
    {
        var isbn = NormalizeIsbn(value);
        if (isbn.Length == 10)
        {
            var sum = 0;
            for (var i = 0; i < 10; i++)
            {
                var ch = isbn[i];
                int digit;
                if (i == 9 && (ch == 'X' || ch == 'x')) digit = 10;
                else if (char.IsDigit(ch)) digit = ch - '0';
                else return false;
                sum += digit * (10 - i);
            }
            return sum % 11 == 0;
        }

        if (isbn.Length == 13 && isbn.All(char.IsDigit))
        {
            var sum = 0;
            for (var i = 0; i < 12; i++)
                sum += (isbn[i] - '0') * (i % 2 == 0 ? 1 : 3);
            var check = (10 - sum % 10) % 10;
            return check == isbn[12] - '0';
        }

        return false;
    }

    public static string NormalizeIsbn(string? value) =>
        new((value ?? string.Empty)
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '-')
            .ToArray());

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static bool Equivalent(EditionMetadata left, EditionMetadata right) =>
        string.Equals(left.Title, right.Title, StringComparison.Ordinal) &&
        string.Equals(left.Subtitle, right.Subtitle, StringComparison.Ordinal) &&
        string.Equals(left.Creator, right.Creator, StringComparison.Ordinal) &&
        string.Equals(left.Language, right.Language, StringComparison.Ordinal) &&
        string.Equals(left.Publisher, right.Publisher, StringComparison.Ordinal) &&
        string.Equals(left.Isbn, right.Isbn, StringComparison.Ordinal) &&
        string.Equals(left.Description, right.Description, StringComparison.Ordinal);
}

internal readonly record struct EditionMetadataUpdateResult(bool Changed, string Message);