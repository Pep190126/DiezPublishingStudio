using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal readonly record struct GoogleDocsExportResult(bool Success, string Message, string? DocumentUrl = null);

internal static class GoogleDocsConfiguration
{
    private const string EnvironmentName = "DIEZ_GOOGLE_OAUTH_CLIENT_ID";
    private const string BundledFileName = "google-oauth-client-id.txt";
    private const string UserFileName = "google-docs-client-id.txt";

    public static string? ClientId
    {
        get
        {
            var environment = Environment.GetEnvironmentVariable(EnvironmentName)?.Trim();
            if (LooksLikeClientId(environment)) return environment;

            var bundled = Read(Path.Combine(AppContext.BaseDirectory, BundledFileName));
            if (LooksLikeClientId(bundled)) return bundled;

            var user = Read(UserPath());
            return LooksLikeClientId(user) ? user : null;
        }
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    public static void SaveClientId(string clientId)
    {
        clientId = (clientId ?? string.Empty).Trim();
        if (!LooksLikeClientId(clientId))
            throw new InvalidOperationException("Il Client ID Google non sembra valido.");
        var path = UserPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, clientId, new UTF8Encoding(false));
    }

    internal static bool LooksLikeClientId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase) &&
        value.Length > 30;

    private static string UserPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiezPublishingStudio",
        UserFileName);

    private static string? Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : null; }
        catch { return null; }
    }
}

internal static class GoogleDocsExportService
{
    internal const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
    internal const string GoogleDocumentMime = "application/vnd.google-apps.document";
    internal const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string UploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,mimeType";

    public static async Task<GoogleDocsExportResult> ExportDocxAsync(
        string docxPath,
        string title,
        bool openInBrowser = true,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(docxPath))
            return new(false, "Il documento modificabile da inviare a Google non esiste.");

        var clientId = GoogleDocsConfiguration.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            return new(false, "Google Documenti non è ancora collegato in questa build di Diez.");

