using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DiezPublishingStudio;

internal static class VisualAssetValidationStatuses
{
    public const string Passed = "PASSED";
    public const string Failed = "FAILED";
    public const string NeedsReview = "NEEDS_REVIEW";
    public const string NotRequired = "NOT_REQUIRED";
}

internal readonly record struct VisualAssetValidationResult(
    string Status,
    bool BlocksApproval,
    string Message,
    int Width,
    int Height,
    double ChromaticRatio,
    double IntermediateToneRatio,
    double BlackRatio,
    double WhiteRatio,
    double TransparentRatio)
{
    public bool Passed => string.Equals(Status, VisualAssetValidationStatuses.Passed, StringComparison.Ordinal);
}

internal static class VisualAssetValidationStore
{
    private const string EntityKind = "DiezVisualAssetValidation";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    internal sealed class State
    {
        public int SchemaVersion { get; set; } = 1;
        public List<Record> Records { get; set; } = [];
    }

    internal sealed class Record
    {
        public Guid VersionId { get; set; }
        public Guid WorkUnitId { get; set; }
        public string Status { get; set; } = VisualAssetValidationStatuses.NotRequired;
        public bool BlocksApproval { get; set; }
        public string Message { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public double ChromaticRatio { get; set; }
        public double IntermediateToneRatio { get; set; }
        public double BlackRatio { get; set; }
        public double WhiteRatio { get; set; }
        public double TransparentRatio { get; set; }
        public string CheckedAtLocal { get; set; } = string.Empty;
    }

    public static void Save(
        PreviewProject project,
        Guid versionId,
        Guid workUnitId,
        VisualAssetValidationResult validation)
    {
        var state = Load(project);
        var record = state.Records.FirstOrDefault(r => r.VersionId == versionId);
        if (record is null)
        {
            record = new Record { VersionId = versionId, WorkUnitId = workUnitId };
            state.Records.Add(record);
        }
        record.WorkUnitId = workUnitId;
        record.Status = validation.Status;
        record.BlocksApproval = validation.BlocksApproval;
        record.Message = validation.Message;
        record.Width = validation.Width;
        record.Height = validation.Height;
        record.ChromaticRatio = validation.ChromaticRatio;
        record.IntermediateToneRatio = validation.IntermediateToneRatio;
        record.BlackRatio = validation.BlackRatio;
        record.WhiteRatio = validation.WhiteRatio;
        record.TransparentRatio = validation.TransparentRatio;
        record.CheckedAtLocal = DateTimeOffset.Now.ToString("O");
        SaveState(project, state);
    }

    public static Record? Get(PreviewProject project, Guid versionId) =>
        Load(project).Records.FirstOrDefault(r => r.VersionId == versionId);

    private static State Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new State();
        try
        {
            var state = JsonSerializer.Deserialize<State>(entity.Notes, JsonOptions) ?? new State();
            state.Records ??= [];
            return state;
        }
        catch { return new State(); }
    }

    private static void SaveState(PreviewProject project, State state)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Validazione asset visuali",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }
}

/// <summary>
/// Deterministic first-line validation of the REAL returned image, never of the provider description.
/// It checks dimensions/aspect ratio for all decodable images and enforces pixel-level color rules for
/// Coloring Book and pure B/W or grayscale illustration modes. Semantic vision validation can be added
/// above this layer, but a manifest cannot override a deterministic pixel failure.
/// </summary>
internal static class VisualAssetValidationService
{
    private const int MaxPngCompressedBytes = 192 * 1024 * 1024;
    private const long MaxPixels = 160_000_000;
    private const int MaxSamples = 1_000_000;

