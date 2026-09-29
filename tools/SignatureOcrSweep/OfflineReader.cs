using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SignatureOcrSweep;

/// <summary>
/// Offline mode: the engine's read path reimplemented locally so preprocessing can vary without an
/// engine change. Source of truth is <c>ocrx-engine/src/Ocrx.Engine/Processing/OcrPipeline.cs</c>:
/// <c>CropAndScaleAsync</c> (BMP encode with crop bounds, decode with a scaled transform, Bgra8,
/// alpha ignored, no colour management), <c>ApplyRedChannelGrayscale</c> and
/// <c>OcrEngine.RecognizeAsync</c>. With <see cref="KnobSet.Baseline"/> every step is a copy of that
/// code, which is what the gate against engine mode proves; the other knobs are additions layered
/// around it.
/// </summary>
public sealed class OfflineReader
{
    private readonly OcrEngine _engine;

    /// <param name="languageTag">BCP-47 tag, or null for the user-profile languages, as the engine does
    /// when its <c>ocrLanguage</c> is blank.</param>
    public OfflineReader(string? languageTag = null)
    {
        _engine = string.IsNullOrWhiteSpace(languageTag)
            ? OcrEngine.TryCreateFromUserProfileLanguages()
              ?? throw new InvalidOperationException("No OCR language pack matches the Windows display language.")
            : OcrEngine.TryCreateFromLanguage(new Language(languageTag))
              ?? throw new InvalidOperationException($"No OCR language pack for '{languageTag}'.");
    }

    public string Language => $"{_engine.RecognizerLanguage.DisplayName} ({_engine.RecognizerLanguage.LanguageTag})";

    public static int MaxImageDimension => (int)OcrEngine.MaxImageDimension;

    public async Task<string> ReadAsync(SoftwareBitmap frame, ReadPlan plan, KnobSet knobs)
    {
        var bounds = new BitmapBounds
        {
            X = plan.FrameRect.X,
            Y = plan.FrameRect.Y,
            Width = plan.FrameRect.Width,
            Height = plan.FrameRect.Height,
        };
        if (bounds.Width == 0 || bounds.Height == 0)
            return string.Empty; // the engine throws here; a sweep row should say blank, not abort the run

        using var crop = await CropAndScaleAsync(frame, bounds, plan.EffectiveScale, knobs);

        var pixels = new byte[4 * crop.PixelWidth * crop.PixelHeight];
        crop.CopyToBuffer(pixels.AsBuffer());
        PixelOps.ApplyPostScale(pixels, knobs);
        crop.CopyFromBuffer(pixels.AsBuffer());

        var result = await _engine.RecognizeAsync(crop);
        return result.Text;
    }

    private static async Task<SoftwareBitmap> CropAndScaleAsync(
        SoftwareBitmap source, BitmapBounds bounds, double scale, KnobSet knobs)
    {
        // Pre-sharpen works on native pixels, so it crops on the CPU first and feeds the encoder the
        // already-cropped bitmap with no bounds. Without it the encoder crops, exactly as the engine does.
        SoftwareBitmap? sharpened = null;
        try
        {
            var input = source;
            var encoderBounds = (BitmapBounds?)bounds;
            if (knobs.Sharpen > 0)
            {
                sharpened = SharpenCrop(source, bounds, knobs.Sharpen);
                input = sharpened;
                encoderBounds = null;
            }

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
            encoder.SetSoftwareBitmap(input);
            if (encoderBounds is { } b)
                encoder.BitmapTransform.Bounds = b;
            await encoder.FlushAsync();

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)(decoder.PixelWidth * scale),
                ScaledHeight = (uint)(decoder.PixelHeight * scale),
                InterpolationMode = knobs.Interpolation switch
                {
                    InterpolationKind.Cubic => BitmapInterpolationMode.Cubic,
                    InterpolationKind.Fant => BitmapInterpolationMode.Fant,
                    InterpolationKind.Linear => BitmapInterpolationMode.Linear,
                    InterpolationKind.NearestNeighbor => BitmapInterpolationMode.NearestNeighbor,
                    _ => throw new ArgumentOutOfRangeException(nameof(knobs)),
                },
            };

            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        }
        finally
        {
            sharpened?.Dispose();
        }
    }

    private static SoftwareBitmap SharpenCrop(SoftwareBitmap source, BitmapBounds bounds, double amount)
    {
        var sourcePixels = new byte[4 * source.PixelWidth * source.PixelHeight];
        source.CopyToBuffer(sourcePixels.AsBuffer());

        int x = (int)bounds.X, y = (int)bounds.Y, w = (int)bounds.Width, h = (int)bounds.Height;
        var cropped = new byte[4 * w * h];
        for (var row = 0; row < h; row++)
            Array.Copy(sourcePixels, 4 * ((y + row) * source.PixelWidth + x), cropped, 4 * row * w, 4 * w);

        var result = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
        result.CopyFromBuffer(PixelOps.UnsharpMask(cropped, w, h, amount).AsBuffer());
        return result;
    }
}