        try
        {
            var accessToken = await GetAccessTokenAsync(clientId, cancellationToken);
            var document = await UploadAndConvertAsync(accessToken, docxPath, title, cancellationToken);
            if (openInBrowser) OpenBrowser(document.Url);
            return new(true, $"Google Documento creato: {document.Name}. Si apre nel browser.", document.Url);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Collegamento a Google annullato.");
        }
        catch (Exception ex)
        {
            return new(false, "Creazione del Google Documento non riuscita: " + Friendly(ex.Message));
        }
    }

    internal static string BuildAuthorizationUrl(
        string clientId,
        string redirectUri,
        string state,
        string codeChallenge) =>
        AuthorizationEndpoint + "?" + string.Join("&", new[]
        {
            Pair("client_id", clientId),
            Pair("redirect_uri", redirectUri),
            Pair("response_type", "code"),
            Pair("scope", DriveFileScope),
            Pair("code_challenge", codeChallenge),
            Pair("code_challenge_method", "S256"),
            Pair("state", state),
            Pair("access_type", "offline"),
            Pair("prompt", "consent")
        });

    internal static string CreatePkceVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));
    internal static string CreatePkceChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static async Task<string> GetAccessTokenAsync(string clientId, CancellationToken cancellationToken)
    {
        var refreshToken = GoogleRefreshTokenStore.Read();
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            try { return await RefreshAsync(clientId, refreshToken, cancellationToken); }
            catch
            {
                GoogleRefreshTokenStore.Delete();
            }
        }
        return await AuthorizeInteractiveAsync(clientId, cancellationToken);
    }

    private static async Task<string> AuthorizeInteractiveAsync(string clientId, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/";
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var verifier = CreatePkceVerifier();
        var challenge = CreatePkceChallenge(verifier);
        var authorizationUrl = BuildAuthorizationUrl(clientId, redirectUri, state, challenge);
        OpenBrowser(authorizationUrl);

        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
        var firstLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) { }

        var target = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? "/";
        var uri = new Uri("http://127.0.0.1" + target);
        var query = ParseQuery(uri.Query);

        await SendBrowserResponseAsync(stream, query.ContainsKey("error")
            ? "Autorizzazione non concessa. Puoi chiudere questa scheda e tornare in Diez."
            : "Google Documenti è collegato a Diez. Puoi chiudere questa scheda.", cancellationToken);

        if (query.TryGetValue("error", out var error))
            throw new InvalidOperationException("Google ha rifiutato l'autorizzazione: " + error);
        if (!query.TryGetValue("state", out var returnedState) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(returnedState)))
            throw new InvalidOperationException("Risposta Google non valida: controllo di sicurezza fallito.");
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Google non ha restituito il codice di autorizzazione.");

        using var http = new HttpClient();
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        }), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(TokenError(json));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Token Google mancante.");
        if (root.TryGetProperty("refresh_token", out var refresh) && !string.IsNullOrWhiteSpace(refresh.GetString()))
            GoogleRefreshTokenStore.Write(refresh.GetString()!);
        return accessToken;
    }

    private static async Task<string> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(TokenError(json));
        using var document = JsonDocument.Parse(json);
        var accessToken = document.RootElement.GetProperty("access_token").GetString();
        return !string.IsNullOrWhiteSpace(accessToken)
            ? accessToken
            : throw new InvalidOperationException("Google non ha restituito un token di accesso.");
    }

    private static async Task<(string Name, string Url)> UploadAndConvertAsync(
        string accessToken,
        string docxPath,
        string title,
        CancellationToken cancellationToken)
    {
        title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(docxPath) : title.Trim();
        var metadata = JsonSerializer.Serialize(new { name = title, mimeType = GoogleDocumentMime });
        var bytes = await File.ReadAllBytesAsync(docxPath, cancellationToken);
        var boundary = "diez_" + Guid.NewGuid().ToString("N");

        using var multipart = new MultipartContent("related", boundary);
        var metadataContent = new StringContent(metadata, Encoding.UTF8, "application/json");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(DocxMime);
        multipart.Add(metadataContent);
        multipart.Add(fileContent);

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint) { Content = multipart };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ApiError(json));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Google Drive non ha restituito l'ID del documento.");
        var name = root.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString()! : title;
        return (name, $"https://docs.google.com/document/d/{id}/edit");
    }

    private static async Task SendBrowserResponseAsync(NetworkStream stream, string message, CancellationToken cancellationToken)
    {
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Diez</title></head><body style=\"font-family:system-ui;padding:3rem\"><h2>Diez Publishing Studio</h2><p>" +
                   System.Net.WebUtility.HtmlEncode(message) + "</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            result[key] = value;
        }
        return result;
    }

    private static string Pair(string name, string value) => Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private static string TokenError(string json) => ExtractGoogleError(json, "Autorizzazione Google non riuscita.");
    private static string ApiError(string json) => ExtractGoogleError(json, "Google Drive non ha accettato il documento.");

    private static string ExtractGoogleError(string json, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("error_description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()))
                return description.GetString()!;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString())) return error.GetString()!;
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && !string.IsNullOrWhiteSpace(message.GetString()))
                    return message.GetString()!;
            }
        }
        catch { }
        return fallback;
    }

    private static string Friendly(string message) => string.IsNullOrWhiteSpace(message) ? "errore sconosciuto" : message.Trim();
}

internal static class GoogleRefreshTokenStore
{
    private const string Target = "DiezPublishingStudio/GoogleDriveRefreshToken";
    private const uint CredentialTypeGeneric = 1;
    private const uint PersistLocalMachine = 2;

    public static string? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!CredRead(Target, CredentialTypeGeneric, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { CredFree(pointer); }
    }

    public static void Write(string refreshToken)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(refreshToken)) return;
        var bytes = Encoding.UTF8.GetBytes(refreshToken);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("Windows non ha potuto salvare in modo sicuro l'accesso Google.");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    public static void Delete()
    {
        if (OperatingSystem.IsWindows()) CredDelete(Target, CredentialTypeGeneric, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential userCredential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPtr);
}