    public static VisualAssetValidationResult Validate(
        PreviewProject project,
        AiExchangeWorkUnit unit,
        string? assetPath)
    {
        if (!string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            return Result(VisualAssetValidationStatuses.NotRequired, false, "Validazione raster non richiesta per contenuto non immagine.");
        if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
            return Result(VisualAssetValidationStatuses.Failed, true, "Asset immagine reale assente: impossibile validare il risultato.");

        RasterStats raster;
        try
        {
            raster = string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase)
                ? PngInspector.Inspect(assetPath)
                : InspectWithAvalonia(assetPath);
        }
        catch (Exception ex)
        {
            return Result(
                VisualAssetValidationStatuses.NeedsReview,
                true,
                $"Diez non riesce a decodificare/analizzare pixel per pixel l'asset reale ({ex.GetBaseException().Message}). Il risultato resta da verificare e non viene approvato automaticamente.");
        }

        var technical = TechnicalSpec.Load(project);
        var hardIssues = new List<string>();
        var reviewIssues = new List<string>();

        ValidateAspectAndResolution(raster, technical, hardIssues, reviewIssues);

        var bookType = BookTypeProfileService.Get(project);
        if (string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            ValidatePureBlackWhite(raster, hardIssues, reviewIssues, "Coloring Book");
            ValidateColoringInkBalance(raster, hardIssues, reviewIssues);
        }
        else
        {
            var profile = ImageCollectionPromptProfileService.Load(project);
            if (profile.ColorMode.StartsWith("Bianco e nero puro", StringComparison.OrdinalIgnoreCase))
                ValidatePureBlackWhite(raster, hardIssues, reviewIssues, "Bianco e nero puro");
            else if (profile.ColorMode.StartsWith("Scala di grigi", StringComparison.OrdinalIgnoreCase))
                ValidateGrayscale(raster, hardIssues, reviewIssues);
        }

        if (hardIssues.Count > 0)
        {
            return new VisualAssetValidationResult(
                VisualAssetValidationStatuses.Failed,
                true,
                "VALIDAZIONE ASSET FALLITA — " + string.Join(" ", hardIssues) + Metrics(raster),
                raster.Width,
                raster.Height,
                raster.ChromaticRatio,
                raster.IntermediateToneRatio,
                raster.BlackRatio,
                raster.WhiteRatio,
                raster.TransparentRatio);
        }

        if (reviewIssues.Count > 0)
        {
            return new VisualAssetValidationResult(
                VisualAssetValidationStatuses.NeedsReview,
                true,
                "ASSET DA VERIFICARE — " + string.Join(" ", reviewIssues) + Metrics(raster),
                raster.Width,
                raster.Height,
                raster.ChromaticRatio,
                raster.IntermediateToneRatio,
                raster.BlackRatio,
                raster.WhiteRatio,
                raster.TransparentRatio);
        }

        return new VisualAssetValidationResult(
            VisualAssetValidationStatuses.Passed,
            false,
            "Asset reale verificato sui pixel: compatibile con i vincoli deterministici del Tipo libro e con le specifiche tecniche correnti." + Metrics(raster),
            raster.Width,
            raster.Height,
            raster.ChromaticRatio,
            raster.IntermediateToneRatio,
            raster.BlackRatio,
            raster.WhiteRatio,
            raster.TransparentRatio);
    }

    private static void ValidateAspectAndResolution(
        RasterStats raster,
        TechnicalSpec technical,
        List<string> hard,
        List<string> review)
    {
        if (TryRatio(technical.AspectRatio, out var expected) && raster.Height > 0)
        {
            var actual = raster.Width / (double)raster.Height;
            var diff = Math.Abs(actual - expected) / expected;
            if (diff > 0.035)
                hard.Add($"Aspect ratio reale {raster.Width}:{raster.Height} incompatibile con {technical.AspectRatio} ({diff:P1} di differenza). ");
            else if (diff > 0.015)
                review.Add($"Aspect ratio reale {raster.Width}:{raster.Height} si discosta leggermente da {technical.AspectRatio} ({diff:P1}). ");
        }

        if (int.TryParse(technical.PixelWidth, out var targetW) && targetW > 0 &&
            int.TryParse(technical.PixelHeight, out var targetH) && targetH > 0)
        {
            var relative = Math.Min(raster.Width / (double)targetW, raster.Height / (double)targetH);
            if (relative < 0.50)
                review.Add($"Risoluzione reale {raster.Width}×{raster.Height}px molto inferiore al target {targetW}×{targetH}px. ");
            else if (relative < 0.85)
                review.Add($"Risoluzione reale {raster.Width}×{raster.Height}px inferiore al target {targetW}×{targetH}px. ");
        }
    }

