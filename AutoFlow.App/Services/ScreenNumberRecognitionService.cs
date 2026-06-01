using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AutoFlow.App.Models;
using Tesseract;

namespace AutoFlow.App.Services;

public sealed class ScreenNumberRecognitionService : IDisposable
{
    private static readonly Regex IntegerPattern = new(@"[-+]?\d+", RegexOptions.Compiled);
    private static readonly Regex FloatPattern = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);
    private readonly Dictionary<string, TesseractEngine> _engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();
    private readonly PathService _pathService;
    private readonly ScreenCaptureService _screenCaptureService;
    private bool _disposed;

    public ScreenNumberRecognitionService(
        ScreenCaptureService screenCaptureService,
        PathService pathService)
    {
        _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
    }

    public bool TryReadNumber(
        int x,
        int y,
        int width,
        int height,
        ScreenNumberReadOptions options,
        out double value)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        using var capture = _screenCaptureService.CaptureRegion(x, y, width, height);
        using var processed = PreprocessImage(capture, options);
        var recognizedText = RecognizeText(processed, options);
        return TryParseRecognizedValue(recognizedText, options, out value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_syncRoot)
        {
            foreach (var engine in _engines.Values)
            {
                engine.Dispose();
            }

            _engines.Clear();
            _disposed = true;
        }
    }

    private string RecognizeText(Bitmap image, ScreenNumberReadOptions options)
    {
        using var imageStream = new MemoryStream();
        image.Save(imageStream, System.Drawing.Imaging.ImageFormat.Png);
        var imageBytes = imageStream.ToArray();

        lock (_syncRoot)
        {
            var engine = GetOrCreateEngine(options.Language);
            engine.DefaultPageSegMode = PageSegMode.SingleLine;
            engine.SetVariable("tessedit_char_whitelist", options.CharacterWhitelist);
            engine.SetVariable("classify_bln_numeric_mode", IsDigitsOnly(options.CharacterWhitelist) ? "1" : "0");

            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(pix);
            var text = page.GetText() ?? string.Empty;
            return options.TrimResult ? text.Trim() : text;
        }
    }

    private TesseractEngine GetOrCreateEngine(string language)
    {
        if (_engines.TryGetValue(language, out var existing))
        {
            return existing;
        }

        var tessDataDirectory = _pathService.ResolveTessDataDirectory();
        var trainedDataPath = Path.Combine(tessDataDirectory, $"{language}.traineddata");
        if (!File.Exists(trainedDataPath))
        {
            throw new InvalidOperationException(
                $"缺少 Tesseract 训练数据文件: {trainedDataPath}");
        }

        var engine = new TesseractEngine(tessDataDirectory, language, EngineMode.Default);
        _engines[language] = engine;
        return engine;
    }

    private static Bitmap PreprocessImage(Bitmap source, ScreenNumberReadOptions options)
    {
        var scale = Math.Clamp(options.Scale, 1, 8);
        var scaledWidth = checked(source.Width * scale);
        var scaledHeight = checked(source.Height * scale);
        var scaledBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format24bppRgb);

        using (var graphics = Graphics.FromImage(scaledBitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, scaledWidth, scaledHeight));
        }

        ApplyGrayscaleThreshold(scaledBitmap, options.Threshold, options.Invert);
        return scaledBitmap;
    }

    private static void ApplyGrayscaleThreshold(Bitmap bitmap, byte? threshold, bool invert)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var original = bitmap.GetPixel(x, y);
                var grayscale = (byte)((original.R * 299 + original.G * 587 + original.B * 114) / 1000);
                var normalized = invert ? (byte)(255 - grayscale) : grayscale;

                if (threshold.HasValue)
                {
                    normalized = normalized >= threshold.Value ? (byte)255 : (byte)0;
                }

                bitmap.SetPixel(x, y, Color.FromArgb(normalized, normalized, normalized));
            }
        }
    }

    private static bool TryParseRecognizedValue(
        string recognizedText,
        ScreenNumberReadOptions options,
        out double value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return false;
        }

        var candidates = ExtractCandidates(recognizedText, options)
            .Take(Math.Max(1, options.MaxCandidates));

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeCandidate(candidate, options.Mode);
            if (options.Mode == ScreenNumberReadMode.Integer)
            {
                if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    value = integer;
                    return true;
                }

                continue;
            }

            if (double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                value = number;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ExtractCandidates(string recognizedText, ScreenNumberReadOptions options)
    {
        var cleanedText = recognizedText
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace('I', '1')
            .Replace('l', '1')
            .Replace('S', '5');

        var pattern = options.Mode == ScreenNumberReadMode.Integer ? IntegerPattern : FloatPattern;
        return pattern.Matches(cleanedText).Select(match => match.Value);
    }

    private static string NormalizeCandidate(string candidate, ScreenNumberReadMode mode)
    {
        var normalized = candidate.Replace(',', '.');
        if (mode == ScreenNumberReadMode.Integer)
        {
            return normalized.Split('.', 2)[0];
        }

        return normalized;
    }

    private static bool IsDigitsOnly(string whitelist)
    {
        return whitelist.All(char.IsDigit);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
