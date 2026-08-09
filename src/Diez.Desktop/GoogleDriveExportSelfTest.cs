namespace DiezPublishingStudio;

internal static class GoogleDriveExportSelfTest
{
    public static void Run()
    {
        if (!GoogleDocsConfiguration.LooksLikeClientId("1234567890-test.apps.googleusercontent.com"))
            throw new InvalidOperationException("Google client ID validation rejected a valid desktop client ID shape.");
        if (GoogleDocsConfiguration.LooksLikeClientId("not-a-google-client"))
            throw new InvalidOperationException("Google client ID validation accepted an invalid value.");

        var verifier = GoogleDocsExportService.CreatePkceVerifier();
        var challenge = GoogleDocsExportService.CreatePkceChallenge(verifier);
        if (verifier.Length < 43 || challenge.Length < 40)
            throw new InvalidOperationException("Google PKCE verifier/challenge is too short.");
        if (challenge.Contains('+') || challenge.Contains('/') || challenge.Contains('='))
            throw new InvalidOperationException("Google PKCE challenge is not base64url encoded.");

        var url = GoogleDocsExportService.BuildAuthorizationUrl(
            "1234567890-test.apps.googleusercontent.com",
            "http://127.0.0.1:54321/",
            "state-test",
            challenge);
        var decoded = Uri.UnescapeDataString(url);
        if (!decoded.Contains(GoogleDocsExportService.DriveFileScope, StringComparison.Ordinal))
            throw new InvalidOperationException("Google authorization does not request the limited drive.file scope.");
        if (!url.Contains("code_challenge_method=S256", StringComparison.Ordinal))
            throw new InvalidOperationException("Google authorization is missing PKCE S256.");
        if (!url.Contains("redirect_uri=", StringComparison.Ordinal) || !decoded.Contains("127.0.0.1:54321", StringComparison.Ordinal))
            throw new InvalidOperationException("Google desktop loopback redirect is missing.");

        if (!string.Equals(GoogleDocsExportService.DocxMime,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.Ordinal) ||
            !string.Equals(GoogleDocsExportService.XlsxMime,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.Ordinal))
            throw new InvalidOperationException("Office MIME types must preserve DOCX/XLSX instead of forcing Google conversion.");
        if (!string.Equals(GoogleDocsExportService.GoogleSpreadsheetMime,
                "application/vnd.google-apps.spreadsheet", StringComparison.Ordinal))
            throw new InvalidOperationException("CSV-to-Sheets conversion MIME is missing.");
    }
}