    private static void ValidatePureBlackWhite(
        RasterStats raster,
        List<string> hard,
        List<string> review,
        string context)
    {
        if (raster.ChromaticRatio > 0.003)
            hard.Add($"{context}: rilevati pixel cromatici ({raster.ChromaticRatio:P1}); sono ammessi solo nero e bianco. ");
        if (raster.IntermediateToneRatio > 0.06)
            hard.Add($"{context}: troppi toni intermedi/grigi ({raster.IntermediateToneRatio:P1}). ");
        else if (raster.IntermediateToneRatio > 0.015)
            review.Add($"{context}: sono presenti toni intermedi/antialiasing ({raster.IntermediateToneRatio:P1}); il deliverable richiesto è binario. ");
        if (raster.TransparentRatio > 0.005)
            hard.Add($"{context}: trasparenza rilevata ({raster.TransparentRatio:P1}); è richiesto fondo bianco opaco. ");
    }

    private static void ValidateGrayscale(RasterStats raster, List<string> hard, List<string> review)
    {
        if (raster.ChromaticRatio > 0.02)
            hard.Add($"Scala di grigi richiesta ma l'asset contiene colore cromatico significativo ({raster.ChromaticRatio:P1}). ");
        else if (raster.ChromaticRatio > 0.005)
            review.Add($"Scala di grigi richiesta: rilevata una piccola componente cromatica ({raster.ChromaticRatio:P1}). ");
    }

    private static void ValidateColoringInkBalance(RasterStats raster, List<string> hard, List<string> review)
    {
        if (raster.BlackRatio < 0.004)
            hard.Add("La pagina è quasi vuota: quantità di tratto nero insufficiente per una tavola coloring. ");
        else if (raster.BlackRatio < 0.012)
            review.Add("La pagina contiene pochissimo tratto nero; verificare che il soggetto sia realmente leggibile e completo. ");
        if (raster.WhiteRatio < 0.45)
            hard.Add($"Troppa area non bianca/nera piena per una tavola da colorare (bianco {raster.WhiteRatio:P1}); possibile fotografia, riempimento o massa nera eccessiva. ");
        if (raster.BlackRatio > 0.42)
            hard.Add($"Massa nera eccessiva ({raster.BlackRatio:P1}) per una pagina da colorare: probabile resa non colorabile o immagine thresholded non adatta. ");
    }

    private static RasterStats InspectWithAvalonia(string path)
    {
        using var source = new Bitmap(path);
        var size = source.PixelSize;
        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > MaxPixels)
            throw new InvalidDataException("Dimensioni raster non valide o eccessive.");

        using var copy = new WriteableBitmap(
            size,
            new Vector(96, 96),
            PixelFormats.Bgra8888,
            AlphaFormat.Unpremul);
        using var frame = copy.Lock();
        source.CopyPixels(frame, AlphaFormat.Unpremul);
        var bytes = checked(frame.RowBytes * frame.Size.Height);
        var buffer = new byte[bytes];
        Marshal.Copy(frame.Address, buffer, 0, buffer.Length);

        var counter = new PixelCounter(size.Width, size.Height);
        var step = SampleStep(size.Width, size.Height);
        for (var y = 0; y < size.Height; y += step)
        {
            var row = y * frame.RowBytes;
            for (var x = 0; x < size.Width; x += step)
            {
                var i = row + x * 4;
                counter.Add(buffer[i + 2], buffer[i + 1], buffer[i], buffer[i + 3]);
            }
        }
        return counter.Finish();
    }

    private static int SampleStep(int width, int height)
    {
        var pixels = Math.Max(1L, (long)width * height);
        if (pixels <= MaxSamples) return 1;
        return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pixels / (double)MaxSamples)));
    }

    private static string Metrics(RasterStats r) =>
        $" [raster {r.Width}×{r.Height}; colore {r.ChromaticRatio:P2}; toni intermedi {r.IntermediateToneRatio:P2}; nero {r.BlackRatio:P2}; bianco {r.WhiteRatio:P2}; trasparenza {r.TransparentRatio:P2}]";

    private static VisualAssetValidationResult Result(string status, bool blocks, string message) =>
        new(status, blocks, message, 0, 0, 0, 0, 0, 0, 0);

    private static bool TryRatio(string? value, out double ratio)
    {
        ratio = 0;
        var text = (value ?? string.Empty).Trim();
        var parts = text.Split(':', '/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) &&
            a > 0 && b > 0)
        {
            ratio = a / b;
            return true;
        }
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ratio) && ratio > 0;
    }

    private sealed class TechnicalSpec
    {
        public string AspectRatio { get; set; } = string.Empty;
        public string PixelWidth { get; set; } = string.Empty;
        public string PixelHeight { get; set; } = string.Empty;

        public static TechnicalSpec Load(PreviewProject project)
        {
            var notes = project.Entities.FirstOrDefault(e =>
                string.Equals(e.Kind, "DiezImageGenerationSpecs", StringComparison.OrdinalIgnoreCase))?.Notes;
            if (string.IsNullOrWhiteSpace(notes)) return new TechnicalSpec();
            try
            {
                return JsonSerializer.Deserialize<TechnicalSpec>(notes, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new TechnicalSpec();
            }
            catch { return new TechnicalSpec(); }
        }
    }

    private readonly record struct RasterStats(
        int Width,
        int Height,
        double ChromaticRatio,
        double IntermediateToneRatio,
        double BlackRatio,
        double WhiteRatio,
        double TransparentRatio);

    private sealed class PixelCounter
    {
        private readonly int _width;
        private readonly int _height;
        private long _sampled;
        private long _chromatic;
        private long _intermediate;
        private long _black;
        private long _white;
        private long _transparent;

        public PixelCounter(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public void Add(byte r, byte g, byte b, byte a)
        {
            _sampled++;
            if (a < 250) _transparent++;
            if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 10) _chromatic++;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (max <= 12) _black++;
            else if (min >= 243) _white++;
            else _intermediate++;
        }

        public RasterStats Finish()
        {
            var n = Math.Max(1L, _sampled);
            return new RasterStats(
                _width,
                _height,
                _chromatic / (double)n,
                _intermediate / (double)n,
                _black / (double)n,
                _white / (double)n,
                _transparent / (double)n);
        }
    }

    private static class PngInspector
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

        public static RasterStats Inspect(string path)
        {
            using var file = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            file.ReadExactly(signature);
            if (!signature.SequenceEqual(Signature)) throw new InvalidDataException("Firma PNG non valida.");

            var idat = new MemoryStream();
            byte[]? palette = null;
            byte[]? paletteAlpha = null;
            var width = 0;
            var height = 0;
            byte bitDepth = 0;
            byte colorType = 0;
            byte interlace = 0;

            while (file.Position < file.Length)
            {
                Span<byte> lenBytes = stackalloc byte[4];
                file.ReadExactly(lenBytes);
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(lenBytes));
                Span<byte> typeBytes = stackalloc byte[4];
                file.ReadExactly(typeBytes);
                var type = Encoding.ASCII.GetString(typeBytes);
                if (length < 0 || length > MaxPngCompressedBytes)
                    throw new InvalidDataException("Chunk PNG eccessivo.");
                var data = new byte[length];
                file.ReadExactly(data);
                file.Position += 4; // CRC: the decoder below is intentionally read-only; platform decoder will reject corrupt files in normal use.

                switch (type)
                {
                    case "IHDR":
                        if (data.Length != 13) throw new InvalidDataException("IHDR PNG non valido.");
                        width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4)));
                        height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4)));
                        bitDepth = data[8];
                        colorType = data[9];
                        if (data[10] != 0 || data[11] != 0) throw new InvalidDataException("Metodo PNG non supportato.");
                        interlace = data[12];
                        break;
                    case "PLTE": palette = data; break;
                    case "tRNS": paletteAlpha = data; break;
                    case "IDAT":
                        if (idat.Length + data.Length > MaxPngCompressedBytes)
                            throw new InvalidDataException("IDAT PNG eccessivo.");
                        idat.Write(data);
                        break;
                    case "IEND": goto Decode;
                }
            }

        Decode:
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
                throw new InvalidDataException("Dimensioni PNG non valide o eccessive.");
            if (interlace != 0) throw new NotSupportedException("PNG interlacciato: validazione pixel deterministica non ancora supportata.");
            var bitsPerPixel = BitsPerPixel(colorType, bitDepth);
            if (bitsPerPixel <= 0) throw new NotSupportedException($"PNG color type {colorType}, bit depth {bitDepth} non supportato.");
            var rowBytes = checked((width * bitsPerPixel + 7) / 8);
            var bpp = Math.Max(1, (bitsPerPixel + 7) / 8);
            var previous = new byte[rowBytes];
            var row = new byte[rowBytes];
            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true);
            var counter = new PixelCounter(width, height);
            var step = SampleStep(width, height);

            for (var y = 0; y < height; y++)
            {
                var filter = z.ReadByte();
                if (filter < 0) throw new EndOfStreamException("PNG troncato prima del filtro scanline.");
                z.ReadExactly(row);
                Unfilter((byte)filter, row, previous, bpp);
                if (y % step == 0)
                {
                    for (var x = 0; x < width; x += step)
                    {
                        var rgba = Pixel(row, x, colorType, bitDepth, palette, paletteAlpha);
                        counter.Add(rgba.R, rgba.G, rgba.B, rgba.A);
                    }
                }
                (previous, row) = (row, previous);
            }
            return counter.Finish();
        }

        private static int BitsPerPixel(byte type, byte depth) => type switch
        {
            0 when depth is 1 or 2 or 4 or 8 or 16 => depth,
            2 when depth is 8 or 16 => 3 * depth,
            3 when depth is 1 or 2 or 4 or 8 => depth,
            4 when depth is 8 or 16 => 2 * depth,
            6 when depth is 8 or 16 => 4 * depth,
            _ => 0
        };

        private static void Unfilter(byte filter, byte[] row, byte[] previous, int bpp)
        {
            switch (filter)
            {
                case 0: return;
                case 1:
                    for (var i = 0; i < row.Length; i++)
                        row[i] = unchecked((byte)(row[i] + (i >= bpp ? row[i - bpp] : 0)));
                    return;
                case 2:
                    for (var i = 0; i < row.Length; i++)
                        row[i] = unchecked((byte)(row[i] + previous[i]));
                    return;
                case 3:
                    for (var i = 0; i < row.Length; i++)
                    {
                        var left = i >= bpp ? row[i - bpp] : 0;
                        var up = previous[i];
                        row[i] = unchecked((byte)(row[i] + ((left + up) >> 1)));
                    }
                    return;
                case 4:
                    for (var i = 0; i < row.Length; i++)
                    {
                        var a = i >= bpp ? row[i - bpp] : 0;
                        var b = previous[i];
                        var c = i >= bpp ? previous[i - bpp] : 0;
                        row[i] = unchecked((byte)(row[i] + Paeth(a, b, c)));
                    }
                    return;
                default: throw new InvalidDataException($"Filtro PNG sconosciuto: {filter}.");
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static Rgba Pixel(byte[] row, int x, byte type, byte depth, byte[]? palette, byte[]? alpha)
        {
            switch (type)
            {
                case 0:
                {
                    var g = depth <= 8 ? ScaledSample(row, x, depth) : row[x * 2];
                    return new Rgba(g, g, g, 255);
                }
                case 2:
                {
                    var step = depth == 16 ? 6 : 3;
                    var i = x * step;
                    return new Rgba(row[i], row[i + (depth == 16 ? 2 : 1)], row[i + (depth == 16 ? 4 : 2)], 255);
                }
                case 3:
                {
                    if (palette is null) throw new InvalidDataException("PNG indexed senza PLTE.");
                    var index = RawSample(row, x, depth);
                    var p = index * 3;
                    if (p + 2 >= palette.Length) throw new InvalidDataException("Indice palette PNG fuori range.");
                    var a = alpha is not null && index < alpha.Length ? alpha[index] : (byte)255;
                    return new Rgba(palette[p], palette[p + 1], palette[p + 2], a);
                }
                case 4:
                {
                    var i = x * (depth == 16 ? 4 : 2);
                    var g = row[i];
                    var a = row[i + (depth == 16 ? 2 : 1)];
                    return new Rgba(g, g, g, a);
                }
                case 6:
                {
                    var step = depth == 16 ? 8 : 4;
                    var i = x * step;
                    return new Rgba(
                        row[i],
                        row[i + (depth == 16 ? 2 : 1)],
                        row[i + (depth == 16 ? 4 : 2)],
                        row[i + (depth == 16 ? 6 : 3)]);
                }
                default: throw new NotSupportedException("Color type PNG non supportato.");
            }
        }

        private static byte ScaledSample(byte[] row, int x, byte depth)
        {
            if (depth == 8) return row[x];
            var raw = RawSample(row, x, depth);
            var max = (1 << depth) - 1;
            return (byte)Math.Round(raw * 255d / max);
        }

        private static int RawSample(byte[] row, int x, byte depth)
        {
            if (depth == 8) return row[x];
            var bit = x * depth;
            var byteIndex = bit >> 3;
            var shift = 8 - depth - (bit & 7);
            return (row[byteIndex] >> shift) & ((1 << depth) - 1);
        }

        private readonly record struct Rgba(byte R, byte G, byte B, byte A);
    }
}
